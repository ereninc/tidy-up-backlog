using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EXW.SaveSystem.Tests
{
    public sealed class SaveFileStoreTests
    {
        private SaveSystemSettings _settings;
        private SaveFileStore _store;
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<SaveSystemSettings>();
            string folderName = $"EXWSaveTests_{Guid.NewGuid():N}";
            var serializedSettings = new SerializedObject(_settings);
            serializedSettings.FindProperty("folderName").stringValue = folderName;
            serializedSettings.FindProperty("backupCount").intValue = 2;
            serializedSettings.FindProperty("compressPayload").boolValue = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            _directory = _settings.GetSaveDirectory();
            _store = new SaveFileStore(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            _store.Dispose();
            UnityEngine.Object.DestroyImmediate(_settings);

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public async Task RoundTripPreservesMetadataContextAndSections()
        {
            SaveSlotMetadata metadata = CreateMetadata("slot_1");
            metadata.Context["game.summary"] = JObject.FromObject(new { day = 7, money = 1250L });
            SaveDocument document = CreateDocument(7);

            SaveOperationResult write = await _store.WriteAsync(metadata, document);
            SaveReadResult read = await _store.ReadAsync("slot_1");

            Assert.That(write.Success, Is.True, write.Message);
            Assert.That(read.Success, Is.True, read.Message);
            Assert.That(read.Slot.Source, Is.EqualTo(SaveFileSource.Primary));
            Assert.That(read.Slot.Metadata.Sequence, Is.EqualTo(1));
            Assert.That((int)read.Document.Sections["game.state"].Data["day"], Is.EqualTo(7));
            Assert.That((long)read.Slot.Metadata.Context["game.summary"]["money"], Is.EqualTo(1250L));
        }

        [Test]
        public async Task CorruptPrimaryAutomaticallyFallsBackToBackup()
        {
            SaveSlotMetadata metadata = CreateMetadata("slot_2");
            Assert.That((await _store.WriteAsync(metadata, CreateDocument(1))).Success, Is.True);
            Assert.That((await _store.WriteAsync(metadata, CreateDocument(2))).Success, Is.True);

            string primary = Path.Combine(_directory, "slot_slot_2.sav");

            using (var stream = new FileStream(primary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Position = stream.Length - 1;
                int value = stream.ReadByte();
                stream.Position = stream.Length - 1;
                stream.WriteByte((byte)(value ^ 0xFF));
                stream.Flush(flushToDisk: true);
            }

            SaveReadResult read = await _store.ReadAsync("slot_2");

            Assert.That(read.Success, Is.True, read.Message);
            Assert.That(read.Slot.Source, Is.EqualTo(SaveFileSource.Backup));
            Assert.That(read.Slot.Recovered, Is.True);
            Assert.That((int)read.Document.Sections["game.state"].Data["day"], Is.EqualTo(1));
        }

        [Test]
        public async Task InterruptedPromotionCanLoadPendingGeneration()
        {
            SaveSlotMetadata metadata = CreateMetadata("slot_3");
            Assert.That((await _store.WriteAsync(metadata, CreateDocument(4))).Success, Is.True);

            string primary = Path.Combine(_directory, "slot_slot_3.sav");
            string pending = Path.Combine(_directory, "slot_slot_3.pending");
            File.Move(primary, pending);

            SaveReadResult read = await _store.ReadAsync("slot_3");

            Assert.That(read.Success, Is.True, read.Message);
            Assert.That(read.Slot.Source, Is.EqualTo(SaveFileSource.Pending));
            Assert.That((int)read.Document.Sections["game.state"].Data["day"], Is.EqualTo(4));
        }

        [Test]
        public async Task ConcurrentStoreWritesAreSerialized()
        {
            var writes = new List<Task<SaveOperationResult>>();

            for (int i = 1; i <= 5; i++)
            {
                writes.Add(_store.WriteAsync(CreateMetadata("slot_4"), CreateDocument(i)));
            }

            SaveOperationResult[] results = await Task.WhenAll(writes);
            SaveReadResult read = await _store.ReadAsync("slot_4");

            for (int i = 0; i < results.Length; i++)
            {
                Assert.That(results[i].Success, Is.True, results[i].Message);
            }

            Assert.That(read.Success, Is.True, read.Message);
            Assert.That(read.Slot.Metadata.Sequence, Is.EqualTo(5));
        }

        [Test]
        public void SerializerUsesCompactUnityValueConverters()
        {
            var serializer = new NewtonsoftSaveSerializer();
            var source = new UnityValueData
            {
                Position = new Vector3(1.25f, -2f, 9.5f),
                Tint = new Color(0.1f, 0.2f, 0.3f, 0.4f)
            };

            string json = serializer.Serialize(source, pretty: false);
            UnityValueData restored = serializer.Deserialize<UnityValueData>(json);

            Assert.That(json, Does.Not.Contain("magnitude"));
            Assert.That(restored.Position, Is.EqualTo(source.Position));
            Assert.That(restored.Tint, Is.EqualTo(source.Tint));
        }

        private static SaveSlotMetadata CreateMetadata(string slotId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new SaveSlotMetadata
            {
                SlotId = slotId,
                DisplayName = slotId,
                CreatedAtUnixMs = now,
                LastSavedAtUnixMs = now,
                SceneName = "Game",
                GameVersion = "test",
                Reason = SaveReason.Manual
            };
        }

        private static SaveDocument CreateDocument(int day)
        {
            return new SaveDocument
            {
                Sections = new Dictionary<string, SaveSectionRecord>
                {
                    ["game.state"] = new SaveSectionRecord
                    {
                        Version = 1,
                        Data = JObject.FromObject(new { day })
                    }
                }
            };
        }

        private sealed class UnityValueData
        {
            public Vector3 Position;
            public Color Tint;
        }
    }
}

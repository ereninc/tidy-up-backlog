using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace EXW.SaveSystem
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        private sealed class CapturedValue
        {
            public string Key;
            public int Version;
            public object Value;
        }

        private sealed class PendingSaveBatch
        {
            public SaveReason Reason;
            public readonly List<TaskCompletionSource<SaveOperationResult>> Waiters =
                new List<TaskCompletionSource<SaveOperationResult>>();
        }

        private sealed class PreparedProviderState
        {
            public ISaveSection Provider;
            public object State;
        }

        public static SaveManager Instance { get; private set; }

        [SerializeField] private SaveSystemSettings settings;
        [SerializeField] private bool persistBetweenScenes = true;

        private ISaveSerializer _serializer;
        private SaveFileStore _fileStore;
        private SaveSystemSettings _runtimeDefaultSettings;
        private SaveDocument _activeDocument;
        private SaveSlotMetadata _activeMetadata;
        private SaveReadResult _preparedLoad;
        private PendingSaveBatch _pendingSave;
        private bool _saveLoopRunning;
        private long _changeRevision;
        private long _savedRevision;
        private int _mainThreadId;

        public bool HasActiveSlot => _activeMetadata != null;
        public bool HasPreparedLoad => _preparedLoad != null && _preparedLoad.Success;
        public bool IsSaving => _saveLoopRunning;
        public bool IsDirty => HasActiveSlot && _changeRevision != _savedRevision;
        public string ActiveSlotId => _activeMetadata?.SlotId ?? string.Empty;
        public SaveSlotMetadata ActiveMetadata => _activeMetadata?.DeepClone();
        public SaveSystemSettings Settings => settings != null ? settings : _runtimeDefaultSettings;

        public event Action<SaveOperationResult> SaveFinished;
        public event Action<SaveOperationResult> LoadPrepared;
        public event Action<SaveOperationResult> RestoreFinished;
        public event Action ActiveSlotChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            if (persistBetweenScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (settings == null)
            {
                _runtimeDefaultSettings = SaveSystemSettings.CreateRuntimeDefault();
                Debug.LogWarning(
                    "SaveManager has no SaveSystemSettings asset. Runtime defaults will be used.",
                    this);
            }

            _serializer = new NewtonsoftSaveSerializer();
            _fileStore = new SaveFileStore(Settings, _serializer, new GZipSaveCompressor());
        }

        private void Update()
        {
            if (_activeMetadata != null)
            {
                _activeMetadata.PlayTimeSeconds += Time.unscaledDeltaTime;
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            _fileStore?.Dispose();

            if (_runtimeDefaultSettings != null)
            {
                Destroy(_runtimeDefaultSettings);
            }

            Instance = null;
        }

        public SaveOperationResult BeginNewGame(string slotId, string displayName)
        {
            if (!IsMainThread())
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "BeginNewGame must be called on Unity's main thread.");
            }

            if (_saveLoopRunning)
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "A new game cannot begin while a save is running.");
            }

            if (!SaveKeyValidator.IsValidSlotId(slotId))
            {
                return SaveOperationResult.Failed(
                    SaveError.InvalidSlotId,
                    "Slot ids may contain lowercase letters, numbers, '_' and '-' only.");
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _activeDocument = new SaveDocument();
            _activeMetadata = new SaveSlotMetadata
            {
                SlotId = slotId,
                DisplayName = NormalizeDisplayName(displayName, slotId),
                CreatedAtUnixMs = now,
                LastSavedAtUnixMs = now,
                PlayTimeSeconds = 0,
                SceneName = SceneManager.GetActiveScene().name,
                GameVersion = Application.version,
                Reason = SaveReason.NewGame
            };

            _preparedLoad = null;
            _changeRevision = 1;
            _savedRevision = 0;
            InvokeSafely(ActiveSlotChanged);

            return SaveOperationResult.Succeeded(
                new SaveSlotInfo(_activeMetadata.DeepClone(), SaveFileSource.Primary),
                "New game state created in memory. Call SaveActiveSlotAsync to write it.");
        }

        public SaveOperationResult SetActiveDisplayName(string displayName)
        {
            if (!IsMainThread())
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "SetActiveDisplayName must run on Unity's main thread.");
            }

            if (_saveLoopRunning)
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "The slot display name cannot change while a save is running.");
            }

            if (_activeMetadata == null)
            {
                return SaveOperationResult.Failed(SaveError.NoActiveSlot, "There is no active slot.");
            }

            _activeMetadata.DisplayName = NormalizeDisplayName(displayName, _activeMetadata.SlotId);
            MarkDirty();
            return SaveOperationResult.Succeeded(
                new SaveSlotInfo(_activeMetadata.DeepClone(), SaveFileSource.Primary));
        }

        public void MarkDirty()
        {
            if (_activeMetadata != null)
            {
                Interlocked.Increment(ref _changeRevision);
            }
        }

        public Task<SaveOperationResult> SaveActiveSlotAsync(SaveReason reason)
        {
            if (!IsMainThread())
            {
                return Task.FromResult(SaveOperationResult.Failed(
                    SaveError.Busy,
                    "Save requests must be created on Unity's main thread."));
            }
            
            if (HasPreparedLoad)
            {
                return Task.FromResult(
                    SaveOperationResult.Failed(
                        SaveError.Busy,
                        "A save cannot begin while a prepared load is waiting to be restored."));
            }

            if (_activeMetadata == null || _activeDocument == null)
            {
                return Task.FromResult(SaveOperationResult.Failed(
                    SaveError.NoActiveSlot,
                    "There is no active slot. Begin a new game or restore a prepared load first."));
            }

            var completion = new TaskCompletionSource<SaveOperationResult>();

            if (_saveLoopRunning)
            {
                _pendingSave ??= new PendingSaveBatch();
                _pendingSave.Reason = reason;
                _pendingSave.Waiters.Add(completion);
                return completion.Task;
            }

            var firstBatch = new PendingSaveBatch { Reason = reason };
            firstBatch.Waiters.Add(completion);
            _saveLoopRunning = true;
            RunSaveLoop(firstBatch);
            return completion.Task;
        }

        public async Task<SaveCatalogResult> GetSlotsAsync()
        {
            return await _fileStore.ListAsync();
        }

        public async Task<SaveOperationResult> PrepareLoadSlotAsync(string slotId)
        {
            if (!IsMainThread())
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "Load requests must be created on Unity's main thread.");
            }

            if (_saveLoopRunning)
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "A slot cannot be loaded while a save is running.");
            }

            SaveReadResult read = await _fileStore.ReadAsync(slotId);

            if (!read.Success)
            {
                SaveOperationResult failure = SaveOperationResult.Failed(read.Error, read.Message);
                InvokeSafely(LoadPrepared, failure);
                return failure;
            }

            _preparedLoad = read;
            SaveOperationResult result = SaveOperationResult.Succeeded(
                read.Slot,
                read.Slot.Recovered
                    ? "A recovery generation was prepared because the primary file was unavailable."
                    : "Slot prepared. Load its scene, then call RestorePreparedLoad.");

            InvokeSafely(LoadPrepared, result);
            return result;
        }

        public SaveOperationResult RestorePreparedLoad()
        {
            if (!IsMainThread())
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "RestorePreparedLoad must run on Unity's main thread.");
            }

            if (_preparedLoad == null || !_preparedLoad.Success)
            {
                return SaveOperationResult.Failed(
                    SaveError.NotFound,
                    "No slot has been prepared for loading.");
            }

            SaveDocument document = _preparedLoad.Document.DeepClone();
            SaveSlotMetadata metadata = _preparedLoad.Slot.Metadata.DeepClone();
            var preparedStates = new List<PreparedProviderState>();
            bool migrated = false;

            try
            {
                ISaveSection[] providers = SaveProviderRegistry.GetSections()
                    .OrderBy(provider => provider.RestoreOrder)
                    .ThenBy(provider => provider.Key, StringComparer.Ordinal)
                    .ToArray();

                for (int i = 0; i < providers.Length; i++)
                {
                    ISaveSection provider = providers[i];
                    object state;

                    if (document.Sections.TryGetValue(provider.Key, out SaveSectionRecord record))
                    {
                        JToken migratedToken = SaveMigrationRunner.Run(provider, record);
                        migrated |= record.Version != provider.CurrentVersion;
                        document.Sections[provider.Key] = new SaveSectionRecord
                        {
                            Version = provider.CurrentVersion,
                            Data = migratedToken
                        };

                        state = provider.DeserializeState(migratedToken, _serializer);
                    }
                    else
                    {
                        state = provider.CreateDefaultState();
                    }

                    preparedStates.Add(new PreparedProviderState
                    {
                        Provider = provider,
                        State = state
                    });
                }
            }
            catch (Exception exception)
            {
                SaveOperationResult failure = SaveOperationResult.Failed(
                    exception.Message.IndexOf("supports version", StringComparison.Ordinal) >= 0
                        ? SaveError.IncompatibleVersion
                        : SaveError.RestoreFailed,
                    exception.Message);

                InvokeSafely(RestoreFinished, failure);
                return failure;
            }

            _activeDocument = document;
            _activeMetadata = metadata;
            _changeRevision = migrated ? 1 : 0;
            _savedRevision = 0;

            try
            {
                for (int i = 0; i < preparedStates.Count; i++)
                {
                    preparedStates[i].Provider.RestoreState(preparedStates[i].State);
                }
            }
            catch (Exception exception)
            {
                _preparedLoad = null;
                SaveOperationResult failure = SaveOperationResult.Failed(
                    SaveError.RestoreFailed,
                    $"Provider restore failed after validation: {exception.Message}");

                InvokeSafely(RestoreFinished, failure);
                return failure;
            }

            SaveSlotInfo slot = _preparedLoad.Slot;
            _preparedLoad = null;
            InvokeSafely(ActiveSlotChanged);

            SaveOperationResult success = SaveOperationResult.Succeeded(
                new SaveSlotInfo(metadata.DeepClone(), slot.Source, slot.BackupIndex),
                migrated
                    ? "Slot restored and migrated. It is marked dirty for the next save."
                    : "Slot restored.");

            InvokeSafely(RestoreFinished, success);
            return success;
        }

        public void CancelPreparedLoad()
        {
            if (!IsMainThread())
            {
                Debug.LogError("CancelPreparedLoad must run on Unity's main thread.", this);
                return;
            }

            _preparedLoad = null;
        }

        public async Task<SaveOperationResult> DeleteSlotAsync(string slotId)
        {
            if (!IsMainThread())
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "Delete requests must be created on Unity's main thread.");
            }

            if (_saveLoopRunning)
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "A slot cannot be deleted while a save is running.");
            }

            SaveOperationResult result = await _fileStore.DeleteAsync(slotId);

            if (result.Success &&
                _preparedLoad != null &&
                _preparedLoad.Slot != null &&
                string.Equals(
                    _preparedLoad.Slot.Metadata.SlotId,
                    slotId,
                    StringComparison.Ordinal))
            {
                _preparedLoad = null;
            }

            if (result.Success &&
                _activeMetadata != null &&
                string.Equals(_activeMetadata.SlotId, slotId, StringComparison.Ordinal))
            {
                _activeMetadata = null;
                _activeDocument = null;
                _changeRevision = 0;
                _savedRevision = 0;
                InvokeSafely(ActiveSlotChanged);
            }

            return result;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public async Task<SaveDebugReadResult> DebugReadSlotAsync(string slotId)
        {
            SaveReadResult read = await _fileStore.ReadAsync(slotId);

            if (!read.Success)
            {
                return SaveDebugReadResult.Failed(read.Error, read.Message);
            }

            try
            {
                string json = _serializer.Serialize(
                    new SaveDebugDocument
                    {
                        Metadata = read.Slot.Metadata,
                        Document = read.Document
                    },
                    pretty: true);

                return SaveDebugReadResult.Succeeded(json);
            }
            catch (Exception exception)
            {
                return SaveDebugReadResult.Failed(SaveError.SerializationFailed, exception.Message);
            }
        }

        public async Task<SaveOperationResult> DebugWriteSlotAsync(string json)
        {
            if (!IsMainThread())
            {
                return SaveOperationResult.Failed(
                    SaveError.Busy,
                    "Debug writes must be created on Unity's main thread.");
            }

            if (_saveLoopRunning)
            {
                return SaveOperationResult.Failed(SaveError.Busy, "Wait for the active save to finish.");
            }

            SaveDebugDocument debugDocument;

            try
            {
                debugDocument = _serializer.Deserialize<SaveDebugDocument>(json);

                if (debugDocument.Metadata == null || debugDocument.Document == null)
                {
                    throw new JsonSerializationException("Metadata and document are required.");
                }
            }
            catch (Exception exception)
            {
                return SaveOperationResult.Failed(SaveError.SerializationFailed, exception.Message);
            }

            debugDocument.Metadata.LastSavedAtUnixMs =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            debugDocument.Metadata.Reason = SaveReason.DebugEdit;

            SaveOperationResult result = await _fileStore.WriteAsync(
                debugDocument.Metadata,
                debugDocument.Document);

            if (result.Success &&
                _activeMetadata != null &&
                string.Equals(
                    _activeMetadata.SlotId,
                    debugDocument.Metadata.SlotId,
                    StringComparison.Ordinal))
            {
                _activeMetadata = result.Slot.Metadata.DeepClone();
                _activeDocument = debugDocument.Document.DeepClone();
                _changeRevision = 0;
                _savedRevision = 0;
            }

            return result;
        }
#endif

        private async void RunSaveLoop(PendingSaveBatch currentBatch)
        {
            while (currentBatch != null)
            {
                SaveOperationResult result;

                try
                {
                    result = await ExecuteSaveAsync(currentBatch.Reason);
                }
                catch (Exception exception)
                {
                    result = SaveOperationResult.Failed(
                        SaveError.IoFailed,
                        exception.Message);
                }

                // Event çalışırken SaveManager hâlâ saving durumunda kalır.
                // Event içerisinden gelen yeni save istekleri pending batch'e eklenir.
                InvokeSafely(SaveFinished, result);

                PendingSaveBatch nextBatch = _pendingSave;
                _pendingSave = null;

                bool hasNextBatch = nextBatch != null;

                // Son save generation tamamlandıysa await eden kod devam etmeden
                // önce sistem artık idle olarak işaretlenir.
                if (!hasNextBatch)
                {
                    _saveLoopRunning = false;
                }

                for (int i = 0; i < currentBatch.Waiters.Count; i++)
                {
                    currentBatch.Waiters[i].TrySetResult(result);
                }

                if (!hasNextBatch)
                {
                    return;
                }

                currentBatch = nextBatch;
            }

            _saveLoopRunning = false;
        }

        private async Task<SaveOperationResult> ExecuteSaveAsync(SaveReason reason)
        {
            long capturedRevision = _changeRevision;
            SaveSlotMetadata metadata = _activeMetadata.DeepClone();
            SaveDocument activeDocument = _activeDocument;
            SaveDocument document = null;
            IReadOnlyList<ISaveSection> providers = SaveProviderRegistry.GetSections();
            IReadOnlyList<ISaveContextProvider> contextProviders =
                SaveProviderRegistry.GetContextProviders();

            var capturedSections = new List<CapturedValue>(providers.Count);
            var capturedContexts = new List<CapturedValue>(contextProviders.Count);

            try
            {
                for (int i = 0; i < providers.Count; i++)
                {
                    ISaveSection provider = providers[i];
                    capturedSections.Add(new CapturedValue
                    {
                        Key = provider.Key,
                        Version = provider.CurrentVersion,
                        Value = provider.CaptureState()
                    });
                }

                for (int i = 0; i < contextProviders.Count; i++)
                {
                    ISaveContextProvider provider = contextProviders[i];
                    capturedContexts.Add(new CapturedValue
                    {
                        Key = provider.Key,
                        Value = provider.CaptureContext()
                    });
                }
            }
            catch (Exception exception)
            {
                return SaveOperationResult.Failed(
                    SaveError.CaptureFailed,
                    $"Save capture failed: {exception.Message}");
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            metadata.LastSavedAtUnixMs = now;
            metadata.CreatedAtUnixMs = metadata.CreatedAtUnixMs == 0 ? now : metadata.CreatedAtUnixMs;
            metadata.PlayTimeSeconds = Math.Round(Math.Max(0d, _activeMetadata.PlayTimeSeconds), 2, MidpointRounding.AwayFromZero);
            metadata.SceneName = SceneManager.GetActiveScene().name;
            metadata.GameVersion = Application.version;
            metadata.Reason = reason;

            try
            {
                await Task.Run(() =>
                {
                    document = activeDocument.DeepClone();

                    for (int i = 0; i < capturedSections.Count; i++)
                    {
                        CapturedValue captured = capturedSections[i];
                        document.Sections[captured.Key] = new SaveSectionRecord
                        {
                            Version = captured.Version,
                            Data = _serializer.ToToken(captured.Value)
                        };
                    }

                    for (int i = 0; i < capturedContexts.Count; i++)
                    {
                        CapturedValue captured = capturedContexts[i];
                        metadata.Context[captured.Key] = _serializer.ToToken(captured.Value);
                    }
                });
            }
            catch (Exception exception)
            {
                return SaveOperationResult.Failed(
                    SaveError.SerializationFailed,
                    $"Save serialization failed: {exception.Message}");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            SaveOperationResult result = await _fileStore.WriteAsync(metadata, document);
            stopwatch.Stop();

            if (!result.Success)
            {
                return result;
            }

            double currentPlayTime = _activeMetadata.PlayTimeSeconds;
            _activeDocument = document;
            _activeMetadata = result.Slot.Metadata.DeepClone();
            _activeMetadata.PlayTimeSeconds = Math.Max(
                currentPlayTime,
                _activeMetadata.PlayTimeSeconds);
            _savedRevision = Math.Max(_savedRevision, capturedRevision);

            return SaveOperationResult.Succeeded(
                result.Slot,
                $"Saved in {stopwatch.ElapsedMilliseconds} ms.");
        }

        private bool IsMainThread()
        {
            return Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private static string NormalizeDisplayName(string value, string fallback)
        {
            string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return result.Length <= 80 ? result : result.Substring(0, 80);
        }

        private static void InvokeSafely(Action handlers)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] invocationList = handlers.GetInvocationList();

            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action)invocationList[i]).Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static void InvokeSafely(
            Action<SaveOperationResult> handlers,
            SaveOperationResult result)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] invocationList = handlers.GetInvocationList();

            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<SaveOperationResult>)invocationList[i]).Invoke(result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EXW.SaveSystem
{
    public sealed class SaveFileStore : ISaveFileStore, IDisposable
    {
        private const int ContainerVersion = 1;
        private const int HashLength = 32;
        private const int MaximumHeaderBytes = 1024 * 1024;
        private const int FixedPreambleBytes = 8 + 4 + 1 + 4 + 8 + HashLength + HashLength;
        private const byte CompressedFlag = 1 << 0;

        private static readonly byte[] Magic =
        {
            (byte)'E', (byte)'X', (byte)'W', (byte)'S',
            (byte)'A', (byte)'V', (byte)'E', (byte)'1'
        };

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryGates =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private readonly SaveStorageOptions _options;
        private readonly ISaveSerializer _serializer;
        private readonly ISaveCompressor _compressor;
        private readonly SemaphoreSlim _ioGate;
        private bool _disposed;

        public SaveFileStore(
            SaveSystemSettings settings,
            ISaveSerializer serializer = null,
            ISaveCompressor compressor = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _options = settings.CreateStorageOptions();
            _serializer = serializer ?? new NewtonsoftSaveSerializer();
            _compressor = compressor ?? new GZipSaveCompressor();
            _ioGate = DirectoryGates.GetOrAdd(
                Path.GetFullPath(_options.DirectoryPath),
                _ => new SemaphoreSlim(1, 1));
        }

        public async Task<SaveOperationResult> WriteAsync(
            SaveSlotMetadata metadata,
            SaveDocument document)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (_disposed)
            {
                return SaveOperationResult.Failed(SaveError.IoFailed, "Save file store is disposed.");
            }

            if (!SaveKeyValidator.IsValidSlotId(metadata.SlotId))
            {
                return SaveOperationResult.Failed(
                    SaveError.InvalidSlotId,
                    $"'{metadata.SlotId}' is not a valid slot id.");
            }

            await _ioGate.WaitAsync();

            try
            {
                return await Task.Run(() => WriteCore(metadata, document));
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public async Task<SaveReadResult> ReadAsync(string slotId)
        {
            if (_disposed)
            {
                return SaveReadResult.Failed(SaveError.IoFailed, "Save file store is disposed.");
            }

            if (!SaveKeyValidator.IsValidSlotId(slotId))
            {
                return SaveReadResult.Failed(
                    SaveError.InvalidSlotId,
                    $"'{slotId}' is not a valid slot id.");
            }

            await _ioGate.WaitAsync();

            try
            {
                return await Task.Run(() => ReadCore(slotId));
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public async Task<SaveCatalogResult> ListAsync()
        {
            if (_disposed)
            {
                return SaveCatalogResult.Failed(SaveError.IoFailed, "Save file store is disposed.");
            }

            await _ioGate.WaitAsync();

            try
            {
                return await Task.Run(ListCore);
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public async Task<SaveOperationResult> DeleteAsync(string slotId)
        {
            if (_disposed)
            {
                return SaveOperationResult.Failed(SaveError.IoFailed, "Save file store is disposed.");
            }

            if (!SaveKeyValidator.IsValidSlotId(slotId))
            {
                return SaveOperationResult.Failed(
                    SaveError.InvalidSlotId,
                    $"'{slotId}' is not a valid slot id.");
            }

            await _ioGate.WaitAsync();

            try
            {
                return await Task.Run(() => DeleteCore(slotId));
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public void Dispose()
        {
            // Do not dispose the gate while an awaited background write may still release it.
            // The store owns no persistent OS handle; active FileStreams are scoped per operation.
            _disposed = true;
        }

        private SaveOperationResult WriteCore(SaveSlotMetadata sourceMetadata, SaveDocument sourceDocument)
        {
            try
            {
                Directory.CreateDirectory(_options.DirectoryPath);
                using FileStream transactionLock = AcquireTransactionLock();

                SaveSlotMetadata metadata = sourceMetadata.DeepClone();
                SaveDocument document = sourceDocument.DeepClone();

                if (document.FormatVersion != SaveDocument.CurrentFormatVersion)
                {
                    return SaveOperationResult.Failed(
                        SaveError.IncompatibleVersion,
                        $"Cannot write document format {document.FormatVersion}.");
                }

                ValidateDocument(document);

                long highestSequence = FindHighestReadableSequence(metadata.SlotId);
                metadata.Sequence = Math.Max(metadata.Sequence, highestSequence) + 1;

                byte[] headerBytes = Utf8.GetBytes(_serializer.Serialize(metadata, pretty: false));
                byte[] documentBytes = Utf8.GetBytes(_serializer.Serialize(document, pretty: false));

                if (headerBytes.Length > MaximumHeaderBytes)
                {
                    return SaveOperationResult.Failed(
                        SaveError.SerializationFailed,
                        "Save metadata exceeded the header safety limit.");
                }

                if (documentBytes.Length > _options.MaximumPayloadBytes)
                {
                    return SaveOperationResult.Failed(
                        SaveError.SerializationFailed,
                        $"Save payload is {documentBytes.Length} bytes and exceeds the configured limit.");
                }

                byte flags = 0;
                byte[] storedPayload = documentBytes;

                if (_options.CompressPayload)
                {
                    storedPayload = _compressor.Compress(documentBytes);
                    flags |= CompressedFlag;
                }

                string pendingPath = GetPendingPath(metadata.SlotId);
                WriteContainer(pendingPath, flags, headerBytes, storedPayload);

                Candidate pending = ReadHeader(pendingPath, SaveFileSource.Pending, 0);
                ReadPayload(pending, deserializeDocument: true);

                Candidate previous = FindBestCandidate(metadata.SlotId, pendingPath, deserializeDocument: false);
                RotateBackups(metadata.SlotId, previous);
                PromotePending(metadata.SlotId);

                Candidate primary = ReadHeader(GetPrimaryPath(metadata.SlotId), SaveFileSource.Primary, 0);
                ReadPayload(primary, deserializeDocument: true);

                sourceMetadata.Sequence = metadata.Sequence;
                SaveSlotInfo info = CreateInfo(primary);
                return SaveOperationResult.Succeeded(info);
            }
            catch (Exception exception) when (IsSerializationException(exception))
            {
                return SaveOperationResult.Failed(SaveError.SerializationFailed, exception.Message);
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveOperationResult.Failed(SaveError.IoFailed, exception.Message);
            }
            catch (Exception exception)
            {
                return SaveOperationResult.Failed(SaveError.SerializationFailed, exception.Message);
            }
        }

        private SaveReadResult ReadCore(string slotId)
        {
            try
            {
                Candidate candidate = FindBestCandidate(slotId, excludedPath: null, deserializeDocument: true);

                if (candidate == null)
                {
                    return HasAnySlotFile(slotId)
                        ? SaveReadResult.Failed(
                            SaveError.CorruptData,
                            "The primary save and all recovery copies are invalid.")
                        : SaveReadResult.Failed(SaveError.NotFound, $"Slot '{slotId}' does not exist.");
                }

                SaveDocument document = candidate.Document;

                if (document.FormatVersion != SaveDocument.CurrentFormatVersion)
                {
                    return SaveReadResult.Failed(
                        SaveError.IncompatibleVersion,
                        $"Save document format {document.FormatVersion} is not supported by this build.");
                }

                return SaveReadResult.Succeeded(CreateInfo(candidate), document);
            }
            catch (Exception exception) when (IsSerializationException(exception))
            {
                return SaveReadResult.Failed(SaveError.CorruptData, exception.Message);
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveReadResult.Failed(SaveError.IoFailed, exception.Message);
            }
            catch (Exception exception)
            {
                return SaveReadResult.Failed(SaveError.CorruptData, exception.Message);
            }
        }

        private SaveCatalogResult ListCore()
        {
            try
            {
                if (!Directory.Exists(_options.DirectoryPath))
                {
                    return SaveCatalogResult.Succeeded(Array.Empty<SaveSlotInfo>());
                }

                string[] slotIds = FindKnownSlotIds();
                var slots = new List<SaveSlotInfo>(slotIds.Length);

                for (int i = 0; i < slotIds.Length; i++)
                {
                    string slotId = slotIds[i];

                    try
                    {
                        Candidate candidate = FindBestCandidate(
                            slotId,
                            excludedPath: null,
                            deserializeDocument: false);

                        slots.Add(candidate != null
                            ? CreateInfo(candidate)
                            : SaveSlotInfo.Damaged(
                                slotId,
                                "The primary save and all recovery copies are invalid."));
                    }
                    catch (Exception exception)
                    {
                        slots.Add(SaveSlotInfo.Damaged(slotId, exception.Message));
                    }
                }

                SaveSlotInfo[] ordered = slots
                    .OrderByDescending(slot => slot.IsValid)
                    .ThenByDescending(slot => slot.Metadata.LastSavedAtUnixMs)
                    .ThenBy(slot => slot.Metadata.SlotId, StringComparer.Ordinal)
                    .ToArray();

                return SaveCatalogResult.Succeeded(ordered);
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveCatalogResult.Failed(SaveError.IoFailed, exception.Message);
            }
            catch (Exception exception)
            {
                return SaveCatalogResult.Failed(SaveError.CorruptData, exception.Message);
            }
        }

        private SaveOperationResult DeleteCore(string slotId)
        {
            try
            {
                Directory.CreateDirectory(_options.DirectoryPath);
                using FileStream transactionLock = AcquireTransactionLock();
                bool found = false;

                foreach (CandidatePath candidate in GetCandidatePaths(slotId))
                {
                    if (!File.Exists(candidate.Path))
                    {
                        continue;
                    }

                    found = true;
                    ExecuteWithIoRetry(() => File.Delete(candidate.Path));
                }

                if (!found)
                {
                    return SaveOperationResult.Failed(
                        SaveError.NotFound,
                        $"Slot '{slotId}' does not exist.");
                }

                return SaveOperationResult.Succeeded(
                    new SaveSlotInfo(
                        new SaveSlotMetadata { SlotId = slotId, DisplayName = slotId },
                        SaveFileSource.Primary),
                    "Slot deleted.");
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveOperationResult.Failed(SaveError.IoFailed, exception.Message);
            }
            catch (Exception exception)
            {
                return SaveOperationResult.Failed(SaveError.IoFailed, exception.Message);
            }
        }

        private void WriteContainer(
            string path,
            byte flags,
            byte[] headerBytes,
            byte[] payloadBytes)
        {
            byte[] headerHash = ComputeHash(headerBytes);
            byte[] payloadHash = ComputeHash(payloadBytes);

            ExecuteWithIoRetry(() =>
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.SequentialScan);

                using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
                writer.Write(Magic);
                writer.Write(ContainerVersion);
                writer.Write(flags);
                writer.Write(headerBytes.Length);
                writer.Write((long)payloadBytes.Length);
                writer.Write(headerHash);
                writer.Write(payloadHash);
                writer.Write(headerBytes);
                writer.Write(payloadBytes);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            });
        }

        private FileStream AcquireTransactionLock()
        {
            string lockPath = Path.Combine(_options.DirectoryPath, ".save.lock");

            return ExecuteWithIoRetry(() => new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.None));
        }

        private Candidate FindBestCandidate(
            string slotId,
            string excludedPath,
            bool deserializeDocument)
        {
            var readableHeaders = new List<Candidate>();

            foreach (CandidatePath candidatePath in GetCandidatePaths(slotId))
            {
                if (!File.Exists(candidatePath.Path) ||
                    string.Equals(candidatePath.Path, excludedPath, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    Candidate candidate = ReadHeader(
                        candidatePath.Path,
                        candidatePath.Source,
                        candidatePath.BackupIndex);

                    if (string.Equals(candidate.Metadata.SlotId, slotId, StringComparison.Ordinal))
                    {
                        readableHeaders.Add(candidate);
                    }
                }
                catch
                {
                    // A corrupt candidate must never prevent recovery from another generation.
                }
            }

            Candidate[] ordered = readableHeaders
                .OrderByDescending(candidate => candidate.Metadata.Sequence)
                .ThenBy(candidate => SourcePriority(candidate.Source))
                .ThenBy(candidate => candidate.BackupIndex)
                .ToArray();

            for (int i = 0; i < ordered.Length; i++)
            {
                try
                {
                    ReadPayload(ordered[i], deserializeDocument);
                    return ordered[i];
                }
                catch
                {
                    // Continue with an older backup if this payload is incomplete or damaged.
                }
            }

            return null;
        }

        private Candidate ReadHeader(string path, SaveFileSource source, int backupIndex)
        {
            return ExecuteWithIoRetry(() =>
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.SequentialScan);

                using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
                byte[] magic = ReadExactly(reader, Magic.Length);

                if (!BytesEqual(magic, Magic))
                {
                    throw new InvalidDataException("Save magic is invalid.");
                }

                int containerVersion = reader.ReadInt32();

                if (containerVersion != ContainerVersion)
                {
                    throw new InvalidDataException(
                        $"Container version {containerVersion} is not supported.");
                }

                byte flags = reader.ReadByte();

                if ((flags & ~CompressedFlag) != 0)
                {
                    throw new InvalidDataException("Save container contains unknown flags.");
                }

                int headerLength = reader.ReadInt32();
                long payloadLength = reader.ReadInt64();

                if (headerLength <= 0 || headerLength > MaximumHeaderBytes)
                {
                    throw new InvalidDataException("Save header length is invalid.");
                }

                long maximumStoredPayload = (long)_options.MaximumPayloadBytes + 1024 * 1024;

                if (payloadLength <= 0 || payloadLength > maximumStoredPayload)
                {
                    throw new InvalidDataException("Stored save payload length is invalid.");
                }

                long expectedLength = FixedPreambleBytes + headerLength + payloadLength;

                if (stream.Length != expectedLength)
                {
                    throw new InvalidDataException("Save file is incomplete or has trailing data.");
                }

                byte[] expectedHeaderHash = ReadExactly(reader, HashLength);
                byte[] expectedPayloadHash = ReadExactly(reader, HashLength);
                byte[] headerBytes = ReadExactly(reader, headerLength);

                if (!BytesEqual(ComputeHash(headerBytes), expectedHeaderHash))
                {
                    throw new InvalidDataException("Save metadata checksum failed.");
                }

                SaveSlotMetadata metadata = _serializer.Deserialize<SaveSlotMetadata>(
                    Utf8.GetString(headerBytes));

                ValidateMetadata(metadata);

                return new Candidate
                {
                    Path = path,
                    Source = source,
                    BackupIndex = backupIndex,
                    Flags = flags,
                    PayloadOffset = FixedPreambleBytes + headerLength,
                    PayloadLength = payloadLength,
                    ExpectedPayloadHash = expectedPayloadHash,
                    Metadata = metadata
                };
            });
        }

        private void ReadPayload(Candidate candidate, bool deserializeDocument)
        {
            ExecuteWithIoRetry(() =>
            {
                using var stream = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.SequentialScan);

                stream.Position = candidate.PayloadOffset;
                byte[] storedPayload = ReadExactly(stream, checked((int)candidate.PayloadLength));

                if (!BytesEqual(ComputeHash(storedPayload), candidate.ExpectedPayloadHash))
                {
                    throw new InvalidDataException("Save payload checksum failed.");
                }

                if (!deserializeDocument)
                {
                    return;
                }

                byte[] documentBytes = (candidate.Flags & CompressedFlag) != 0
                    ? _compressor.Decompress(storedPayload, _options.MaximumPayloadBytes)
                    : storedPayload;

                candidate.Document = _serializer.Deserialize<SaveDocument>(Utf8.GetString(documentBytes));
                ValidateDocument(candidate.Document);
            });
        }

        private void RotateBackups(string slotId, Candidate previous)
        {
            for (int index = _options.BackupCount; index >= 2; index--)
            {
                string source = GetBackupPath(slotId, index - 1);
                string destination = GetBackupPath(slotId, index);

                if (File.Exists(source))
                {
                    CopyWithFlush(source, destination);
                }
            }

            if (previous == null)
            {
                return;
            }

            string firstBackup = GetBackupPath(slotId, 1);

            if (!string.Equals(previous.Path, firstBackup, StringComparison.Ordinal))
            {
                CopyWithFlush(previous.Path, firstBackup);
            }
        }

        private void PromotePending(string slotId)
        {
            string primary = GetPrimaryPath(slotId);
            string pending = GetPendingPath(slotId);
            string swap = GetSwapPath(slotId);

            if (!File.Exists(primary))
            {
                ExecuteWithIoRetry(() => File.Move(pending, primary));
                return;
            }

            bool replaced = false;

            try
            {
                if (File.Exists(swap))
                {
                    ExecuteWithIoRetry(() => File.Delete(swap));
                }

                ExecuteWithIoRetry(() =>
                    File.Replace(pending, primary, swap, ignoreMetadataErrors: true));
                replaced = true;
            }
            catch (PlatformNotSupportedException)
            {
                // Use the recoverable two-rename fallback below.
            }
            catch (IOException)
            {
                // Some file systems do not implement File.Replace. Pending remains recoverable.
            }

            if (replaced)
            {
                ReadPayload(ReadHeader(primary, SaveFileSource.Primary, 0), deserializeDocument: false);

                if (File.Exists(swap))
                {
                    ExecuteWithIoRetry(() => File.Delete(swap));
                }

                return;
            }

            if (File.Exists(swap))
            {
                ExecuteWithIoRetry(() => File.Delete(swap));
            }

            ExecuteWithIoRetry(() => File.Move(primary, swap));

            try
            {
                ExecuteWithIoRetry(() => File.Move(pending, primary));
                ReadPayload(ReadHeader(primary, SaveFileSource.Primary, 0), deserializeDocument: false);
                ExecuteWithIoRetry(() => File.Delete(swap));
            }
            catch
            {
                if (!File.Exists(primary) && File.Exists(swap))
                {
                    ExecuteWithIoRetry(() => File.Move(swap, primary));
                }

                throw;
            }
        }

        private void CopyWithFlush(string source, string destination)
        {
            ExecuteWithIoRetry(() =>
            {
                File.Copy(source, destination, overwrite: true);

                using var stream = new FileStream(
                    destination,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

                stream.Flush(flushToDisk: true);
            });
        }

        private long FindHighestReadableSequence(string slotId)
        {
            long sequence = 0;

            foreach (CandidatePath path in GetCandidatePaths(slotId))
            {
                if (!File.Exists(path.Path))
                {
                    continue;
                }

                try
                {
                    Candidate candidate = ReadHeader(path.Path, path.Source, path.BackupIndex);

                    if (string.Equals(candidate.Metadata.SlotId, slotId, StringComparison.Ordinal))
                    {
                        sequence = Math.Max(sequence, candidate.Metadata.Sequence);
                    }
                }
                catch
                {
                    // Sequence recovery only needs one readable header.
                }
            }

            return sequence;
        }

        private string[] FindKnownSlotIds()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            foreach (string file in Directory.EnumerateFiles(_options.DirectoryPath, "slot_*"))
            {
                string name = Path.GetFileName(file);
                string slotId = TryExtractSlotId(name);

                if (SaveKeyValidator.IsValidSlotId(slotId))
                {
                    result.Add(slotId);
                }
            }

            return result.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private string TryExtractSlotId(string fileName)
        {
            const string prefix = "slot_";

            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            string[] suffixes = new string[3 + _options.BackupCount];
            suffixes[0] = ".sav";
            suffixes[1] = ".pending";
            suffixes[2] = ".swap";

            for (int i = 1; i <= _options.BackupCount; i++)
            {
                suffixes[2 + i] = $".bak{i}";
            }

            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];

                if (fileName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
                }
            }

            return null;
        }

        private bool HasAnySlotFile(string slotId)
        {
            return GetCandidatePaths(slotId).Any(path => File.Exists(path.Path));
        }

        private IEnumerable<CandidatePath> GetCandidatePaths(string slotId)
        {
            yield return new CandidatePath(GetPrimaryPath(slotId), SaveFileSource.Primary, 0);
            yield return new CandidatePath(GetPendingPath(slotId), SaveFileSource.Pending, 0);
            yield return new CandidatePath(GetSwapPath(slotId), SaveFileSource.Swap, 0);

            for (int i = 1; i <= _options.BackupCount; i++)
            {
                yield return new CandidatePath(GetBackupPath(slotId, i), SaveFileSource.Backup, i);
            }
        }

        private string GetPrimaryPath(string slotId) => Path.Combine(_options.DirectoryPath, $"slot_{slotId}.sav");
        private string GetPendingPath(string slotId) => Path.Combine(_options.DirectoryPath, $"slot_{slotId}.pending");
        private string GetSwapPath(string slotId) => Path.Combine(_options.DirectoryPath, $"slot_{slotId}.swap");
        private string GetBackupPath(string slotId, int index) => Path.Combine(_options.DirectoryPath, $"slot_{slotId}.bak{index}");

        private static SaveSlotInfo CreateInfo(Candidate candidate)
        {
            return new SaveSlotInfo(
                candidate.Metadata,
                candidate.Source,
                candidate.BackupIndex);
        }

        private static int SourcePriority(SaveFileSource source)
        {
            return source switch
            {
                SaveFileSource.Primary => 0,
                SaveFileSource.Pending => 1,
                SaveFileSource.Swap => 2,
                SaveFileSource.Backup => 3,
                _ => 4
            };
        }

        private static void ValidateMetadata(SaveSlotMetadata metadata)
        {
            const long MaximumUnixMilliseconds = 253402300799999L;

            if (!SaveKeyValidator.IsValidSlotId(metadata.SlotId) || metadata.Sequence < 1)
            {
                throw new InvalidDataException("Save metadata contains invalid identity data.");
            }

            if (metadata.CreatedAtUnixMs < 0 ||
                metadata.CreatedAtUnixMs > MaximumUnixMilliseconds ||
                metadata.LastSavedAtUnixMs < 0 ||
                metadata.LastSavedAtUnixMs > MaximumUnixMilliseconds)
            {
                throw new InvalidDataException("Save metadata contains an invalid timestamp.");
            }

            if (metadata.PlayTimeSeconds < 0 ||
                double.IsNaN(metadata.PlayTimeSeconds) ||
                double.IsInfinity(metadata.PlayTimeSeconds))
            {
                throw new InvalidDataException("Save metadata contains invalid play time.");
            }

            if (metadata.DisplayName != null && metadata.DisplayName.Length > 256)
            {
                throw new InvalidDataException("Save display name is unexpectedly long.");
            }

            metadata.DisplayName = string.IsNullOrWhiteSpace(metadata.DisplayName)
                ? metadata.SlotId
                : metadata.DisplayName;
            metadata.SceneName ??= string.Empty;
            metadata.GameVersion ??= string.Empty;
            metadata.Context ??= new Dictionary<string, Newtonsoft.Json.Linq.JToken>(StringComparer.Ordinal);

            foreach (string key in metadata.Context.Keys)
            {
                SaveKeyValidator.RequireProviderKey(key);
            }
        }

        private static void ValidateDocument(SaveDocument document)
        {
            if (document == null || document.Sections == null)
            {
                throw new InvalidDataException("Save document is missing its section map.");
            }

            foreach (KeyValuePair<string, SaveSectionRecord> pair in document.Sections)
            {
                SaveKeyValidator.RequireProviderKey(pair.Key);

                if (pair.Value == null || pair.Value.Version < 1 || pair.Value.Data == null)
                {
                    throw new InvalidDataException(
                        $"Save section '{pair.Key}' has an invalid record.");
                }
            }
        }

        private static byte[] ComputeHash(byte[] value)
        {
            using SHA256 sha = SHA256.Create();
            return sha.ComputeHash(value);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;

            for (int i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }

        private static byte[] ReadExactly(BinaryReader reader, int count)
        {
            byte[] value = reader.ReadBytes(count);

            if (value.Length != count)
            {
                throw new EndOfStreamException("Save file ended unexpectedly.");
            }

            return value;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var value = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                int read = stream.Read(value, offset, count - offset);

                if (read == 0)
                {
                    throw new EndOfStreamException("Save file ended unexpectedly.");
                }

                offset += read;
            }

            return value;
        }

        private static T ExecuteWithIoRetry<T>(Func<T> operation)
        {
            IOException lastException = null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    return operation();
                }
                catch (IOException exception)
                {
                    lastException = exception;

                    if (attempt < 3)
                    {
                        Thread.Sleep(50 * (attempt + 1));
                    }
                }
            }

            throw lastException ?? new IOException("Save I/O failed.");
        }

        private static void ExecuteWithIoRetry(Action operation)
        {
            ExecuteWithIoRetry(() =>
            {
                operation();
                return true;
            });
        }

        private static bool IsSerializationException(Exception exception)
        {
            return exception is Newtonsoft.Json.JsonException ||
                   exception is InvalidDataException ||
                   exception is DecoderFallbackException ||
                   exception is OverflowException;
        }

        private static bool IsIoException(Exception exception)
        {
            return exception is IOException ||
                   exception is UnauthorizedAccessException ||
                   exception is DirectoryNotFoundException;
        }

        private sealed class Candidate
        {
            public string Path;
            public SaveFileSource Source;
            public int BackupIndex;
            public byte Flags;
            public long PayloadOffset;
            public long PayloadLength;
            public byte[] ExpectedPayloadHash;
            public SaveSlotMetadata Metadata;
            public SaveDocument Document;
        }

        private readonly struct CandidatePath
        {
            public string Path { get; }
            public SaveFileSource Source { get; }
            public int BackupIndex { get; }

            public CandidatePath(string path, SaveFileSource source, int backupIndex)
            {
                Path = path;
                Source = source;
                BackupIndex = backupIndex;
            }
        }
    }
}

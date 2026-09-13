using System;
using System.Collections.Generic;

namespace EXW.SaveSystem
{
    public class SaveOperationResult
    {
        public bool Success => Error == SaveError.None;
        public SaveError Error { get; }
        public string Message { get; }
        public SaveSlotInfo Slot { get; }

        protected SaveOperationResult(SaveError error, string message, SaveSlotInfo slot)
        {
            Error = error;
            Message = message ?? string.Empty;
            Slot = slot;
        }

        public static SaveOperationResult Succeeded(SaveSlotInfo slot, string message = "")
        {
            return new SaveOperationResult(SaveError.None, message, slot);
        }

        public static SaveOperationResult Failed(SaveError error, string message)
        {
            if (error == SaveError.None)
            {
                throw new ArgumentException("A failed result requires an error code.", nameof(error));
            }

            return new SaveOperationResult(error, message, null);
        }
    }

    public sealed class SaveReadResult : SaveOperationResult
    {
        public SaveDocument Document { get; }

        private SaveReadResult(
            SaveError error,
            string message,
            SaveSlotInfo slot,
            SaveDocument document)
            : base(error, message, slot)
        {
            Document = document;
        }

        public static SaveReadResult Succeeded(SaveSlotInfo slot, SaveDocument document)
        {
            return new SaveReadResult(SaveError.None, string.Empty, slot, document);
        }

        public new static SaveReadResult Failed(SaveError error, string message)
        {
            return new SaveReadResult(error, message, null, null);
        }
    }

    public sealed class SaveCatalogResult
    {
        public bool Success => Error == SaveError.None;
        public SaveError Error { get; }
        public string Message { get; }
        public IReadOnlyList<SaveSlotInfo> Slots { get; }

        private SaveCatalogResult(
            SaveError error,
            string message,
            IReadOnlyList<SaveSlotInfo> slots)
        {
            Error = error;
            Message = message ?? string.Empty;
            Slots = slots ?? Array.Empty<SaveSlotInfo>();
        }

        public static SaveCatalogResult Succeeded(IReadOnlyList<SaveSlotInfo> slots)
        {
            return new SaveCatalogResult(SaveError.None, string.Empty, slots);
        }

        public static SaveCatalogResult Failed(SaveError error, string message)
        {
            return new SaveCatalogResult(error, message, Array.Empty<SaveSlotInfo>());
        }
    }

    public sealed class SaveDebugDocument
    {
        public SaveSlotMetadata Metadata { get; set; }
        public SaveDocument Document { get; set; }
    }

    public sealed class SaveDebugReadResult
    {
        public bool Success => Error == SaveError.None;
        public SaveError Error { get; }
        public string Message { get; }
        public string Json { get; }

        private SaveDebugReadResult(SaveError error, string message, string json)
        {
            Error = error;
            Message = message ?? string.Empty;
            Json = json ?? string.Empty;
        }

        public static SaveDebugReadResult Succeeded(string json)
        {
            return new SaveDebugReadResult(SaveError.None, string.Empty, json);
        }

        public static SaveDebugReadResult Failed(SaveError error, string message)
        {
            return new SaveDebugReadResult(error, message, string.Empty);
        }
    }
}

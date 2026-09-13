using System;

namespace EXW.SaveSystem
{
    public sealed class SaveSlotInfo
    {
        public SaveSlotMetadata Metadata { get; }
        public SaveFileSource Source { get; }
        public int BackupIndex { get; }
        public bool IsValid { get; }
        public string Diagnostic { get; }
        public bool Recovered => IsValid && Source != SaveFileSource.Primary;

        public SaveSlotInfo(
            SaveSlotMetadata metadata,
            SaveFileSource source,
            int backupIndex = 0,
            bool isValid = true,
            string diagnostic = "")
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Source = source;
            BackupIndex = backupIndex;
            IsValid = isValid;
            Diagnostic = diagnostic ?? string.Empty;
        }

        internal static SaveSlotInfo Damaged(string slotId, string diagnostic)
        {
            return new SaveSlotInfo(
                new SaveSlotMetadata
                {
                    SlotId = slotId,
                    DisplayName = "Damaged Save"
                },
                SaveFileSource.Primary,
                isValid: false,
                diagnostic: diagnostic);
        }
    }
}

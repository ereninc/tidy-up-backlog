namespace EXW.SaveSystem
{
    public enum SaveError
    {
        None,
        InvalidSlotId,
        NoActiveSlot,
        Busy,
        NotFound,
        CorruptData,
        IncompatibleVersion,
        DuplicateProvider,
        CaptureFailed,
        RestoreFailed,
        SerializationFailed,
        IoFailed
    }
}

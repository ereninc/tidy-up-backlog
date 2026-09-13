namespace EXW.SaveSystem
{
    public interface ISaveContextProvider
    {
        string Key { get; }
        object CaptureContext();
    }
}

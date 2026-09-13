namespace EXW.SaveSystem
{
    public interface ISaveCompressor
    {
        byte[] Compress(byte[] input);
        byte[] Decompress(byte[] input, int maximumOutputBytes);
    }
}

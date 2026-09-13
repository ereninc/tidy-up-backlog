using System;
using System.IO;
using System.IO.Compression;

namespace EXW.SaveSystem
{
    public sealed class GZipSaveCompressor : ISaveCompressor
    {
        private const int BufferSize = 81920;

        public byte[] Compress(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            using var output = new MemoryStream();

            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(input, 0, input.Length);
            }

            return output.ToArray();
        }

        public byte[] Decompress(byte[] input, int maximumOutputBytes)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (maximumOutputBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
            }

            using var source = new MemoryStream(input, writable: false);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Min(input.Length * 2, maximumOutputBytes));

            var buffer = new byte[BufferSize];

            while (true)
            {
                int read = gzip.Read(buffer, 0, buffer.Length);

                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > maximumOutputBytes)
                {
                    throw new InvalidDataException(
                        $"Decompressed save exceeds the {maximumOutputBytes} byte safety limit.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
    }
}

using System.IO.Compression;

namespace Bifrost.Updates.Chunking
{
    /// <summary>
    /// On-host format of a chunk: one header byte (0 = raw, 1 = Brotli) followed by the payload. Many client files (the Calligraphy pack,
    /// DXT texture data) barely compress, so a chunk is stored raw unless Brotli saves at least 3%.
    /// </summary>
    public static class ChunkCodec
    {
        private const byte Raw = 0;
        private const byte Brotli = 1;

        public static byte[] Encode(ReadOnlySpan<byte> data)
        {
            byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
            if (BrotliEncoder.TryCompress(data, compressed, out int written, 4, 22) && written + 1 < data.Length * 0.97)
            {
                byte[] result = new byte[written + 1];
                result[0] = Brotli;
                Buffer.BlockCopy(compressed, 0, result, 1, written);
                return result;
            }

            byte[] raw = new byte[data.Length + 1];
            raw[0] = Raw;
            data.CopyTo(raw.AsSpan(1));
            return raw;
        }

        /// <summary>Returns the chunk bytes, or null if the data is malformed or does not decode to exactly the expected size.</summary>
        public static byte[] Decode(ReadOnlySpan<byte> stored, int expectedSize)
        {
            if (stored.Length < 1 || expectedSize < 0 || expectedSize > UpdateConstants.ChunkMax)
                return null;

            ReadOnlySpan<byte> payload = stored[1..];
            switch (stored[0])
            {
                case Raw:
                    return payload.Length == expectedSize ? payload.ToArray() : null;

                case Brotli:
                    byte[] result = new byte[expectedSize];
                    if (BrotliDecoder.TryDecompress(payload, result, out int written) == false || written != expectedSize)
                        return null;
                    return result;

                default:
                    return null;
            }
        }
    }
}

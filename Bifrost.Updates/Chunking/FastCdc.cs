using System.Security.Cryptography;

namespace Bifrost.Updates.Chunking
{
    /// <summary>
    /// Content-defined chunking (FastCDC style, normalized chunking) with a gear hash. Boundaries depend only on the content around them, so an insertion
    /// or an in-place edit only changes the chunks near it. The publisher and the launcher MUST use identical parameters and this exact algorithm:
    /// it is part of the published format. Changing anything here invalidates every published manifest.
    /// </summary>
    public static class FastCdc
    {
        private static readonly ulong[] Gear = BuildGearTable();

        private static ulong[] BuildGearTable()
        {
            // Fixed xorshift sequence: identical on every platform and runtime.
            ulong[] table = new ulong[256];
            ulong x = 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < table.Length; i++)
            {
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
                table[i] = x * 0x2545F4914F6CDD1DUL;
            }
            return table;
        }

        /// <summary>Called for every chunk: offset in the file, the chunk bytes (valid only during the call) and their SHA-256 (lowercase hex).</summary>
        public delegate void ChunkCallback(long offset, ReadOnlySpan<byte> data, string sha256);

        public sealed class Result
        {
            public long Size { get; init; }
            public string Sha256 { get; init; }
            public int ChunkCount { get; init; }
        }

        public static Result Chunk(Stream input, ChunkCallback onChunk, CancellationToken cancellationToken = default, Action<long> progress = null,
            int min = UpdateConstants.ChunkMin, int average = UpdateConstants.ChunkAverage, int max = UpdateConstants.ChunkMax)
        {
            if (min <= 0 || average < min || max < average)
                throw new ArgumentException("Invalid chunk size parameters.");

            int bits = BitsFor(average);
            // High-bit masks: the high bits of the gear hash depend on the last ~64 bytes, which gives better boundaries than the low bits.
            ulong maskSmall = ((1UL << (bits + 2)) - 1) << (64 - (bits + 2));   // stricter before the average size
            ulong maskLarge = ((1UL << (bits - 2)) - 1) << (64 - (bits - 2));   // looser after it

            byte[] buffer = new byte[max * 2];
            int start = 0, end = 0;
            bool eof = false;
            long offset = 0;
            int chunkCount = 0;

            using IncrementalHash fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using IncrementalHash chunkHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] digest = new byte[32];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Keep at least max bytes available unless the file has ended.
                if (!eof && end - start < max)
                {
                    if (start > 0)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                        end -= start;
                        start = 0;
                    }

                    while (!eof && end < max)
                    {
                        int read = input.Read(buffer, end, buffer.Length - end);
                        if (read == 0) eof = true; else end += read;
                    }
                }

                int available = end - start;
                if (available == 0)
                    break;

                int cut = available <= min ? available : FindCut(buffer, start, available, min, average, max, maskSmall, maskLarge);
                ReadOnlySpan<byte> chunk = new(buffer, start, cut);

                chunkHash.AppendData(chunk);
                chunkHash.GetHashAndReset(digest);
                fileHash.AppendData(chunk);

                onChunk?.Invoke(offset, chunk, Convert.ToHexString(digest).ToLowerInvariant());

                start += cut;
                offset += cut;
                chunkCount++;
                progress?.Invoke(offset);
            }

            fileHash.GetHashAndReset(digest);
            return new Result { Size = offset, Sha256 = Convert.ToHexString(digest).ToLowerInvariant(), ChunkCount = chunkCount };
        }

        private static int BitsFor(int average)
        {
            int bits = 0;
            while ((1 << (bits + 1)) <= average) bits++;
            if (bits < 4) throw new ArgumentException("Average chunk size too small.");
            return bits;
        }

        private static int FindCut(byte[] buffer, int start, int available, int min, int average, int max, ulong maskSmall, ulong maskLarge)
        {
            int limit = Math.Min(available, max);
            int normal = Math.Min(available, average);
            ulong hash = 0;
            int i = min;

            for (; i < normal; i++)
            {
                hash = (hash << 1) + Gear[buffer[start + i]];
                if ((hash & maskSmall) == 0) return i + 1;
            }

            for (; i < limit; i++)
            {
                hash = (hash << 1) + Gear[buffer[start + i]];
                if ((hash & maskLarge) == 0) return i + 1;
            }

            return limit;
        }
    }
}

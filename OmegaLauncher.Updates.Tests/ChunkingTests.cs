using System.Security.Cryptography;
using OmegaLauncher.Updates;
using OmegaLauncher.Updates.Chunking;

namespace OmegaLauncher.Updates.Tests
{
    public class ChunkingTests
    {
        internal static byte[] RandomBytes(int length, int seed)
        {
            byte[] data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static (List<(long Offset, int Length, string Hash)> Chunks, FastCdc.Result Result) Run(byte[] data)
        {
            List<(long, int, string)> chunks = new();
            FastCdc.Result result = FastCdc.Chunk(new MemoryStream(data), (offset, span, sha) => chunks.Add((offset, span.Length, sha)));
            return (chunks, result);
        }

        [Fact]
        public void ChunksCoverTheFileAndRespectTheSizeLimits()
        {
            byte[] data = RandomBytes(6 * 1024 * 1024 + 123, 1);
            var (chunks, result) = Run(data);

            Assert.Equal(data.Length, result.Size);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), result.Sha256);
            Assert.Equal(chunks.Count, result.ChunkCount);

            long expectedOffset = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                Assert.Equal(expectedOffset, chunks[i].Offset);
                Assert.True(chunks[i].Length <= UpdateConstants.ChunkMax);
                if (i < chunks.Count - 1)
                    Assert.True(chunks[i].Length >= UpdateConstants.ChunkMin);

                Assert.Equal(Convert.ToHexString(SHA256.HashData(data.AsSpan((int)chunks[i].Offset, chunks[i].Length))).ToLowerInvariant(), chunks[i].Hash);
                expectedOffset += chunks[i].Length;
            }

            Assert.Equal(data.Length, expectedOffset);
        }

        [Fact]
        public void AverageChunkSizeIsNearTheConfiguredAverage()
        {
            var (chunks, _) = Run(RandomBytes(24 * 1024 * 1024, 2));
            double average = chunks.Average(c => (double)c.Length);
            Assert.InRange(average, UpdateConstants.ChunkAverage * 0.5, UpdateConstants.ChunkAverage * 2.0);
        }

        [Fact]
        public void ChunkingIsDeterministic()
        {
            byte[] data = RandomBytes(3 * 1024 * 1024, 3);
            Assert.Equal(Run(data).Chunks.Select(c => c.Hash), Run(data).Chunks.Select(c => c.Hash));
        }

        [Fact]
        public void AnInsertionOnlyChangesTheChunksAroundIt()
        {
            byte[] data = RandomBytes(8 * 1024 * 1024, 4);
            byte[] inserted = new byte[data.Length + 1000];
            Buffer.BlockCopy(data, 0, inserted, 0, 4_000_000);
            new Random(99).NextBytes(inserted.AsSpan(4_000_000, 1000));
            Buffer.BlockCopy(data, 4_000_000, inserted, 4_001_000, data.Length - 4_000_000);

            HashSet<string> before = Run(data).Chunks.Select(c => c.Hash).ToHashSet();
            var after = Run(inserted).Chunks;
            int shared = after.Count(c => before.Contains(c.Hash));

            // Boundaries re-synchronise right after the insertion, so almost everything is shared.
            Assert.True(shared >= after.Count - 6, $"only {shared} of {after.Count} chunks were shared");
        }

        [Fact]
        public void AnInPlaceEditChangesFewChunks()
        {
            byte[] data = RandomBytes(8 * 1024 * 1024, 5);
            byte[] edited = (byte[])data.Clone();
            for (int i = 0; i < 100; i++)
                edited[3_000_000 + i] ^= 0xFF;

            HashSet<string> before = Run(data).Chunks.Select(c => c.Hash).ToHashSet();
            var after = Run(edited).Chunks;
            Assert.True(after.Count(c => before.Contains(c.Hash) == false) <= 3);
        }

        [Fact]
        public void EmptyAndTinyFiles()
        {
            var (emptyChunks, empty) = Run(Array.Empty<byte>());
            Assert.Empty(emptyChunks);
            Assert.Equal(0, empty.Size);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant(), empty.Sha256);

            var (tinyChunks, tiny) = Run(new byte[] { 1, 2, 3 });
            Assert.Single(tinyChunks);
            Assert.Equal(3, tiny.Size);
        }

        [Fact]
        public void CodecRoundTripsRawAndCompressedChunks()
        {
            byte[] random = RandomBytes(100_000, 6);
            byte[] storedRandom = ChunkCodec.Encode(random);
            Assert.Equal(0, storedRandom[0]);
            Assert.Equal(random, ChunkCodec.Decode(storedRandom, random.Length));

            byte[] zeros = new byte[200_000];
            byte[] storedZeros = ChunkCodec.Encode(zeros);
            Assert.Equal(1, storedZeros[0]);
            Assert.True(storedZeros.Length < 1000);
            Assert.Equal(zeros, ChunkCodec.Decode(storedZeros, zeros.Length));
        }

        [Fact]
        public void CodecRejectsMalformedChunks()
        {
            byte[] stored = ChunkCodec.Encode(RandomBytes(5000, 7));
            Assert.Null(ChunkCodec.Decode(stored, 4999));
            Assert.Null(ChunkCodec.Decode(new byte[] { 9, 1, 2, 3 }, 3));
            Assert.Null(ChunkCodec.Decode(ReadOnlySpan<byte>.Empty, 0));
            Assert.Null(ChunkCodec.Decode(stored, UpdateConstants.ChunkMax + 1));
        }
    }
}

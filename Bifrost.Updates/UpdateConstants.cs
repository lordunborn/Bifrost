namespace Bifrost.Updates
{
    public static class UpdateConstants
    {
        public const int ManifestSchema = 1;

        // Chunk sizes are part of the published format: the launcher must chunk local files exactly like the publisher did.
        // 256 KiB average was picked from measurements on the real client files (see the plan): a Calligraphy.sip editing session
        // changes ~6 MB at this size vs ~8-12 MB at 1 MiB, and the stock -> merged texture caches need ~80 MB.
        public const int ChunkMin = 64 * 1024;
        public const int ChunkAverage = 256 * 1024;
        public const int ChunkMax = 1024 * 1024;

        public const string ManifestFileName = "manifest.json";
        public const string SignatureFileName = "manifest.sig";
        public const string BaselineSizesFileName = "baseline-sizes.json";
        public const string ChunkDirectory = "chunks";
    }
}

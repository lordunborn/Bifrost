using System.Text.Json.Serialization;

namespace Bifrost.Updates.Models
{
    /// <summary>
    /// A reference to one content-defined chunk of a file. Chunks are stored on the host as chunks/xx/&lt;sha256&gt;.
    /// </summary>
    public class ChunkRef
    {
        [JsonPropertyName("h")] public string Sha256 { get; set; }
        [JsonPropertyName("s")] public int Size { get; set; }
    }

    public class ManifestFile
    {
        /// <summary>Path relative to the client root, forward slashes, canonical case (taken from the stock client).</summary>
        public string Path { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public List<ChunkRef> Chunks { get; set; } = new();
    }

    /// <summary>
    /// The published description of the launcher-managed client files. Signed (manifest.sig, ECDSA P-256 over the exact manifest.json bytes).
    /// </summary>
    public class ClientManifest
    {
        public int Schema { get; set; } = UpdateConstants.ManifestSchema;
        public int ContentVersion { get; set; }
        public int MinLauncherVersion { get; set; }
        public string BaseClientVersion { get; set; }
        public bool Force { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
        public string PublishedUtc { get; set; }
        public int ChunkAverage { get; set; } = UpdateConstants.ChunkAverage;
        public List<ManifestFile> Files { get; set; } = new();

        /// <summary>Files that were managed in an earlier content version and no longer are: the launcher puts the stock file back.</summary>
        public List<string> Removed { get; set; } = new();
    }

    public class BaselineFile
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }

    /// <summary>Hashes of every file of the pristine (stock) client. Only the publisher needs the hashes.</summary>
    public class ClientBaseline
    {
        public string BaseClientVersion { get; set; }
        public List<BaselineFile> Files { get; set; } = new();
    }

    /// <summary>Path + size of every stock file, published so the launcher can sanity-check a player's base install without hashing 24 GB.</summary>
    public class BaselineSizes
    {
        public string BaseClientVersion { get; set; }
        public List<BaselineSizeEntry> Files { get; set; } = new();
    }

    public class BaselineSizeEntry
    {
        [JsonPropertyName("p")] public string Path { get; set; }
        [JsonPropertyName("s")] public long Size { get; set; }
    }

    /// <summary>Publisher-side cache of file hashes so repeated publishes do not re-hash 36 GB.</summary>
    public class HashCache
    {
        public Dictionary<string, HashCacheEntry> Entries { get; set; } = new();
    }

    public class HashCacheEntry
    {
        public long Size { get; set; }
        public long MtimeTicks { get; set; }
        public string Sha256 { get; set; }
    }
}

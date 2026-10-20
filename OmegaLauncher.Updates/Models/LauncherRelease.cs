namespace OmegaLauncher.Updates.Models
{
    /// <summary>One file of a launcher release: the program for one platform.</summary>
    public sealed class LauncherReleaseFile
    {
        /// <summary>"win-x64" or "linux-x64".</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Path relative to the content root, for example "launcher/OmegaLauncher-win-x64-1.0.1.exe".</summary>
        public string Path { get; set; } = string.Empty;

        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    /// <summary>The published description of the newest launcher (launcher/latest.json, signed like the client manifest).</summary>
    public sealed class LauncherRelease
    {
        public int Schema { get; set; } = 1;
        public string Version { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime PublishedUtc { get; set; }
        public List<LauncherReleaseFile> Files { get; set; } = new();
    }
}

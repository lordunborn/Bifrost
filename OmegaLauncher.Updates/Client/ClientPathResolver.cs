using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Client
{
    /// <summary>
    /// Maps a manifest path to a file on disk. Manifest paths carry the canonical case of the stock client. On Windows the file system ignores case; on Linux
    /// (Steam Deck) a client can contain folders whose case differs, so each segment is matched ignoring case against what already exists and only the
    /// missing tail is created with the manifest's case. That way an update never creates a second "cookedpcconsole" next to "CookedPCConsole".
    /// </summary>
    public static class ClientPathResolver
    {
        public static string Resolve(string clientRoot, string relativePath)
        {
            string full = ClientPathGuard.ResolveUnderRoot(clientRoot, relativePath);
            if (full == null)
                throw new InvalidDataException($"Unsafe path in manifest: {relativePath}");

            if (OperatingSystem.IsWindows())
                return full;

            string current = Path.GetFullPath(clientRoot);
            string[] segments = relativePath.Split('/');

            for (int i = 0; i < segments.Length; i++)
            {
                string exact = Path.Combine(current, segments[i]);
                if (File.Exists(exact) || Directory.Exists(exact))
                {
                    current = exact;
                    continue;
                }

                string match = null;
                if (Directory.Exists(current))
                {
                    foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                    {
                        if (string.Equals(Path.GetFileName(entry), segments[i], StringComparison.OrdinalIgnoreCase))
                        {
                            match = entry;
                            break;
                        }
                    }
                }

                current = match ?? Path.Combine(current, segments[i]);
            }

            return current;
        }
    }
}

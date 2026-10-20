namespace OmegaLauncher.Updates.Security
{
    /// <summary>
    /// Every path that comes from a manifest is untrusted. A manifest path must be a plain relative path with forward slashes that stays inside the client
    /// folder, or the launcher refuses the whole manifest.
    /// </summary>
    public static class ClientPathGuard
    {
        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 400)
                return false;

            if (path[0] == '/' || path.Contains('\\') || path.Contains(':'))
                return false;

            foreach (char c in path)
            {
                if (c < 32 || c is '<' or '>' or '"' or '|' or '?' or '*')
                    return false;
            }

            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;

                // Windows would silently strip these and make two different names point at one file.
                if (segment[^1] == '.' || segment[^1] == ' ')
                    return false;
            }

            return true;
        }

        /// <summary>Full path of a manifest path under the client root, or null if the path is unsafe or would escape the root.</summary>
        public static string ResolveUnderRoot(string clientRoot, string relativePath)
        {
            if (IsSafeRelativePath(relativePath) == false)
                return null;

            string root = Path.GetFullPath(clientRoot);
            if (root.EndsWith(Path.DirectorySeparatorChar) == false)
                root += Path.DirectorySeparatorChar;

            string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
        }
    }
}

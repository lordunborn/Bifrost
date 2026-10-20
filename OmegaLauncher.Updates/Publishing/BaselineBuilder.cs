using System.Security.Cryptography;
using OmegaLauncher.Updates.Models;

namespace OmegaLauncher.Updates.Publishing
{
    public static class BaselineBuilder
    {
        /// <summary>All files under a client root as forward-slash relative paths.</summary>
        public static IEnumerable<string> EnumerateRelativePaths(string clientRoot)
        {
            string root = Path.GetFullPath(clientRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.System, IgnoreInaccessible = true };

            foreach (string full in Directory.EnumerateFiles(root, "*", options))
                yield return Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
        }

        public static string HashFile(string path)
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }

        /// <summary>Hash of a file through the cache (size + modification time must match).</summary>
        public static string HashFileCached(string clientRoot, string relativePath, HashCache cache)
        {
            string full = Path.Combine(clientRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            FileInfo info = new(full);
            long ticks = info.LastWriteTimeUtc.Ticks;

            if (cache != null)
            {
                lock (cache)
                {
                    if (cache.Entries.TryGetValue(relativePath, out HashCacheEntry entry) && entry.Size == info.Length && entry.MtimeTicks == ticks)
                        return entry.Sha256;
                }
            }

            string hash = HashFile(full);

            if (cache != null)
            {
                lock (cache)
                    cache.Entries[relativePath] = new HashCacheEntry { Size = info.Length, MtimeTicks = ticks, Sha256 = hash };
            }

            return hash;
        }

        /// <summary>Hashes every file of a pristine client. Run once against a Steam-verified copy of the stock client.</summary>
        public static ClientBaseline Build(string clientRoot, string baseClientVersion, Action<string> log = null, CancellationToken cancellationToken = default)
        {
            List<string> paths = EnumerateRelativePaths(clientRoot).ToList();
            log?.Invoke($"Hashing {paths.Count} files...");

            BaselineFile[] files = new BaselineFile[paths.Count];
            int done = 0;

            Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = cancellationToken }, i =>
            {
                string full = Path.Combine(clientRoot, paths[i].Replace('/', Path.DirectorySeparatorChar));
                files[i] = new BaselineFile { Path = paths[i], Size = new FileInfo(full).Length, Sha256 = HashFile(full) };

                int n = Interlocked.Increment(ref done);
                if (n % 500 == 0)
                    log?.Invoke($"  {n} / {paths.Count}");
            });

            return new ClientBaseline
            {
                BaseClientVersion = baseClientVersion,
                Files = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList()
            };
        }
    }
}

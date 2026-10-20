using OmegaLauncher.Updates.Chunking;
using OmegaLauncher.Updates.Models;

namespace OmegaLauncher.Updates.Client
{
    public sealed class PlannedFile
    {
        public ManifestFile Manifest { get; init; }
        public string FullPath { get; init; }
        public bool Exists { get; init; }
        public bool UpToDate { get; init; }

        /// <summary>Chunks of the local file by hash (offset and length of the first occurrence); empty when there is no usable local file.</summary>
        public Dictionary<string, (long Offset, int Length)> LocalChunks { get; init; } = new();

        /// <summary>Distinct chunks that must be downloaded, in file order.</summary>
        public List<ChunkRef> MissingChunks { get; init; } = new();

        public long DownloadBytes => MissingChunks.Sum(c => (long)c.Size);
    }

    public sealed class UpdatePlan
    {
        public int TargetVersion { get; init; }
        public List<PlannedFile> Files { get; init; } = new();
        public List<string> Removed { get; init; } = new();

        public IEnumerable<PlannedFile> FilesToUpdate => Files.Where(f => f.UpToDate == false);
        public int FileCount => FilesToUpdate.Count();

        /// <summary>Bytes of chunk data to download (before compression, so an upper bound of what travels over the network).</summary>
        public long DownloadBytes => FilesToUpdate.Sum(f => f.DownloadBytes);

        /// <summary>Total size of the files that will be rebuilt (needed as free disk space while they are assembled).</summary>
        public long RebuildBytes => FilesToUpdate.Sum(f => f.Manifest.Size);

        public bool UpToDate => FileCount == 0 && Removed.Count == 0;
    }

    public sealed class PlanProgress
    {
        public string CurrentFile { get; init; }
        public int FilesDone { get; init; }
        public int FilesTotal { get; init; }
    }

    /// <summary>
    /// Works out what has to be done to bring a client folder to a manifest, without changing anything. A file is up to date when its hash equals the
    /// manifest's; otherwise the local file is chunked with the same algorithm the publisher used, and only the chunks it lacks are listed for download.
    /// Hashes from the previous run are trusted while a file's size and modification time are unchanged (pass <c>ignoreCache</c> to check everything).
    /// </summary>
    public static class UpdatePlanner
    {
        public static UpdatePlan Plan(ClientManifest manifest, string clientRoot, LauncherState state, bool ignoreCache = false,
            IProgress<PlanProgress> progress = null, CancellationToken cancellationToken = default)
        {
            UpdatePlan plan = new() { TargetVersion = manifest.ContentVersion };
            int done = 0;

            foreach (ManifestFile file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new PlanProgress { CurrentFile = file.Path, FilesDone = done, FilesTotal = manifest.Files.Count });

                plan.Files.Add(PlanFile(file, clientRoot, state, ignoreCache, cancellationToken));
                done++;
            }

            // A file that used to be managed and no longer is: only relevant if this launcher installed it.
            foreach (string removed in manifest.Removed)
            {
                if (state.Files.ContainsKey(removed))
                    plan.Removed.Add(removed);
            }

            progress?.Report(new PlanProgress { CurrentFile = null, FilesDone = done, FilesTotal = manifest.Files.Count });
            return plan;
        }

        private static PlannedFile PlanFile(ManifestFile file, string clientRoot, LauncherState state, bool ignoreCache, CancellationToken cancellationToken)
        {
            string fullPath = ClientPathResolver.Resolve(clientRoot, file.Path);
            FileInfo info = new(fullPath);

            if (info.Exists == false)
            {
                return new PlannedFile { Manifest = file, FullPath = fullPath, Exists = false, UpToDate = false, MissingChunks = DistinctChunks(file, null) };
            }

            // Trust a previous hash while size and modification time are unchanged.
            if (ignoreCache == false && state.Files.TryGetValue(file.Path, out StateFile known)
                && known.Size == info.Length && known.MtimeTicks == info.LastWriteTimeUtc.Ticks && known.Sha256 == file.Sha256 && info.Length == file.Size)
            {
                return new PlannedFile { Manifest = file, FullPath = fullPath, Exists = true, UpToDate = true };
            }

            // Chunk the local file the way the publisher did; whatever matches does not have to be downloaded.
            Dictionary<string, (long Offset, int Length)> local = new();
            FastCdc.Result result;

            using (FileStream fs = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan))
            {
                result = FastCdc.Chunk(fs, (offset, data, sha) => local.TryAdd(sha, (offset, data.Length)), cancellationToken);
            }

            if (result.Sha256 == file.Sha256 && result.Size == file.Size)
                return new PlannedFile { Manifest = file, FullPath = fullPath, Exists = true, UpToDate = true, LocalChunks = local };

            return new PlannedFile { Manifest = file, FullPath = fullPath, Exists = true, UpToDate = false, LocalChunks = local, MissingChunks = DistinctChunks(file, local) };
        }

        private static List<ChunkRef> DistinctChunks(ManifestFile file, Dictionary<string, (long Offset, int Length)> local)
        {
            HashSet<string> seen = new();
            List<ChunkRef> missing = new();

            foreach (ChunkRef chunk in file.Chunks)
            {
                if (local != null && local.ContainsKey(chunk.Sha256))
                    continue;

                if (seen.Add(chunk.Sha256))
                    missing.Add(chunk);
            }

            return missing;
        }

        /// <summary>
        /// Records the hashes of files found up to date, so the next check can skip hashing them. Call after planning.
        /// </summary>
        public static void RememberUpToDate(UpdatePlan plan, LauncherState state)
        {
            foreach (PlannedFile file in plan.Files.Where(f => f.UpToDate && f.Exists))
            {
                FileInfo info = new(file.FullPath);
                if (state.Files.TryGetValue(file.Manifest.Path, out StateFile entry) == false)
                {
                    entry = new StateFile { Adopted = true };
                    state.Files[file.Manifest.Path] = entry;
                }

                entry.Size = info.Length;
                entry.MtimeTicks = info.LastWriteTimeUtc.Ticks;
                entry.Sha256 = file.Manifest.Sha256;
            }
        }
    }
}

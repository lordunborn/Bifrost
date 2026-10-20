using System.Security.Cryptography;
using OmegaLauncher.Updates.Chunking;
using OmegaLauncher.Updates.Content;
using OmegaLauncher.Updates.Models;

namespace OmegaLauncher.Updates.Client
{
    public class UpdateException : Exception
    {
        public UpdateException(string message, Exception inner = null) : base(message, inner) { }
    }

    public sealed class InsufficientSpaceException : UpdateException
    {
        public long RequiredBytes { get; }
        public long AvailableBytes { get; }

        public InsufficientSpaceException(long required, long available)
            : base($"Not enough free disk space: {required / 1048576} MB needed, {available / 1048576} MB available.")
        {
            RequiredBytes = required;
            AvailableBytes = available;
        }
    }

    public sealed class UpdateProgress
    {
        public string CurrentFile { get; init; }
        public int FilesDone { get; init; }
        public int FilesTotal { get; init; }
        public long BytesDownloaded { get; init; }
        public long BytesToDownload { get; init; }
    }

    public sealed class ApplyResult
    {
        public int FilesUpdated { get; set; }
        public int FilesRemoved { get; set; }
        public long BytesDownloaded { get; set; }
    }

    /// <summary>
    /// Brings a client folder to a manifest: downloads the missing chunks (verifying each one), rebuilds every changed file next to the original, checks the
    /// whole-file hash and only then swaps it in. The stock original of each file is moved into .mho/backup the first time it is replaced, so Revert can put it back.
    /// A failure or cancel never leaves a half-written game file: the target is only touched by the final rename.
    /// </summary>
    public static class UpdateApplier
    {
        private const int DownloadParallelism = 6;

        public static async Task<ApplyResult> ApplyAsync(UpdatePlan plan, IContentSource source, string clientRoot, LauncherState state,
            IProgress<UpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            MhoFolders folders = new(clientRoot);
            ApplyResult result = new();
            List<PlannedFile> work = plan.FilesToUpdate.ToList();

            CheckFreeSpace(folders, plan);
            Directory.CreateDirectory(folders.Mho);
            Directory.CreateDirectory(folders.TempDirectory);
            Directory.CreateDirectory(folders.ChunkCacheDirectory);

            long downloaded = 0;
            long toDownload = plan.DownloadBytes;
            int filesDone = 0;

            void Report(string current) => progress?.Report(new UpdateProgress
            {
                CurrentFile = current,
                FilesDone = filesDone,
                FilesTotal = work.Count,
                BytesDownloaded = Interlocked.Read(ref downloaded),
                BytesToDownload = toDownload
            });

            Report(null);

            foreach (PlannedFile file in work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(file.Manifest.Path);

                await DownloadChunksAsync(file, source, folders, bytes =>
                {
                    Interlocked.Add(ref downloaded, bytes);
                    Report(file.Manifest.Path);
                }, cancellationToken);

                string partPath = Assemble(file, folders, cancellationToken);
                SwapIn(file, partPath, folders, state);
                LauncherStateStore.Save(folders, state);

                // The chunks of this file are in the new file now.
                foreach (ChunkRef chunk in file.MissingChunks)
                    TryDelete(folders.CachedChunkPath(chunk.Sha256));

                filesDone++;
                result.FilesUpdated++;
                Report(file.Manifest.Path);
            }

            foreach (string removed in plan.Removed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (state.Files.TryGetValue(removed, out StateFile entry) == false)
                    continue;

                RestoreOrDelete(removed, entry, folders, clientRoot);
                state.Files.Remove(removed);
                result.FilesRemoved++;
            }

            state.ContentVersion = plan.TargetVersion;
            LauncherStateStore.Save(folders, state);

            TryDeleteDirectory(folders.TempDirectory);
            TryDeleteDirectory(folders.ChunkCacheDirectory);

            result.BytesDownloaded = downloaded;
            return result;
        }

        private static void CheckFreeSpace(MhoFolders folders, UpdatePlan plan)
        {
            // While a file is rebuilt its original is still in place, and the downloaded chunks sit in the cache: so both the rebuilt size and the download count.
            long required = (long)((plan.RebuildBytes + plan.DownloadBytes) * 1.02) + (64L << 20);

            try
            {
                string probe = Directory.Exists(folders.Root) ? folders.Root : Path.GetPathRoot(folders.Root);
                long available = new DriveInfo(Path.GetPathRoot(probe)).AvailableFreeSpace;
                if (plan.FileCount > 0 && available < required)
                    throw new InsufficientSpaceException(required, available);
            }
            catch (ArgumentException)
            {
                // Network or unusual mounts that DriveInfo cannot describe: skip the pre-check, writes will fail loudly if the disk is full.
            }
        }

        private static async Task DownloadChunksAsync(PlannedFile file, IContentSource source, MhoFolders folders, Action<long> onBytes, CancellationToken cancellationToken)
        {
            using SemaphoreSlim gate = new(DownloadParallelism);
            List<Task> tasks = new();

            foreach (ChunkRef chunk in file.MissingChunks)
            {
                string cached = folders.CachedChunkPath(chunk.Sha256);
                if (File.Exists(cached) && new FileInfo(cached).Length == chunk.Size)
                {
                    onBytes(chunk.Size);   // already downloaded by an interrupted earlier run
                    continue;
                }

                await gate.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        byte[] data = await FetchVerifiedChunkAsync(source, chunk, cancellationToken);
                        string temp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await File.WriteAllBytesAsync(temp, data, cancellationToken);
                        File.Move(temp, cached, true);
                        onBytes(chunk.Size);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private static async Task<byte[]> FetchVerifiedChunkAsync(IContentSource source, ChunkRef chunk, CancellationToken cancellationToken)
        {
            const int attempts = 3;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                byte[] stored = await source.GetBytesAsync(ChunkStorePath.Of(chunk.Sha256), cancellationToken);
                if (stored == null)
                    throw new UpdateException($"The update server does not have chunk {chunk.Sha256[..12]}. The update is incomplete on the server; try again later.");

                byte[] data = ChunkCodec.Decode(stored, chunk.Size);
                if (data != null && Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() == chunk.Sha256)
                    return data;

                // Corrupted in transit (or on the host): fetch it again before giving up.
            }

            throw new UpdateException($"Chunk {chunk.Sha256[..12]} failed its integrity check {attempts} times. The download was corrupted; nothing was changed.");
        }

        /// <summary>Builds the new file as a .part file from reusable local chunks and the downloaded cache, and checks its hash.</summary>
        private static string Assemble(PlannedFile file, MhoFolders folders, CancellationToken cancellationToken)
        {
            string partPath = folders.PartPathFor(file.Manifest.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(partPath));

            FileStream old = file.Exists && file.LocalChunks.Count > 0
                ? new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                : null;

            try
            {
                using FileStream part = new(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[UpdateConstants.ChunkMax];

                foreach (ChunkRef chunk in file.Manifest.Chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int length = chunk.Size;

                    if (TryReadLocalChunk(old, file, chunk, buffer))
                    {
                        // reused from the file that is being replaced
                    }
                    else
                    {
                        string cached = folders.CachedChunkPath(chunk.Sha256);
                        if (File.Exists(cached) == false)
                            throw new UpdateException($"Chunk {chunk.Sha256[..12]} is missing from the download cache.");

                        using FileStream cs = File.OpenRead(cached);
                        if (cs.Read(buffer, 0, length) != length)
                            throw new UpdateException($"Cached chunk {chunk.Sha256[..12]} is damaged.");
                    }

                    part.Write(buffer, 0, length);
                    hash.AppendData(buffer, 0, length);
                }

                part.Flush(true);

                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (actual != file.Manifest.Sha256 || part.Length != file.Manifest.Size)
                    throw new UpdateException($"The rebuilt file {file.Manifest.Path} does not match the published hash. Nothing was changed.");
            }
            catch
            {
                TryDelete(partPath);
                throw;
            }
            finally
            {
                old?.Dispose();
            }

            return partPath;
        }

        private static bool TryReadLocalChunk(FileStream old, PlannedFile file, ChunkRef chunk, byte[] buffer)
        {
            if (old == null || file.LocalChunks.TryGetValue(chunk.Sha256, out (long Offset, int Length) location) == false || location.Length != chunk.Size)
                return false;

            try
            {
                old.Seek(location.Offset, SeekOrigin.Begin);
                int read = 0;
                while (read < chunk.Size)
                {
                    int n = old.Read(buffer, read, chunk.Size - read);
                    if (n == 0) return false;
                    read += n;
                }

                // The local file could have changed since it was planned: re-check the bytes before trusting them.
                return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, chunk.Size))).ToLowerInvariant() == chunk.Sha256;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void SwapIn(PlannedFile file, string partPath, MhoFolders folders, LauncherState state)
        {
            string path = file.Manifest.Path;
            string target = file.FullPath;
            Directory.CreateDirectory(Path.GetDirectoryName(target));

            bool tracked = state.Files.TryGetValue(path, out StateFile entry);
            bool hadOriginal = tracked && entry.HadOriginal;

            if (File.Exists(target))
            {
                if (tracked == false || entry.Adopted)
                {
                    // First time we replace this file: keep the stock original for Revert (a rename on the same volume costs no space).
                    string backup = folders.BackupPathFor(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Move(target, backup, true);
                    hadOriginal = true;
                }
                else
                {
                    File.Delete(target);   // an earlier version of ours; the stock original is already backed up
                }
            }

            File.Move(partPath, target, true);

            FileInfo info = new(target);
            state.Files[path] = new StateFile { Size = info.Length, MtimeTicks = info.LastWriteTimeUtc.Ticks, Sha256 = file.Manifest.Sha256, HadOriginal = hadOriginal };
        }

        /// <summary>Puts the stock original back, or deletes the file if the launcher added it. Adopted files are never touched.</summary>
        internal static void RestoreOrDelete(string relativePath, StateFile entry, MhoFolders folders, string clientRoot)
        {
            if (entry.Adopted)
                return;

            string target = ClientPathResolver.Resolve(clientRoot, relativePath);
            string backup = folders.BackupPathFor(relativePath);

            if (entry.HadOriginal && File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(backup, target, true);
            }
            else if (entry.HadOriginal == false && File.Exists(target))
            {
                File.Delete(target);
            }
        }

        internal static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        internal static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

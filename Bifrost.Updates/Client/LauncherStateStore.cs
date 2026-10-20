using System.Text.Json;
using Bifrost.Updates.Models;
using Bifrost.Updates.Publishing;

namespace Bifrost.Updates.Client
{
    /// <summary>Folder layout of the launcher's own files inside a client folder. Everything lives under ".mho" so it is easy to see, exclude and delete.</summary>
    public sealed class MhoFolders
    {
        public MhoFolders(string clientRoot)
        {
            Root = Path.GetFullPath(clientRoot);
            Mho = Path.Combine(Root, ".mho");
        }

        public string Root { get; }
        public string Mho { get; }
        public string StatePath => Path.Combine(Mho, "state.json");
        public string BackupDirectory => Path.Combine(Mho, "backup");
        public string TempDirectory => Path.Combine(Mho, "tmp");
        public string ChunkCacheDirectory => Path.Combine(Mho, "cache");

        public string BackupPathFor(string relativePath) => Path.Combine(BackupDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        public string PartPathFor(string relativePath) => Path.Combine(TempDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar) + ".part");
        public string CachedChunkPath(string sha256) => Path.Combine(ChunkCacheDirectory, sha256);
    }

    public static class LauncherStateStore
    {
        public static LauncherState Load(MhoFolders folders)
        {
            try
            {
                if (File.Exists(folders.StatePath))
                    return JsonSerializer.Deserialize(File.ReadAllBytes(folders.StatePath), UpdatesJsonContext.Default.LauncherState) ?? new LauncherState();
            }
            catch (JsonException)
            {
                // A damaged state file only costs a re-check: every file is hashed again.
            }

            return new LauncherState();
        }

        public static void Save(MhoFolders folders, LauncherState state)
        {
            ManifestIO.WriteAtomic(folders.StatePath, JsonSerializer.SerializeToUtf8Bytes(state, UpdatesJsonContext.Default.LauncherState));
        }
    }
}

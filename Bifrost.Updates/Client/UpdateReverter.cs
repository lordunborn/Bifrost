using Bifrost.Updates.Models;

namespace Bifrost.Updates.Client
{
    /// <summary>Puts a client folder back to stock: restores every original the launcher backed up and deletes every file it added.</summary>
    public static class UpdateReverter
    {
        public static int RevertAll(string clientRoot, LauncherState state, IProgress<PlanProgress> progress = null, CancellationToken cancellationToken = default)
        {
            MhoFolders folders = new(clientRoot);
            List<string> paths = state.Files.Keys.ToList();
            int reverted = 0;

            for (int i = 0; i < paths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new PlanProgress { CurrentFile = paths[i], FilesDone = i, FilesTotal = paths.Count });

                StateFile entry = state.Files[paths[i]];
                if (entry.Adopted == false)
                    reverted++;

                UpdateApplier.RestoreOrDelete(paths[i], entry, folders, clientRoot);
                state.Files.Remove(paths[i]);
                LauncherStateStore.Save(folders, state);
            }

            state.ContentVersion = 0;
            LauncherStateStore.Save(folders, state);
            return reverted;
        }
    }
}

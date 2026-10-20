using System.Text;
using Bifrost.Updates.Content;
using Bifrost.Updates.Models;
using Bifrost.Updates.Publishing;
using Bifrost.Updates.Security;

namespace Bifrost.Updates.Client
{
    public sealed class ManifestRejectedException : UpdateException
    {
        public ManifestRejectedException(string message) : base(message) { }
    }

    public sealed class UpdateCheckResult
    {
        public ClientManifest Manifest { get; init; }
        public UpdatePlan Plan { get; init; }
        public LauncherState State { get; init; }

        /// <summary>Result of the stock-client sanity check, or null if the host does not publish baseline sizes.</summary>
        public BaseCheckResult BaseCheck { get; init; }

        /// <summary>The published content needs a newer launcher than this one.</summary>
        public bool LauncherTooOld { get; init; }

        public int InstalledContentVersion => State.ContentVersion;
        public bool UpToDate => Plan.UpToDate;

        /// <summary>The player has to update before playing.</summary>
        public bool Required => Manifest.Force && UpToDate == false;
    }

    /// <summary>The launcher's entry points: check what is published against a client folder, apply it, or revert it.</summary>
    public static class UpdateService
    {
        public static async Task<UpdateCheckResult> CheckAsync(IContentSource source, string publicKeyBase64, string clientRoot, int launcherVersion = int.MaxValue,
            bool ignoreCache = false, IProgress<PlanProgress> progress = null, CancellationToken cancellationToken = default)
        {
            byte[] manifestBytes;
            byte[] signatureBytes;

            try
            {
                manifestBytes = await source.GetBytesAsync(UpdateConstants.ManifestFileName, cancellationToken);
                signatureBytes = await source.GetBytesAsync(UpdateConstants.SignatureFileName, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new UpdateException("The update server could not be reached. Check your connection and try again.", ex);
            }

            if (manifestBytes == null || signatureBytes == null)
                throw new UpdateException("The update server has no published client files yet.");

            if (ManifestSigner.Verify(manifestBytes, Encoding.UTF8.GetString(signatureBytes), publicKeyBase64) == false)
                throw new ManifestRejectedException("The update information is not signed by the server's key. It was rejected, nothing was changed.");

            ClientManifest manifest = ManifestIO.Deserialize(manifestBytes);
            List<string> problems = ManifestIO.Validate(manifest);
            if (problems.Count > 0)
                throw new ManifestRejectedException("The update information is invalid: " + problems[0]);

            MhoFolders folders = new(clientRoot);
            LauncherState state = LauncherStateStore.Load(folders);

            UpdatePlan plan = UpdatePlanner.Plan(manifest, clientRoot, state, ignoreCache, progress, cancellationToken);

            // Remember what is already up to date, so the next check does not hash it again.
            UpdatePlanner.RememberUpToDate(plan, state);
            if (state.Files.Count > 0)
                LauncherStateStore.Save(folders, state);

            BaseCheckResult baseCheck = null;
            try
            {
                baseCheck = await BaseCheck.RunAsync(source, clientRoot, manifest, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // The base check is advice only.
            }

            return new UpdateCheckResult
            {
                Manifest = manifest,
                Plan = plan,
                State = state,
                BaseCheck = baseCheck,
                LauncherTooOld = manifest.MinLauncherVersion > launcherVersion
            };
        }

        public static Task<ApplyResult> ApplyAsync(UpdateCheckResult check, IContentSource source, string clientRoot, IProgress<UpdateProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (check.LauncherTooOld)
                throw new UpdateException("This update needs a newer launcher. Update the launcher first.");

            return UpdateApplier.ApplyAsync(check.Plan, source, clientRoot, check.State, progress, cancellationToken);
        }

        public static int Revert(string clientRoot, IProgress<PlanProgress> progress = null, CancellationToken cancellationToken = default)
        {
            MhoFolders folders = new(clientRoot);
            LauncherState state = LauncherStateStore.Load(folders);
            return UpdateReverter.RevertAll(clientRoot, state, progress, cancellationToken);
        }
    }
}

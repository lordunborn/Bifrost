using OmegaLauncher.Core.Models;
using OmegaLauncher.Core.Serialization;

namespace OmegaLauncher.Core.Updates
{
    /// <summary>
    /// Server-specific settings that ship next to the launcher (Mho.Deployment.json): where the client files are published, the key that signs them,
    /// and the servers to offer. The repository only holds an empty template; the real file is handed to players with the launcher.
    /// </summary>
    public class DeploymentConfig
    {
        public const string FileName = "Mho.Deployment.json";

        /// <summary>Version of the update format this launcher understands (compared with the manifest's MinLauncherVersion).</summary>
        public const int LauncherVersion = 1;

        public string UpdateBaseUrl { get; set; } = string.Empty;
        public string UpdatePublicKey { get; set; } = string.Empty;

        /// <summary>Shared secret that signs the content token passed to the game (-webtoken). Must match the server's ContentTokenSecret; empty = no token.</summary>
        public string ContentTokenSecret { get; set; } = string.Empty;
        public List<ServerInfo> Servers { get; set; } = new();

        public bool UpdatesConfigured => string.IsNullOrWhiteSpace(UpdateBaseUrl) == false && string.IsNullOrWhiteSpace(UpdatePublicKey) == false;

        public static DeploymentConfig Load()
        {
            // Next to the launcher first, then the working directory (the launcher may be started from anywhere, e.g. a Steam shortcut).
            foreach (string path in new[] { Path.Combine(AppContext.BaseDirectory, FileName), FileName })
            {
                if (File.Exists(path) == false)
                    continue;

                try
                {
                    return JsonHelper.LoadOrCreate(path, JsonContext.Default.DeploymentConfig) ?? new();
                }
                catch (System.Text.Json.JsonException)
                {
                    // A broken file behaves like a missing one: the launcher still works as a plain launcher.
                }
            }

            return new();
        }
    }
}

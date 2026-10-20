using Bifrost.Updates.Client;

namespace Bifrost.Core.ClientManagement
{
    public static class ClientHelper
    {
        private const string ClientPathOverrideFile = "ClientPathOverride.txt";

        public static string GetClientPath()
        {
            if (File.Exists(ClientPathOverrideFile))
                return File.ReadAllText(ClientPathOverrideFile);

            return Directory.GetCurrentDirectory();
        }

        /// <summary>Other places to look for the game when the current directory is not it: the saved folder, then Steam libraries.</summary>
        public static IEnumerable<string> GetFallbackPaths(string savedClientPath)
        {
            if (string.IsNullOrWhiteSpace(savedClientPath) == false)
                yield return savedClientPath;

            foreach (string candidate in ClientInspector.SteamCandidates())
                yield return candidate;
        }

        /// <summary>The game root for a folder the player picked, or null if it is not a Marvel Heroes folder.</summary>
        public static string ResolveRoot(string path) => ClientInspector.FindClientRoot(path);
    }
}

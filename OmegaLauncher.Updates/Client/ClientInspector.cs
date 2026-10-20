namespace OmegaLauncher.Updates.Client
{
    public sealed class ClientInfo
    {
        public string Root { get; set; }
        public string ExecutablePath { get; set; }
        public string Version { get; set; }
        public List<string> Problems { get; } = new();

        public bool Found => Root != null && ExecutablePath != null;
    }

    /// <summary>Finds and describes a Marvel Heroes client folder.</summary>
    public static class ClientInspector
    {
        private static readonly string[] ExecutableSubPaths =
        {
            "UnrealEngine3/Binaries/Win64/MarvelHeroesOmega.exe",
            "UnrealEngine3/Binaries/Win32/MarvelHeroesOmega.exe",
        };

        private static readonly string[] CommonFolderNames = { "Marvel Heroes", "Marvel Heroes Omega", "Marvel Heroes 2016", "MarvelHeroes" };

        /// <summary>The game root (the folder holding UnrealEngine3 and Data/Game) for any file or folder the user picked, or null.</summary>
        public static string FindClientRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string current = Path.GetFullPath(path);
            if (File.Exists(current))
                current = Path.GetDirectoryName(current);

            for (int i = 0; i < 6 && current != null; i++)
            {
                if (LooksLikeRoot(current))
                    return current;

                foreach (string name in CommonFolderNames)
                {
                    string child = Path.Combine(current, name);
                    if (LooksLikeRoot(child))
                        return child;
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }

        private static bool LooksLikeRoot(string folder) =>
            Directory.Exists(Path.Combine(folder, "UnrealEngine3")) && Directory.Exists(Path.Combine(folder, "Data", "Game"));

        public static ClientInfo Inspect(string path, string expectedVersion = null)
        {
            ClientInfo info = new();
            info.Root = FindClientRoot(path);

            if (info.Root == null)
            {
                info.Problems.Add("This is not a Marvel Heroes folder (UnrealEngine3 and Data/Game were not found).");
                return info;
            }

            foreach (string sub in ExecutableSubPaths)
            {
                string exe = ClientPathResolver.Resolve(info.Root, sub);
                if (File.Exists(exe))
                {
                    info.ExecutablePath = exe;
                    break;
                }
            }

            if (info.ExecutablePath == null)
            {
                info.Problems.Add("MarvelHeroesOmega.exe was not found under UnrealEngine3/Binaries.");
                return info;
            }

            info.Version = PeVersion.Read(info.ExecutablePath);

            if (info.Version == null)
                info.Problems.Add("The version of MarvelHeroesOmega.exe could not be read.");
            else if (expectedVersion != null && info.Version != expectedVersion)
                info.Problems.Add($"This client is version {info.Version}, but the server needs {expectedVersion}. Get the stock {expectedVersion} client (Steam depot), then run the launcher again.");

            return info;
        }

        /// <summary>Likely install locations of the Steam copy of the game on this machine (Windows and Linux/Steam Deck).</summary>
        public static IEnumerable<string> SteamCandidates()
        {
            List<string> steamRoots = new();

            if (OperatingSystem.IsWindows())
            {
                foreach (string env in new[] { "ProgramFiles(x86)", "ProgramFiles" })
                {
                    string pf = Environment.GetEnvironmentVariable(env);
                    if (pf != null)
                        steamRoots.Add(Path.Combine(pf, "Steam"));
                }
            }
            else
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                steamRoots.Add(Path.Combine(home, ".steam", "steam"));
                steamRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
                steamRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string steam in steamRoots)
            {
                if (Directory.Exists(steam) == false)
                    continue;

                List<string> libraries = new() { steam };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }

                foreach (string library in libraries)
                {
                    string common = Path.Combine(library, "steamapps", "common");
                    foreach (string name in CommonFolderNames)
                    {
                        string candidate = Path.Combine(common, name);
                        if (LooksLikeRoot(candidate) && seen.Add(Path.GetFullPath(candidate)))
                            yield return candidate;
                    }
                }
            }
        }
    }
}

namespace Bifrost.Core.Platform
{
    public enum RunnerKind
    {
        Proton,     // Steam's Proton or Proton-GE: "proton run game.exe"
        Umu,        // umu-launcher: "umu-run game.exe", downloads Proton by itself
        Wine        // plain Wine
    }

    public sealed class GameRunner
    {
        public RunnerKind Kind { get; init; }
        public string Path { get; init; }
        public string Name { get; init; }

        public override string ToString() => $"{Name} ({Path})";
    }

    /// <summary>
    /// Finds a way to run the Windows game on Linux and builds the command for it: Proton-GE and Proton from Steam, umu-run, or Wine, in that order.
    /// Nothing here touches the game files; the Wine prefix lives in the launcher's own data folder.
    /// </summary>
    public static class LinuxRunner
    {
        public const string RunnerEnvironmentVariable = "MHO_RUNNER";

        /// <summary>Steam installations on this machine: native, the older dot folder, and the Flatpak one.</summary>
        public static List<string> SteamRoots(string home)
        {
            List<string> candidates = new()
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            };

            List<string> roots = new();
            HashSet<string> seen = new();

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate) == false)
                    continue;

                // ~/.steam/steam is a symlink to the real folder; list each real folder once.
                string real = Directory.ResolveLinkTarget(candidate, true)?.FullName ?? candidate;
                if (seen.Add(real))
                    roots.Add(candidate);
            }

            return roots;
        }

        /// <summary>All runners found, best first. <paramref name="pathDirectories"/> is the PATH split into folders.</summary>
        public static List<GameRunner> Detect(string home, IEnumerable<string> pathDirectories)
        {
            List<GameRunner> found = new();
            List<string> steamRoots = SteamRoots(home);

            // Proton-GE and other community builds installed by hand or by ProtonUp-Qt. Newest name first.
            foreach (string root in steamRoots)
            {
                string tools = Path.Combine(root, "compatibilitytools.d");
                if (Directory.Exists(tools) == false)
                    continue;

                foreach (string dir in Directory.GetDirectories(tools).OrderByDescending(Path.GetFileName, NaturalComparer.Instance))
                {
                    string script = Path.Combine(dir, "proton");
                    if (File.Exists(script))
                        found.Add(new GameRunner { Kind = RunnerKind.Proton, Path = script, Name = Path.GetFileName(dir) });
                }
            }

            // Valve's own Proton builds, installed from the Steam library ("Proton 9.0", "Proton Experimental").
            foreach (string root in steamRoots)
            {
                string common = Path.Combine(root, "steamapps", "common");
                if (Directory.Exists(common) == false)
                    continue;

                foreach (string dir in Directory.GetDirectories(common, "Proton*").OrderByDescending(Path.GetFileName, NaturalComparer.Instance))
                {
                    string script = Path.Combine(dir, "proton");
                    if (File.Exists(script))
                        found.Add(new GameRunner { Kind = RunnerKind.Proton, Path = script, Name = Path.GetFileName(dir) });
                }
            }

            string umu = FindOnPath("umu-run", pathDirectories);
            if (umu != null)
                found.Add(new GameRunner { Kind = RunnerKind.Umu, Path = umu, Name = "umu-run" });

            string wine = FindOnPath("wine", pathDirectories);
            if (wine != null)
                found.Add(new GameRunner { Kind = RunnerKind.Wine, Path = wine, Name = "Wine" });

            return found;
        }

        /// <summary>A runner the player named explicitly (a proton script, umu-run or wine), recognised by its file name.</summary>
        public static GameRunner FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || File.Exists(path) == false)
                return null;

            string name = Path.GetFileName(path).ToLowerInvariant();
            if (name == "proton")
                return new GameRunner { Kind = RunnerKind.Proton, Path = path, Name = Path.GetFileName(Path.GetDirectoryName(path)) };

            if (name.StartsWith("umu-run"))
                return new GameRunner { Kind = RunnerKind.Umu, Path = path, Name = "umu-run" };

            if (name.StartsWith("wine"))
                return new GameRunner { Kind = RunnerKind.Wine, Path = path, Name = "Wine" };

            return null;
        }

        /// <summary>Where the game's Wine prefix lives: the launcher's data folder, never inside the game folder.</summary>
        public static string DefaultPrefix(string home)
        {
            string dataHome = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
                dataHome = Path.Combine(home, ".local", "share");

            return Path.Combine(dataHome, "MHOLauncher", "prefix");
        }

        public static LaunchCommand Build(GameRunner runner, string executablePath, IEnumerable<string> gameArguments, string prefix, string steamRoot)
        {
            LaunchCommand command = new()
            {
                FileName = runner.Path,
                WorkingDirectory = Path.GetDirectoryName(executablePath)
            };

            switch (runner.Kind)
            {
                case RunnerKind.Proton:
                    command.Arguments.Add("run");
                    command.Environment["STEAM_COMPAT_DATA_PATH"] = prefix;
                    if (steamRoot != null)
                        command.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamRoot;
                    break;

                case RunnerKind.Umu:
                    command.Environment["WINEPREFIX"] = Path.Combine(prefix, "pfx");
                    command.Environment["GAMEID"] = "umu-0";
                    break;

                case RunnerKind.Wine:
                    command.Environment["WINEPREFIX"] = Path.Combine(prefix, "pfx");
                    break;
            }

            command.Arguments.Add(executablePath);
            command.Arguments.AddRange(gameArguments);
            return command;
        }

        /// <summary>Compares names with numbers as numbers, so "GE-Proton10-1" is newer than "GE-Proton9-20".</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new();

            public int Compare(string x, string y)
            {
                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                    {
                        int startX = i, startY = j;
                        while (i < x.Length && char.IsDigit(x[i])) i++;
                        while (j < y.Length && char.IsDigit(y[j])) j++;

                        string numX = x[startX..i].TrimStart('0');
                        string numY = y[startY..j].TrimStart('0');
                        int byLength = numX.Length.CompareTo(numY.Length);
                        if (byLength != 0)
                            return byLength;

                        int byValue = string.CompareOrdinal(numX, numY);
                        if (byValue != 0)
                            return byValue;
                    }
                    else
                    {
                        int byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                        if (byChar != 0)
                            return byChar;

                        i++;
                        j++;
                    }
                }

                return (x.Length - i).CompareTo(y.Length - j);
            }
        }

        private static string FindOnPath(string fileName, IEnumerable<string> directories)
        {
            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}

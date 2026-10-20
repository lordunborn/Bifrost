using Bifrost.Core.Platform;

namespace Bifrost.Updates.Tests
{
    public class LinuxRunnerTests : IDisposable
    {
        private readonly string _home = Path.Combine(Path.GetTempPath(), "mho-runner-" + Guid.NewGuid().ToString("N"));

        public LinuxRunnerTests()
        {
            Directory.CreateDirectory(_home);
        }

        public void Dispose()
        {
            Directory.Delete(_home, true);
        }

        private string Touch(params string[] parts)
        {
            string path = Path.Combine(new[] { _home }.Concat(parts).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "");
            return path;
        }

        [Fact]
        public void Detect_OrdersProtonGeThenValveProtonThenUmuThenWine()
        {
            string ge = Touch(".local", "share", "Steam", "compatibilitytools.d", "GE-Proton9-20", "proton");
            string valve = Touch(".local", "share", "Steam", "steamapps", "common", "Proton 9.0", "proton");
            string bin = Path.Combine(_home, "bin");
            Touch("bin", "umu-run");
            Touch("bin", "wine");

            List<GameRunner> runners = LinuxRunner.Detect(_home, new[] { bin });

            Assert.Equal(new[] { RunnerKind.Proton, RunnerKind.Proton, RunnerKind.Umu, RunnerKind.Wine }, runners.Select(r => r.Kind));
            Assert.Equal(ge, runners[0].Path);
            Assert.Equal(valve, runners[1].Path);
        }

        [Fact]
        public void Detect_PutsNewestProtonGeFirst_ByVersionNumber()
        {
            Touch(".local", "share", "Steam", "compatibilitytools.d", "GE-Proton9-5", "proton");
            string newest = Touch(".local", "share", "Steam", "compatibilitytools.d", "GE-Proton10-1", "proton");
            string middle = Touch(".local", "share", "Steam", "compatibilitytools.d", "GE-Proton9-20", "proton");

            List<GameRunner> runners = LinuxRunner.Detect(_home, Array.Empty<string>());

            Assert.Equal(newest, runners[0].Path);
            Assert.Equal(middle, runners[1].Path);
            Assert.Equal(3, runners.Count);
        }

        [Fact]
        public void Detect_NothingInstalled_FindsNothing()
        {
            Assert.Empty(LinuxRunner.Detect(_home, new[] { Path.Combine(_home, "bin") }));
        }

        [Fact]
        public void FromPath_RecognisesRunnersByFileName()
        {
            Assert.Equal(RunnerKind.Proton, LinuxRunner.FromPath(Touch("a", "proton")).Kind);
            Assert.Equal(RunnerKind.Umu, LinuxRunner.FromPath(Touch("b", "umu-run")).Kind);
            Assert.Equal(RunnerKind.Wine, LinuxRunner.FromPath(Touch("c", "wine64")).Kind);
            Assert.Null(LinuxRunner.FromPath(Touch("d", "something-else")));
            Assert.Null(LinuxRunner.FromPath(Path.Combine(_home, "missing", "proton")));
        }

        [Fact]
        public void Build_Proton_SetsCompatEnvironmentAndRunVerb()
        {
            GameRunner runner = new() { Kind = RunnerKind.Proton, Path = "/steam/proton", Name = "Proton" };
            string exe = Path.Combine(_home, "game", "MarvelHeroesOmega.exe");

            LaunchCommand command = LinuxRunner.Build(runner, exe, new[] { "-robocopy", "-siteconfigurl=host/SiteConfig.xml" }, "/data/prefix", "/steam");

            Assert.Equal("/steam/proton", command.FileName);
            Assert.Equal(new[] { "run", exe, "-robocopy", "-siteconfigurl=host/SiteConfig.xml" }, command.Arguments);
            Assert.Equal("/data/prefix", command.Environment["STEAM_COMPAT_DATA_PATH"]);
            Assert.Equal("/steam", command.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"]);
            Assert.Equal(Path.GetDirectoryName(exe), command.WorkingDirectory);
        }

        [Fact]
        public void Build_UmuAndWine_UsePrefixSubfolder()
        {
            string exe = Path.Combine(_home, "game", "MarvelHeroesOmega.exe");

            LaunchCommand umu = LinuxRunner.Build(new GameRunner { Kind = RunnerKind.Umu, Path = "umu-run" }, exe, new[] { "-a" }, "/data/prefix", null);
            Assert.Equal(new[] { exe, "-a" }, umu.Arguments);
            Assert.Equal(Path.Combine("/data/prefix", "pfx"), umu.Environment["WINEPREFIX"]);
            Assert.Equal("umu-0", umu.Environment["GAMEID"]);

            LaunchCommand wine = LinuxRunner.Build(new GameRunner { Kind = RunnerKind.Wine, Path = "wine" }, exe, new[] { "-a" }, "/data/prefix", null);
            Assert.Equal(new[] { exe, "-a" }, wine.Arguments);
            Assert.False(wine.Environment.ContainsKey("STEAM_COMPAT_DATA_PATH"));
        }

        [Fact]
        public void Describe_QuotesArgumentsWithSpaces()
        {
            LaunchCommand command = new() { FileName = "proton", Arguments = { "run", "/games/Marvel Heroes/game.exe" }, Environment = { ["A"] = "b c" } };

            string text = command.Describe();

            Assert.Contains("A=\"b c\"", text);
            Assert.Contains("\"/games/Marvel Heroes/game.exe\"", text);
        }
    }
}

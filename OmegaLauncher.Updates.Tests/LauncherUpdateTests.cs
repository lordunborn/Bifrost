using System.Text;
using System.Text.Json;
using OmegaLauncher.Updates.Client;
using OmegaLauncher.Updates.Content;
using OmegaLauncher.Updates.Models;
using OmegaLauncher.Updates.Publishing;
using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Tests
{
    public class LauncherUpdateTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "mho-launcher-" + Guid.NewGuid().ToString("N"));
        private readonly string _host;
        private readonly string _privatePemPath;
        private readonly string _publicKey;

        public LauncherUpdateTests()
        {
            _host = Path.Combine(_dir, "host");
            Directory.CreateDirectory(_host);

            var (privatePem, publicKey) = ManifestSigner.GenerateKeyPair();
            _privatePemPath = Path.Combine(_dir, "private.pem");
            File.WriteAllText(_privatePemPath, privatePem);
            _publicKey = publicKey;
        }

        public void Dispose() => Directory.Delete(_dir, true);

        private string BuiltFile(string content)
        {
            string path = Path.Combine(_dir, "built-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllText(path, content);
            return path;
        }

        private LauncherRelease Publish(string content, string platform, string version, string notes = "")
            => LauncherPublisher.Publish(BuiltFile(content), platform, version, _privatePemPath, _host, notes);

        private IContentSource Source => new DirectoryContentSource(_host);

        [Fact]
        public async Task Check_NewerVersion_ReturnsTheFileForThisPlatform()
        {
            Publish("windows program", "win-x64", "1.0.1");
            Publish("linux program", "linux-x64", "1.0.1");

            LauncherUpdateInfo info = await LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "linux-x64");

            Assert.NotNull(info);
            Assert.Equal(new Version(1, 0, 1), info.Version);
            Assert.Equal("linux-x64", info.File.Platform);
            Assert.EndsWith("-linux-x64-1.0.1.exe", info.File.Path);
        }

        [Theory]
        [InlineData("1.0.1")]
        [InlineData("1.0.2")]
        [InlineData("2.0.0")]
        public async Task Check_SameOrOlderRelease_ReturnsNull(string installed)
        {
            Publish("program", "win-x64", "1.0.1");

            Assert.Null(await LauncherUpdater.CheckAsync(Source, _publicKey, LauncherUpdater.ParseVersion(installed), "win-x64"));
        }

        [Fact]
        public async Task Check_NoBuildForThisPlatform_ReturnsNull()
        {
            Publish("program", "win-x64", "1.0.1");

            Assert.Null(await LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "linux-x64"));
        }

        [Fact]
        public async Task Check_NothingPublished_ReturnsNull()
        {
            Assert.Null(await LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "win-x64"));
        }

        [Fact]
        public void Publish_OtherPlatformOfTheSameVersion_JoinsTheRelease_NewVersionReplacesIt()
        {
            Publish("a", "win-x64", "1.0.1");
            LauncherRelease both = Publish("b", "linux-x64", "1.0.1");
            Assert.Equal(2, both.Files.Count);

            LauncherRelease next = Publish("c", "win-x64", "1.0.2");
            Assert.Single(next.Files);
            Assert.Equal("1.0.2", next.Version);
        }

        [Fact]
        public async Task Check_ReleaseSignedByAnotherKey_IsRejected()
        {
            Publish("program", "win-x64", "1.0.1");
            var (_, otherPublicKey) = ManifestSigner.GenerateKeyPair();

            await Assert.ThrowsAsync<ManifestRejectedException>(() => LauncherUpdater.CheckAsync(Source, otherPublicKey, new Version(1, 0, 0), "win-x64"));
        }

        [Fact]
        public async Task Check_ReleaseEditedAfterSigning_IsRejected()
        {
            Publish("program", "win-x64", "1.0.1");
            string releasePath = Path.Combine(_host, "launcher", "latest.json");
            File.WriteAllText(releasePath, File.ReadAllText(releasePath).Replace("1.0.1", "9.9.9"));

            await Assert.ThrowsAsync<ManifestRejectedException>(() => LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "win-x64"));
        }

        [Theory]
        [InlineData("../outside.exe")]
        [InlineData("launcher/../../outside.exe")]
        [InlineData("C:/Windows/evil.exe")]
        [InlineData("chunks/ab/abcdef")]
        public async Task Check_SignedReleaseWithUnsafePath_IsIgnored(string path)
        {
            LauncherRelease release = new()
            {
                Version = "1.0.1",
                Files = { new LauncherReleaseFile { Platform = "win-x64", Path = path, Size = 10, Sha256 = new string('a', 64) } }
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(release, UpdatesJsonContext.Default.LauncherRelease);
            Directory.CreateDirectory(Path.Combine(_host, "launcher"));
            File.WriteAllBytes(Path.Combine(_host, "launcher", "latest.json"), bytes);
            File.WriteAllText(Path.Combine(_host, "launcher", "latest.sig"), ManifestSigner.Sign(bytes, File.ReadAllText(_privatePemPath)), new UTF8Encoding(false));

            Assert.Null(await LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "win-x64"));
        }

        [Fact]
        public async Task Download_WritesTheVerifiedFile_AndLeavesNoPartFile()
        {
            Publish("the new launcher program", "win-x64", "1.0.1");
            LauncherUpdateInfo info = await LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "win-x64");
            string target = Path.Combine(_dir, "OmegaLauncher.exe.new");

            await LauncherUpdater.DownloadAsync(Source, info, target);

            Assert.Equal("the new launcher program", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".part"));
        }

        [Fact]
        public async Task Download_FileChangedOnTheHost_IsDiscarded()
        {
            Publish("the new launcher program", "win-x64", "1.0.1");
            LauncherUpdateInfo info = await LauncherUpdater.CheckAsync(Source, _publicKey, new Version(1, 0, 0), "win-x64");
            File.WriteAllText(Path.Combine(_host, info.File.Path.Replace('/', Path.DirectorySeparatorChar)), "the new launcher PROGRAM"); // same length, different bytes
            string target = Path.Combine(_dir, "OmegaLauncher.exe.new");

            UpdateException ex = await Assert.ThrowsAsync<UpdateException>(() => LauncherUpdater.DownloadAsync(Source, info, target));

            Assert.Contains("did not match", ex.Message);
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(target + ".part"));
        }

        [Theory]
        [InlineData("1.0.1", 1, 0, 1)]
        [InlineData("2.10.3" + ".4", 2, 10, 3)]
        [InlineData(" 1.2 ", 1, 2, 0)]
        public void ParseVersion_ReadsMajorMinorBuild(string text, int major, int minor, int build)
        {
            Assert.Equal(new Version(major, minor, build), LauncherUpdater.ParseVersion(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1")]
        public void ParseVersion_Unreadable_ReturnsNull(string text)
        {
            Assert.Null(LauncherUpdater.ParseVersion(text));
        }
    }
}

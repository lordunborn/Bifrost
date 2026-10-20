using System.Text;
using OmegaLauncher.Updates.Publishing;
using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Tests
{
    public class SecurityAndGlobTests
    {
        [Fact]
        public void SignatureRoundTripAndTamperDetection()
        {
            var (privatePem, publicKey) = ManifestSigner.GenerateKeyPair();
            byte[] data = Encoding.UTF8.GetBytes("{\"ContentVersion\":1}");
            string signature = ManifestSigner.Sign(data, privatePem);

            Assert.True(ManifestSigner.Verify(data, signature, publicKey));

            byte[] tampered = (byte[])data.Clone();
            tampered[3] ^= 1;
            Assert.False(ManifestSigner.Verify(tampered, signature, publicKey));

            var (_, otherPublicKey) = ManifestSigner.GenerateKeyPair();
            Assert.False(ManifestSigner.Verify(data, signature, otherPublicKey));

            Assert.False(ManifestSigner.Verify(data, "not base64!!", publicKey));
            Assert.False(ManifestSigner.Verify(data, signature, "garbage"));
        }

        [Theory]
        [InlineData("Data/Game/Calligraphy.sip", true)]
        [InlineData("UnrealEngine3/MarvelGame/CookedPCConsole/AssetPackageCache.bin", true)]
        [InlineData("a.txt", true)]
        [InlineData("", false)]
        [InlineData("/etc/passwd", false)]
        [InlineData("..", false)]
        [InlineData("../x", false)]
        [InlineData("a/../b", false)]
        [InlineData("a/./b", false)]
        [InlineData("a//b", false)]
        [InlineData("a\\b", false)]
        [InlineData("C:/x", false)]
        [InlineData("C:\\x", false)]
        [InlineData("a/b/", false)]
        [InlineData("a/b.", false)]
        [InlineData("a/b ", false)]
        [InlineData("a/b?c", false)]
        public void PathGuardAcceptsOnlyPlainRelativePaths(string path, bool safe)
        {
            Assert.Equal(safe, ClientPathGuard.IsSafeRelativePath(path));
        }

        [Fact]
        public void ResolveUnderRootStaysInsideTheRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "mho-root");
            Assert.NotNull(ClientPathGuard.ResolveUnderRoot(root, "Data/Game/x.sip"));
            Assert.StartsWith(Path.GetFullPath(root), ClientPathGuard.ResolveUnderRoot(root, "Data/Game/x.sip"));
            Assert.Null(ClientPathGuard.ResolveUnderRoot(root, "../outside.txt"));
            Assert.Null(ClientPathGuard.ResolveUnderRoot(root, "a/../../outside.txt"));
        }

        [Theory]
        [InlineData("*.bak*", "Data/Game/Calligraphy.sip.bak12", true)]
        [InlineData("*.bak*", "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc.bak-preworkpatch", true)]
        [InlineData("*.bak*", "Data/Game/Calligraphy.sip", false)]
        [InlineData("Logs/**", "Logs/a/b.log", true)]
        [InlineData("Logs/**", "Data/Logs/a.log", false)]
        [InlineData("BiFrost/**", "BiFrost/Bifrost.exe", true)]
        [InlineData("Bifrost.*", "BiFrost/Bifrost.exe", true)]
        [InlineData("OmegaLauncher.*", "OmegaLauncher.GuiConfig.json", true)]
        [InlineData("Data/Game/*.sip", "Data/Game/Calligraphy.sip", true)]
        [InlineData("Data/Game/*.sip", "Data/Game/Loco/x.sip", false)]
        [InlineData("**/*.dmp", "UnrealEngine3/Binaries/Win64/x.dmp", true)]
        public void GlobMatching(string pattern, string path, bool expected)
        {
            Assert.Equal(expected, new GlobMatcher(new[] { pattern }).IsMatch(path));
        }
    }
}

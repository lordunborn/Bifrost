using System.Text.Json;
using Bifrost.Updates;
using Bifrost.Updates.Content;
using Bifrost.Updates.Models;
using Bifrost.Updates.Publishing;
using Bifrost.Updates.Security;

namespace Bifrost.Updates.Tests
{
    public sealed class PublishRoundTripTests : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "mho-updates-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _pristine;
        private readonly string _edited;
        private readonly string _output;
        private readonly string _baselinePath;
        private readonly string _privateKeyPath;
        private readonly string _publicKey;

        public PublishRoundTripTests()
        {
            _pristine = Path.Combine(_temp, "pristine");
            _edited = Path.Combine(_temp, "edited");
            _output = Path.Combine(_temp, "host");

            Write(_pristine, "Data/Game/Calligraphy.sip", ChunkingTests.RandomBytes(3_000_000, 10));
            Write(_pristine, "Data/Game/mu_cdata.sip", ChunkingTests.RandomBytes(500_000, 11));
            Write(_pristine, "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc", ChunkingTests.RandomBytes(5_000_000, 12));
            Write(_pristine, "UnrealEngine3/Binaries/Win64/MarvelHeroesOmega.exe", ChunkingTests.RandomBytes(200_000, 13));

            Directory.CreateDirectory(_edited);
            CopyDirectory(_pristine, _edited);

            string baselineJson = Path.Combine(_temp, "baseline.json");
            ClientBaseline baseline = BaselineBuilder.Build(_pristine, "1.53.0.203");
            File.WriteAllBytes(baselineJson, JsonSerializer.SerializeToUtf8Bytes(baseline, UpdatesJsonContext.Default.ClientBaseline));
            _baselinePath = baselineJson;

            var (privatePem, publicKey) = ManifestSigner.GenerateKeyPair();
            _privateKeyPath = Path.Combine(_temp, "private.pem");
            File.WriteAllText(_privateKeyPath, privatePem);
            _publicKey = publicKey;
        }

        public void Dispose()
        {
            try { Directory.Delete(_temp, true); } catch (IOException) { }
        }

        private static void Write(string root, string relative, byte[] data)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private PublishResult Publish(bool forceNewVersion = false) => ClientPublisher.Publish(new PublishOptions
        {
            ClientRoot = _edited,
            BaselinePath = _baselinePath,
            OutputDirectory = _output,
            PrivateKeyPemPath = _privateKeyPath,
            Notes = "test",
            ForceNewVersion = forceNewVersion
        });

        private ClientManifest ReadManifest() => ManifestIO.Deserialize(File.ReadAllBytes(Path.Combine(_output, "manifest.json")));

        [Fact]
        public async Task OnlyEditedAndAddedFilesAreManagedAndTheHostVerifies()
        {
            // The edit keeps the file size (like an in-place edit of a sip) so the hash comparison, not the size, has to catch it.
            byte[] sip = File.ReadAllBytes(Path.Combine(_edited, "Data/Game/Calligraphy.sip"));
            for (int i = 0; i < 200; i++)
                sip[1_500_000 + i] ^= 0x5A;
            Write(_edited, "Data/Game/Calligraphy.sip", sip);
            Write(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk", ChunkingTests.RandomBytes(700_000, 14));
            Write(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc.bak-preworkpatch", ChunkingTests.RandomBytes(10_000, 15));
            Write(_edited, "Logs/session.log", ChunkingTests.RandomBytes(1000, 16));

            PublishResult result = Publish();

            Assert.Equal(1, result.ContentVersion);
            Assert.Equal(new[] { "Data/Game/Calligraphy.sip" }, result.Changed);
            Assert.Equal(new[] { "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk" }, result.Added);

            ClientManifest manifest = ReadManifest();
            Assert.Equal(new[] { "Data/Game/Calligraphy.sip", "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk" }, manifest.Files.Select(f => f.Path));
            Assert.Equal("1.53.0.203", manifest.BaseClientVersion);
            Assert.Empty(ManifestIO.Validate(manifest));

            VerifyResult verify = await HostVerifier.VerifyAsync(new DirectoryContentSource(_output), _publicKey, deep: true);
            Assert.True(verify.Ok, string.Join("; ", verify.Errors));
            Assert.Equal(1, verify.ContentVersion);
        }

        [Fact]
        public void PublishingAgainWithoutChangesDoesNothing()
        {
            Write(_edited, "Data/Game/mu_cdata.sip", ChunkingTests.RandomBytes(500_000, 20));
            Assert.Equal(1, Publish().ContentVersion);

            PublishResult second = Publish();
            Assert.True(second.NoChange);
            Assert.Equal(1, second.ContentVersion);
            Assert.Equal(1, ReadManifest().ContentVersion);

            Assert.Equal(2, Publish(forceNewVersion: true).ContentVersion);
        }

        [Fact]
        public void ASmallEditPublishesOnlyTheChangedChunks()
        {
            byte[] tfc = File.ReadAllBytes(Path.Combine(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc"));
            tfc = tfc.Concat(ChunkingTests.RandomBytes(300_000, 21)).ToArray();   // like the costume merge: data appended to the end
            Write(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc", tfc);

            PublishResult first = Publish();
            Assert.Equal(1, first.ContentVersion);

            // Edit the middle of the file, as a second publish.
            for (int i = 0; i < 50; i++)
                tfc[2_000_000 + i] ^= 0xFF;
            Write(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc", tfc);

            PublishResult second = Publish();
            Assert.Equal(2, second.ContentVersion);
            Assert.InRange(second.NewChunks, 1, 4);
            Assert.True(second.ReusedChunks > 10);
        }

        [Fact]
        public void FilesThatStopBeingManagedAreListedAsRemoved()
        {
            Write(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk", ChunkingTests.RandomBytes(100_000, 30));
            Write(_edited, "Data/Game/mu_cdata.sip", ChunkingTests.RandomBytes(500_000, 31));
            Assert.Equal(1, Publish().ContentVersion);

            File.Delete(Path.Combine(_edited, "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk"));
            PublishResult second = Publish();

            Assert.Equal(2, second.ContentVersion);
            ClientManifest manifest = ReadManifest();
            Assert.Equal(new[] { "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk" }, manifest.Removed);
            Assert.Equal(new[] { "Data/Game/mu_cdata.sip" }, manifest.Files.Select(f => f.Path));

            // Cumulative: a player on version 1 skipping straight to 3 still needs to know about the removal.
            Write(_edited, "Data/Game/mu_cdata.sip", ChunkingTests.RandomBytes(500_000, 32));
            Publish();
            Assert.Equal(new[] { "UnrealEngine3/MarvelGame/CookedPCConsole/Costume_New.upk" }, ReadManifest().Removed);
        }

        [Fact]
        public async Task VerificationCatchesTamperingAndCorruption()
        {
            Write(_edited, "Data/Game/mu_cdata.sip", ChunkingTests.RandomBytes(500_000, 40));
            Publish();

            // Wrong key.
            var (_, otherKey) = ManifestSigner.GenerateKeyPair();
            VerifyResult wrongKey = await HostVerifier.VerifyAsync(new DirectoryContentSource(_output), otherKey, deep: false);
            Assert.False(wrongKey.Ok);

            // Tampered manifest.
            string manifestPath = Path.Combine(_output, "manifest.json");
            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            byte[] original = (byte[])manifestBytes.Clone();
            manifestBytes[manifestBytes.Length / 2] ^= 1;
            File.WriteAllBytes(manifestPath, manifestBytes);
            Assert.False((await HostVerifier.VerifyAsync(new DirectoryContentSource(_output), _publicKey, deep: false)).Ok);
            File.WriteAllBytes(manifestPath, original);

            // Corrupt chunk.
            string chunkPath = Directory.EnumerateFiles(Path.Combine(_output, "chunks"), "*", SearchOption.AllDirectories).First();
            byte[] chunk = File.ReadAllBytes(chunkPath);
            chunk[^1] ^= 0xFF;
            File.WriteAllBytes(chunkPath, chunk);
            VerifyResult corrupt = await HostVerifier.VerifyAsync(new DirectoryContentSource(_output), _publicKey, deep: false);
            Assert.False(corrupt.Ok);

            // Missing chunk.
            File.Delete(chunkPath);
            Assert.False((await HostVerifier.VerifyAsync(new DirectoryContentSource(_output), _publicKey, deep: false)).Ok);
        }
    }
}

using System.Text.Json;
using OmegaLauncher.Updates;
using OmegaLauncher.Updates.Client;
using OmegaLauncher.Updates.Content;
using OmegaLauncher.Updates.Models;
using OmegaLauncher.Updates.Publishing;
using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Tests
{
    /// <summary>
    /// The whole player story on temp folders: a pristine client (the "player"), an edited client (what we publish), a host folder, and the launcher's
    /// check / apply / revert in between.
    /// </summary>
    public sealed class UpdateFlowTests : IDisposable
    {
        private const string Sip = "Data/Game/Calligraphy.sip";
        private const string Tfc = "UnrealEngine3/MarvelGame/CookedPCConsole/Textures.tfc";
        private const string NewUpk = "UnrealEngine3/MarvelGame/CookedPCConsole/UC__MarvelPlayer_New_SF.upk";
        private const string Exe = "UnrealEngine3/Binaries/Win64/MarvelHeroesOmega.exe";

        private readonly string _temp = Path.Combine(Path.GetTempPath(), "mho-flow-" + Guid.NewGuid().ToString("N"));
        private readonly string _pristine, _edited, _player, _host, _baselinePath, _privateKeyPath, _publicKey;

        public UpdateFlowTests()
        {
            _pristine = Path.Combine(_temp, "pristine");
            _edited = Path.Combine(_temp, "edited");
            _player = Path.Combine(_temp, "player");
            _host = Path.Combine(_temp, "host");

            Write(_pristine, Sip, ChunkingTests.RandomBytes(2_500_000, 101));
            Write(_pristine, "Data/Game/mu_cdata.sip", ChunkingTests.RandomBytes(300_000, 102));
            Write(_pristine, Tfc, ChunkingTests.RandomBytes(6_000_000, 103));
            Write(_pristine, Exe, ChunkingTests.RandomBytes(200_000, 104));
            Write(_pristine, "Data/Game/Loco/eng.all/strings.bin", ChunkingTests.RandomBytes(50_000, 105));

            CopyDirectory(_pristine, _edited);
            CopyDirectory(_pristine, _player);

            // The edits: a sip edited in place, a texture cache with data appended, and a new costume package.
            byte[] sip = File.ReadAllBytes(Path.Combine(_edited, Sip));
            for (int i = 0; i < 300; i++) sip[1_200_000 + i] ^= 0x33;
            Write(_edited, Sip, sip);
            Write(_edited, Tfc, File.ReadAllBytes(Path.Combine(_edited, Tfc)).Concat(ChunkingTests.RandomBytes(400_000, 106)).ToArray());
            Write(_edited, NewUpk, ChunkingTests.RandomBytes(900_000, 107));

            ClientBaseline baseline = BaselineBuilder.Build(_pristine, "1.53.0.203");
            _baselinePath = Path.Combine(_temp, "baseline.json");
            File.WriteAllBytes(_baselinePath, JsonSerializer.SerializeToUtf8Bytes(baseline, UpdatesJsonContext.Default.ClientBaseline));

            var (pem, publicKey) = ManifestSigner.GenerateKeyPair();
            _privateKeyPath = Path.Combine(_temp, "private.pem");
            File.WriteAllText(_privateKeyPath, pem);
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

        private PublishResult Publish(bool force = true) => ClientPublisher.Publish(new PublishOptions
        {
            ClientRoot = _edited, BaselinePath = _baselinePath, OutputDirectory = _host, PrivateKeyPemPath = _privateKeyPath, Force = force
        });

        private IContentSource Host => new DirectoryContentSource(_host);

        private static string Hash(string root, string relative) => BaselineBuilder.HashFile(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        [Fact]
        public async Task AFreshPlayerIsBroughtToTheEditedClientDownloadingOnlyWhatIsMissing()
        {
            Publish();

            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);
            Assert.False(check.UpToDate);
            Assert.True(check.Required);
            Assert.Equal(3, check.Plan.FileCount);
            Assert.Equal(0, check.InstalledContentVersion);
            Assert.True(check.BaseCheck.Ok, string.Join(",", check.BaseCheck.Missing.Concat(check.BaseCheck.WrongSize)));

            // The player already has the stock chunks of the sip and the texture cache: only the changed parts travel.
            long downloadChunks = check.Plan.DownloadBytes;
            Assert.True(downloadChunks < 3_000_000, $"would download {downloadChunks} bytes");

            ApplyResult result = await UpdateService.ApplyAsync(check, Host, _player);
            Assert.Equal(3, result.FilesUpdated);

            foreach (string path in new[] { Sip, Tfc, NewUpk })
                Assert.Equal(Hash(_edited, path), Hash(_player, path));

            // Unmanaged files are untouched; the stock originals were kept for Revert.
            Assert.Equal(Hash(_pristine, "Data/Game/mu_cdata.sip"), Hash(_player, "Data/Game/mu_cdata.sip"));
            Assert.True(File.Exists(Path.Combine(_player, ".mho", "backup", "Data", "Game", "Calligraphy.sip")));
            Assert.False(File.Exists(Path.Combine(_player, ".mho", "backup", "UnrealEngine3", "MarvelGame", "CookedPCConsole", "UC__MarvelPlayer_New_SF.upk")));

            // A second check finds nothing to do and downloads nothing.
            UpdateCheckResult again = await UpdateService.CheckAsync(Host, _publicKey, _player);
            Assert.True(again.UpToDate);
            Assert.Equal(1, again.InstalledContentVersion);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(_player, ".mho"), "*.part", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task ARepeatedCheckTrustsThePreviousHashes()
        {
            Publish();
            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);
            await UpdateService.ApplyAsync(check, Host, _player);

            // Damage a file without changing its size or time: the cached hash is trusted until a full check is asked for.
            string sipPath = Path.Combine(_player, Sip.Replace('/', Path.DirectorySeparatorChar));
            DateTime time = File.GetLastWriteTimeUtc(sipPath);
            byte[] bytes = File.ReadAllBytes(sipPath);
            bytes[10] ^= 0xFF;
            File.WriteAllBytes(sipPath, bytes);
            File.SetLastWriteTimeUtc(sipPath, time);

            Assert.True((await UpdateService.CheckAsync(Host, _publicKey, _player)).UpToDate);

            UpdateCheckResult full = await UpdateService.CheckAsync(Host, _publicKey, _player, ignoreCache: true);
            Assert.False(full.UpToDate);
            Assert.Equal(1, full.Plan.FileCount);

            await UpdateService.ApplyAsync(full, Host, _player);
            Assert.Equal(Hash(_edited, Sip), Hash(_player, Sip));
        }

        [Fact]
        public async Task ASecondPublishOnlyDownloadsTheNewChunks()
        {
            Publish();
            await UpdateService.ApplyAsync(await UpdateService.CheckAsync(Host, _publicKey, _player), Host, _player);

            byte[] sip = File.ReadAllBytes(Path.Combine(_edited, Sip));
            for (int i = 0; i < 80; i++) sip[2_000_000 + i] ^= 0x77;
            Write(_edited, Sip, sip);
            Assert.Equal(2, Publish().ContentVersion);

            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);
            Assert.Equal(2, check.Manifest.ContentVersion);
            Assert.Equal(1, check.Plan.FileCount);
            Assert.True(check.Plan.DownloadBytes < 1_500_000, $"downloads {check.Plan.DownloadBytes}");

            ApplyResult result = await UpdateService.ApplyAsync(check, Host, _player);
            Assert.Equal(1, result.FilesUpdated);
            Assert.Equal(Hash(_edited, Sip), Hash(_player, Sip));

            // The stock original is still the one in the backup, not the version-1 file.
            Assert.Equal(Hash(_pristine, Sip), BaselineBuilder.HashFile(Path.Combine(_player, ".mho", "backup", "Data", "Game", "Calligraphy.sip")));
        }

        [Fact]
        public async Task RevertPutsTheStockClientBack()
        {
            Publish();
            await UpdateService.ApplyAsync(await UpdateService.CheckAsync(Host, _publicKey, _player), Host, _player);

            int reverted = UpdateService.Revert(_player);
            Assert.Equal(3, reverted);

            Assert.Equal(Hash(_pristine, Sip), Hash(_player, Sip));
            Assert.Equal(Hash(_pristine, Tfc), Hash(_player, Tfc));
            Assert.False(File.Exists(Path.Combine(_player, NewUpk.Replace('/', Path.DirectorySeparatorChar))));

            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);
            Assert.False(check.UpToDate);
            Assert.Equal(0, check.InstalledContentVersion);
        }

        [Fact]
        public async Task AClientThatAlreadyHasTheEditsIsAdoptedAndRevertLeavesItAlone()
        {
            Publish();

            // The developer's own client: already identical to what was published.
            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _edited);
            Assert.True(check.UpToDate);

            Assert.Equal(0, UpdateService.Revert(_edited));
            Assert.Equal(Hash(_edited, Sip), Hash(_edited, Sip));
            Assert.True(File.Exists(Path.Combine(_edited, NewUpk.Replace('/', Path.DirectorySeparatorChar))));
            Assert.NotEqual(Hash(_pristine, Sip), Hash(_edited, Sip));
        }

        [Fact]
        public async Task ACorruptedChunkOnTheHostChangesNothing()
        {
            Publish();

            // Damage one chunk that a fresh player needs.
            ClientManifest manifest = ManifestIO.Deserialize(File.ReadAllBytes(Path.Combine(_host, "manifest.json")));
            string upk = manifest.Files.First(f => f.Path == NewUpk).Chunks[0].Sha256;
            string chunkPath = Path.Combine(_host, "chunks", upk[..2], upk);
            byte[] chunk = File.ReadAllBytes(chunkPath);
            chunk[^1] ^= 0xFF;
            File.WriteAllBytes(chunkPath, chunk);

            string sipBefore = Hash(_player, Sip);
            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);
            await Assert.ThrowsAsync<UpdateException>(() => UpdateService.ApplyAsync(check, Host, _player));

            // The files that were being processed before the failure may be updated, but nothing is half written and the stock files are still recoverable.
            Assert.Empty(Directory.EnumerateFiles(_player, "*.part", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(_player, NewUpk.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(sipBefore == Hash(_player, Sip) || Hash(_edited, Sip) == Hash(_player, Sip));

            UpdateService.Revert(_player);
            Assert.Equal(Hash(_pristine, Sip), Hash(_player, Sip));
            Assert.Equal(Hash(_pristine, Tfc), Hash(_player, Tfc));
        }

        [Fact]
        public async Task ATamperedOrWronglySignedManifestIsRejected()
        {
            Publish();

            var (_, otherKey) = ManifestSigner.GenerateKeyPair();
            await Assert.ThrowsAsync<ManifestRejectedException>(() => UpdateService.CheckAsync(Host, otherKey, _player));

            string manifestPath = Path.Combine(_host, "manifest.json");
            byte[] bytes = File.ReadAllBytes(manifestPath);
            bytes[bytes.Length / 3] ^= 1;
            File.WriteAllBytes(manifestPath, bytes);
            await Assert.ThrowsAsync<ManifestRejectedException>(() => UpdateService.CheckAsync(Host, _publicKey, _player));

            Assert.Equal(Hash(_pristine, Sip), Hash(_player, Sip));
        }

        [Fact]
        public async Task CancellingMidwayLeavesNoHalfWrittenGameFiles()
        {
            Publish();
            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);

            using CancellationTokenSource cts = new();
            Progress<UpdateProgress> progress = new(p =>
            {
                if (p.FilesDone >= 1)
                    cts.Cancel();
            });

            // Progress callbacks are posted asynchronously, so cancel directly after the first file completes through a wrapping source.
            CancelAfterSource source = new(Host, cts, afterRequests: 3);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateService.ApplyAsync(check, source, _player, progress, cts.Token));

            Assert.Empty(Directory.EnumerateFiles(_player, "*.part", SearchOption.AllDirectories));

            // Running the update again finishes the job.
            UpdateCheckResult retry = await UpdateService.CheckAsync(Host, _publicKey, _player);
            await UpdateService.ApplyAsync(retry, Host, _player);
            Assert.True((await UpdateService.CheckAsync(Host, _publicKey, _player)).UpToDate);
        }

        [Fact]
        public async Task ARemovedFileIsRestoredFromTheBackupOrDeleted()
        {
            Publish();
            await UpdateService.ApplyAsync(await UpdateService.CheckAsync(Host, _publicKey, _player), Host, _player);

            // Version 2: the texture cache and the new package stop being managed (back to stock / gone).
            File.Copy(Path.Combine(_pristine, Tfc.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(_edited, Tfc.Replace('/', Path.DirectorySeparatorChar)), true);
            File.Delete(Path.Combine(_edited, NewUpk.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(2, Publish().ContentVersion);

            UpdateCheckResult check = await UpdateService.CheckAsync(Host, _publicKey, _player);
            Assert.Equal(2, check.Plan.Removed.Count);
            ApplyResult result = await UpdateService.ApplyAsync(check, Host, _player);
            Assert.Equal(2, result.FilesRemoved);

            Assert.Equal(Hash(_pristine, Tfc), Hash(_player, Tfc));
            Assert.False(File.Exists(Path.Combine(_player, NewUpk.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(Hash(_edited, Sip), Hash(_player, Sip));
        }

        [Fact]
        public void TheInspectorFindsTheClientFromAnyPickedFolderAndReadsNothingFromAFakeExe()
        {
            Assert.Equal(Path.GetFullPath(_player), ClientInspector.FindClientRoot(Path.Combine(_player, "UnrealEngine3", "Binaries")));
            Assert.Equal(Path.GetFullPath(_player), ClientInspector.FindClientRoot(Path.Combine(_player, Exe.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Null(ClientInspector.FindClientRoot(_temp));

            ClientInfo info = ClientInspector.Inspect(_player, "1.53.0.203");
            Assert.True(info.Found);
            Assert.NotEmpty(info.Problems);   // the fake exe has no version resource
        }

        /// <summary>Wraps a source and cancels after a number of requests, to test cancelling in the middle of an update.</summary>
        private sealed class CancelAfterSource : IContentSource
        {
            private readonly IContentSource _inner;
            private readonly CancellationTokenSource _cts;
            private readonly int _after;
            private int _count;

            public CancelAfterSource(IContentSource inner, CancellationTokenSource cts, int afterRequests)
            {
                _inner = inner; _cts = cts; _after = afterRequests;
            }

            public async Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref _count) >= _after)
                    _cts.Cancel();

                return await _inner.GetBytesAsync(relativePath, cancellationToken);
            }
        }
    }
}

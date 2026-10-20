using System.Security.Cryptography;
using System.Text.Json;
using OmegaLauncher.Updates.Client;
using OmegaLauncher.Updates.Models;
using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Publishing
{
    /// <summary>Adds a launcher build to the host folder and signs launcher/latest.json. Platforms of the same version accumulate in one release.</summary>
    public static class LauncherPublisher
    {
        public static LauncherRelease Publish(string builtFile, string platform, string version, string privateKeyPemPath, string outputDirectory, string notes = "")
        {
            if (File.Exists(builtFile) == false)
                throw new IOException($"{builtFile} does not exist.");

            if (LauncherUpdater.ParseVersion(version) == null)
                throw new ArgumentException($"'{version}' is not a version like 1.0.1.");

            if (string.IsNullOrWhiteSpace(platform) || platform.Any(c => char.IsLetterOrDigit(c) == false && c != '-'))
                throw new ArgumentException($"'{platform}' is not a platform like win-x64 or linux-x64.");

            byte[] data = File.ReadAllBytes(builtFile);
            string extension = Path.GetExtension(builtFile);
            string relativePath = $"{UpdateConstants.LauncherDirectory}/OmegaLauncher-{platform}-{version}{extension}";

            string launcherDirectory = Path.Combine(outputDirectory, UpdateConstants.LauncherDirectory);
            Directory.CreateDirectory(launcherDirectory);
            ManifestIO.WriteAtomic(Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)), data);

            string releasePath = Path.Combine(outputDirectory, UpdateConstants.LauncherReleaseFileName.Replace('/', Path.DirectorySeparatorChar));
            LauncherRelease release = null;
            if (File.Exists(releasePath))
            {
                LauncherRelease existing = JsonSerializer.Deserialize(File.ReadAllBytes(releasePath), UpdatesJsonContext.Default.LauncherRelease);
                if (existing != null && LauncherUpdater.ParseVersion(existing.Version) == LauncherUpdater.ParseVersion(version))
                    release = existing;
            }

            release ??= new LauncherRelease();
            release.Version = version;
            release.Notes = string.IsNullOrEmpty(notes) ? release.Notes : notes;
            release.PublishedUtc = DateTime.UtcNow;
            release.Files.RemoveAll(f => string.Equals(f.Platform, platform, StringComparison.OrdinalIgnoreCase));
            release.Files.Add(new LauncherReleaseFile
            {
                Platform = platform,
                Path = relativePath,
                Size = data.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
            });

            byte[] releaseBytes = JsonSerializer.SerializeToUtf8Bytes(release, UpdatesJsonContext.Default.LauncherRelease);
            string signature = ManifestSigner.Sign(releaseBytes, File.ReadAllText(privateKeyPemPath));

            // Two files cannot change at the same instant; a launcher that reads in between fails the signature check and simply tries again next start.
            ManifestIO.WriteAtomic(Path.Combine(outputDirectory, UpdateConstants.LauncherReleaseSignatureFileName.Replace('/', Path.DirectorySeparatorChar)), signature);
            ManifestIO.WriteAtomic(releasePath, releaseBytes);
            return release;
        }
    }
}

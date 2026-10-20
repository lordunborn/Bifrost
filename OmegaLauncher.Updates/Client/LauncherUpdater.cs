using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OmegaLauncher.Updates.Content;
using OmegaLauncher.Updates.Models;
using OmegaLauncher.Updates.Publishing;
using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Client
{
    public sealed class LauncherUpdateInfo
    {
        public LauncherRelease Release { get; init; }
        public LauncherReleaseFile File { get; init; }
        public Version Version { get; init; }
    }

    /// <summary>
    /// Finds out whether the server publishes a newer launcher and downloads it, verified by signature and hash.
    /// Installing (swapping the running program for the new one) is done by the application, which owns the process.
    /// </summary>
    public static class LauncherUpdater
    {
        /// <summary>"win-x64", "linux-x64" and so on for the machine this runs on.</summary>
        public static string CurrentPlatform()
        {
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unknown";
            string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            return $"{os}-{arch}";
        }

        /// <summary>Compares version strings like "1.0.1" (extra parts are ignored); null if it cannot be read.</summary>
        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Version.TryParse(text.Trim(), out Version version) == false)
                return null;

            return new Version(Math.Max(version.Major, 0), Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
        }

        /// <summary>The newer launcher for this platform, or null if the installed one is current or the server publishes none.</summary>
        /// <exception cref="ManifestRejectedException">The release information is not signed by the server's key.</exception>
        public static async Task<LauncherUpdateInfo> CheckAsync(IContentSource source, string publicKeyBase64, Version current, string platform, CancellationToken cancellationToken = default)
        {
            byte[] releaseBytes = await source.GetBytesAsync(UpdateConstants.LauncherReleaseFileName, cancellationToken);
            byte[] signatureBytes = await source.GetBytesAsync(UpdateConstants.LauncherReleaseSignatureFileName, cancellationToken);

            if (releaseBytes == null || signatureBytes == null)
                return null;

            if (ManifestSigner.Verify(releaseBytes, Encoding.UTF8.GetString(signatureBytes), publicKeyBase64) == false)
                throw new ManifestRejectedException("The launcher update information is not signed by the server's key. It was rejected.");

            LauncherRelease release = System.Text.Json.JsonSerializer.Deserialize(releaseBytes, UpdatesJsonContext.Default.LauncherRelease);
            Version published = ParseVersion(release?.Version);
            if (published == null || published <= current)
                return null;

            LauncherReleaseFile file = release.Files.FirstOrDefault(f => string.Equals(f.Platform, platform, StringComparison.OrdinalIgnoreCase));
            if (file == null || IsSafePath(file.Path) == false || ManifestIO.IsHexSha256(file.Sha256) == false || file.Size <= 0)
                return null;

            return new LauncherUpdateInfo { Release = release, File = file, Version = published };
        }

        /// <summary>Downloads the new launcher to <paramref name="targetPath"/> after checking its size and SHA-256. Nothing is left behind on failure.</summary>
        public static async Task DownloadAsync(IContentSource source, LauncherUpdateInfo info, string targetPath, CancellationToken cancellationToken = default)
        {
            byte[] data = await source.GetBytesAsync(info.File.Path, cancellationToken)
                ?? throw new UpdateException("The new launcher is not on the update server.");

            if (data.LongLength != info.File.Size || string.Equals(Convert.ToHexString(SHA256.HashData(data)), info.File.Sha256, StringComparison.OrdinalIgnoreCase) == false)
                throw new UpdateException("The downloaded launcher did not match what the server published, so it was discarded.");

            string partPath = targetPath + ".part";
            try
            {
                await File.WriteAllBytesAsync(partPath, data, cancellationToken);
                File.Move(partPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partPath))
                    File.Delete(partPath);
            }
        }

        private static bool IsSafePath(string path)
        {
            return string.IsNullOrEmpty(path) == false && path.StartsWith(UpdateConstants.LauncherDirectory + "/", StringComparison.Ordinal) &&
                   path.Contains("..") == false && path.Contains('\\') == false && path.Contains(':') == false;
        }
    }
}

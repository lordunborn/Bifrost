using System.Diagnostics;
using System.Reflection;

namespace OmegaLauncher.Core.Updates
{
    /// <summary>
    /// Replaces the running launcher program with a downloaded one. A running program cannot be overwritten on Windows, but it can be renamed:
    /// the old file moves aside to ".old", the new file takes its name, and the new launcher is started. The ".old" file is deleted on the next start.
    /// </summary>
    public static class SelfUpdate
    {
        public const string ProgramName = "OmegaLauncher";

        public static string CurrentExecutablePath => Environment.ProcessPath;

        /// <summary>False when running from source through the dotnet host or under another name, where swapping the "program" would break the SDK.</summary>
        public static bool IsSupported
        {
            get
            {
                string path = CurrentExecutablePath;
                return string.IsNullOrEmpty(path) == false &&
                       string.Equals(Path.GetFileNameWithoutExtension(path), ProgramName, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static Version CurrentVersion
        {
            get
            {
                Version version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
                return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
            }
        }

        /// <summary>Where the downloaded launcher is stored until it is installed.</summary>
        public static string DownloadPath => CurrentExecutablePath + ".new";

        /// <summary>Deletes leftovers of an earlier update. Failures are ignored: a file still in use is removed on a later start.</summary>
        public static void CleanUpLeftovers()
        {
            string path = CurrentExecutablePath;
            if (IsSupported == false)
                return;

            foreach (string suffix in new[] { ".old", ".new", ".new.part" })
            {
                try
                {
                    string leftover = path + suffix;
                    if (File.Exists(leftover))
                        File.Delete(leftover);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>Puts <paramref name="newFile"/> in place of <paramref name="currentExecutable"/>, keeping the old program as ".old". Rolls back if anything fails.</summary>
        public static void Install(string currentExecutable, string newFile)
        {
            if (File.Exists(newFile) == false)
                throw new IOException("The downloaded launcher is missing.");

            string oldPath = currentExecutable + ".old";

            if (File.Exists(oldPath))
                File.Delete(oldPath);

            File.Move(currentExecutable, oldPath);

            try
            {
                File.Move(newFile, currentExecutable);

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(currentExecutable,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
            catch
            {
                // Put the working launcher back before reporting the failure.
                if (File.Exists(currentExecutable))
                    File.Delete(currentExecutable);

                File.Move(oldPath, currentExecutable);
                throw;
            }
        }

        /// <summary>Starts the (new) launcher with the same command line arguments.</summary>
        public static void Restart()
        {
            ProcessStartInfo startInfo = new(CurrentExecutablePath) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            foreach (string argument in Environment.GetCommandLineArgs().Skip(1))
                startInfo.ArgumentList.Add(argument);

            Process.Start(startInfo);
        }
    }
}

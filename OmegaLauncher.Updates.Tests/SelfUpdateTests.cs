using OmegaLauncher.Core.Updates;

namespace OmegaLauncher.Updates.Tests
{
    public class SelfUpdateTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "mho-selfupdate-" + Guid.NewGuid().ToString("N"));

        public SelfUpdateTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, true);

        private string Write(string name, string content)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Install_NewFileTakesTheName_OldProgramIsKeptAsOld()
        {
            string current = Write("OmegaLauncher.exe", "old program");
            string downloaded = Write("OmegaLauncher.exe.new", "new program");

            SelfUpdate.Install(current, downloaded);

            Assert.Equal("new program", File.ReadAllText(current));
            Assert.Equal("old program", File.ReadAllText(current + ".old"));
            Assert.False(File.Exists(downloaded));
        }

        [Fact]
        public void Install_ReplacesAnOldFileLeftByAnEarlierUpdate()
        {
            string current = Write("OmegaLauncher.exe", "v2");
            Write("OmegaLauncher.exe.old", "v1");
            string downloaded = Write("OmegaLauncher.exe.new", "v3");

            SelfUpdate.Install(current, downloaded);

            Assert.Equal("v3", File.ReadAllText(current));
            Assert.Equal("v2", File.ReadAllText(current + ".old"));
        }

        [Fact]
        public void Install_MissingDownload_ChangesNothing()
        {
            string current = Write("OmegaLauncher.exe", "old program");

            Assert.Throws<IOException>(() => SelfUpdate.Install(current, Path.Combine(_dir, "nothing.new")));

            Assert.Equal("old program", File.ReadAllText(current));
            Assert.False(File.Exists(current + ".old"));
        }

        [Fact]
        public void Install_NewProgramIsExecutableOnLinux()
        {
            if (OperatingSystem.IsWindows())
                return;

            string current = Write("OmegaLauncher", "old program");
            string downloaded = Write("OmegaLauncher.new", "new program");   // a freshly downloaded file has no executable bit

            SelfUpdate.Install(current, downloaded);

            Assert.True(File.GetUnixFileMode(current).HasFlag(UnixFileMode.UserExecute));
        }

        [Fact]
        public void Install_NewFileCannotBeMoved_PutsTheOldProgramBack()
        {
            // Needs a file that cannot be moved. Windows refuses to move an open file; Linux allows it, so the case cannot be set up there
            // (the rollback code is the same on both).
            if (OperatingSystem.IsWindows() == false)
                return;

            string current = Write("OmegaLauncher.exe", "old program");
            string downloaded = Write("OmegaLauncher.exe.new", "new program");

            // Keep the new file open so the move fails, like an antivirus scan or a locked file would.
            using FileStream lockedNew = new(downloaded, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.ThrowsAny<IOException>(() => SelfUpdate.Install(current, downloaded));

            Assert.Equal("old program", File.ReadAllText(current));
        }
    }
}

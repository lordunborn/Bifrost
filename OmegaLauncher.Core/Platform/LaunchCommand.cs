using System.Diagnostics;

namespace OmegaLauncher.Core.Platform
{
    /// <summary>Everything needed to start the game: the program, its arguments, extra environment variables and the folder to start in.</summary>
    public sealed class LaunchCommand
    {
        public string FileName { get; init; }
        public List<string> Arguments { get; init; } = new();
        public Dictionary<string, string> Environment { get; init; } = new();
        public string WorkingDirectory { get; init; }

        public ProcessStartInfo ToStartInfo()
        {
            ProcessStartInfo startInfo = new(FileName) { UseShellExecute = false };

            foreach (string argument in Arguments)
                startInfo.ArgumentList.Add(argument);

            foreach (var kvp in Environment)
                startInfo.Environment[kvp.Key] = kvp.Value;

            if (string.IsNullOrEmpty(WorkingDirectory) == false)
                startInfo.WorkingDirectory = WorkingDirectory;

            return startInfo;
        }

        /// <summary>The command as a shell line, for logs and the --print-launch dry run.</summary>
        public string Describe()
        {
            List<string> lines = new();

            foreach (var kvp in Environment)
                lines.Add($"{kvp.Key}={Quote(kvp.Value)}");

            if (string.IsNullOrEmpty(WorkingDirectory) == false)
                lines.Add($"cd {Quote(WorkingDirectory)}");

            lines.Add(string.Join(' ', new[] { FileName }.Concat(Arguments).Select(Quote)));
            return string.Join(System.Environment.NewLine, lines);
        }

        private static string Quote(string value)
        {
            return value.Length > 0 && value.IndexOfAny(new[] { ' ', '"', '\'', '$', '&', ';', '(', ')' }) < 0 ? value : "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}

using System.Text.RegularExpressions;

namespace OmegaLauncher.Updates.Publishing
{
    /// <summary>
    /// Small glob matcher for the publisher's exclude list. Paths use forward slashes and are matched case-insensitively.
    /// "*" matches within one path segment, "**" matches across segments, "?" matches one character. A pattern without a "/" matches the file name
    /// in any folder (so "*.bak*" excludes backups everywhere).
    /// </summary>
    public sealed class GlobMatcher
    {
        private readonly List<Regex> _patterns = new();

        public GlobMatcher(IEnumerable<string> patterns)
        {
            foreach (string pattern in patterns)
            {
                string trimmed = pattern.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                _patterns.Add(ToRegex(trimmed));
            }
        }

        public bool IsMatch(string relativePath)
        {
            foreach (Regex regex in _patterns)
            {
                if (regex.IsMatch(relativePath))
                    return true;
            }

            return false;
        }

        private static Regex ToRegex(string pattern)
        {
            pattern = pattern.Replace('\\', '/');
            bool anyFolder = pattern.Contains('/') == false;
            System.Text.StringBuilder sb = new();
            sb.Append(anyFolder ? "(^|/)" : "^");

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }

            // A folder pattern such as "Logs/**" also matches the files inside it; a plain name matches the whole segment.
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}

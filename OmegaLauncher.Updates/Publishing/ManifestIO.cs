using System.Text;
using System.Text.Json;
using OmegaLauncher.Updates.Models;
using OmegaLauncher.Updates.Security;

namespace OmegaLauncher.Updates.Publishing
{
    public static class ManifestIO
    {
        public static byte[] Serialize(ClientManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, UpdatesJsonContext.Default.ClientManifest);

        public static ClientManifest Deserialize(byte[] data) => JsonSerializer.Deserialize(data, UpdatesJsonContext.Default.ClientManifest);

        public static void WriteAtomic(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, path, true);
        }

        public static void WriteAtomic(string path, string text) => WriteAtomic(path, new UTF8Encoding(false).GetBytes(text));

        /// <summary>Structural checks every consumer of a manifest must pass, whatever it does with it. Returns a list of problems (empty = valid).</summary>
        public static List<string> Validate(ClientManifest manifest)
        {
            List<string> problems = new();

            if (manifest == null)
            {
                problems.Add("Manifest could not be read.");
                return problems;
            }

            if (manifest.Schema != UpdateConstants.ManifestSchema)
                problems.Add($"Unsupported manifest schema {manifest.Schema}.");

            if (manifest.ContentVersion < 1)
                problems.Add("Content version must be 1 or higher.");

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (ManifestFile file in manifest.Files)
            {
                if (ClientPathGuard.IsSafeRelativePath(file.Path) == false)
                {
                    problems.Add($"Unsafe path: {file.Path}");
                    continue;
                }

                if (seen.Add(file.Path) == false)
                    problems.Add($"Duplicate path: {file.Path}");

                long total = 0;
                foreach (ChunkRef chunk in file.Chunks)
                {
                    if (chunk.Size <= 0 || chunk.Size > UpdateConstants.ChunkMax || IsHexSha256(chunk.Sha256) == false)
                    {
                        problems.Add($"Bad chunk in {file.Path}");
                        break;
                    }

                    total += chunk.Size;
                }

                if (total != file.Size)
                    problems.Add($"Chunk sizes do not add up to the file size: {file.Path}");

                if (IsHexSha256(file.Sha256) == false)
                    problems.Add($"Bad file hash: {file.Path}");
            }

            foreach (string removed in manifest.Removed)
            {
                if (ClientPathGuard.IsSafeRelativePath(removed) == false)
                    problems.Add($"Unsafe removed path: {removed}");
            }

            return problems;
        }

        public static bool IsHexSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;

            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }

            return true;
        }
    }
}

using System.Security.Cryptography;
using Bifrost.Updates.Chunking;
using Bifrost.Updates.Content;
using Bifrost.Updates.Models;

namespace Bifrost.Updates.Publishing
{
    public class VerifyResult
    {
        public int ContentVersion { get; set; }
        public int Files { get; set; }
        public int ChunksChecked { get; set; }
        public long BytesChecked { get; set; }
        public List<string> Errors { get; } = new();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Downloads what the host publishes and checks it the way a launcher would: signature, manifest structure, every chunk present, decodable and hashing
    /// to its name. With <c>deep</c> it also re-assembles each file and checks the whole-file SHA-256.
    /// </summary>
    public static class HostVerifier
    {
        public static async Task<VerifyResult> VerifyAsync(IContentSource source, string publicKeyBase64, bool deep, Action<string> log = null, CancellationToken cancellationToken = default)
        {
            log ??= _ => { };
            VerifyResult result = new();

            byte[] manifestBytes = await source.GetBytesAsync(UpdateConstants.ManifestFileName, cancellationToken);
            byte[] signatureBytes = await source.GetBytesAsync(UpdateConstants.SignatureFileName, cancellationToken);
            if (manifestBytes == null || signatureBytes == null)
            {
                result.Errors.Add("manifest.json or manifest.sig is missing on the host.");
                return result;
            }

            if (Security.ManifestSigner.Verify(manifestBytes, System.Text.Encoding.UTF8.GetString(signatureBytes), publicKeyBase64) == false)
            {
                result.Errors.Add("Manifest signature is NOT valid for this public key.");
                return result;
            }

            ClientManifest manifest = ManifestIO.Deserialize(manifestBytes);
            result.Errors.AddRange(ManifestIO.Validate(manifest));
            if (result.Ok == false)
                return result;

            result.ContentVersion = manifest.ContentVersion;
            result.Files = manifest.Files.Count;

            if (await source.GetBytesAsync(UpdateConstants.BaselineSizesFileName, cancellationToken) == null)
                result.Errors.Add("baseline-sizes.json is missing on the host.");

            HashSet<string> verified = new();
            object gate = new();

            foreach (ManifestFile file in manifest.Files)
            {
                using IncrementalHash fileHash = deep ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

                // Chunks of one file are fetched in order when deep (the whole-file hash needs the order); otherwise they are checked in parallel.
                if (deep)
                {
                    foreach (ChunkRef chunk in file.Chunks)
                    {
                        byte[] data = await CheckChunkAsync(source, chunk, file.Path, result, gate, verified, cancellationToken);
                        if (data != null)
                            fileHash.AppendData(data);
                    }

                    string hash = Convert.ToHexString(fileHash.GetHashAndReset()).ToLowerInvariant();
                    if (hash != file.Sha256 && result.Ok)
                        result.Errors.Add($"Whole-file hash mismatch: {file.Path}");
                }
                else
                {
                    await Parallel.ForEachAsync(file.Chunks, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
                        async (chunk, ct) => await CheckChunkAsync(source, chunk, file.Path, result, gate, verified, ct));
                }

                log($"  {file.Path}: {file.Chunks.Count} chunks");
            }

            return result;
        }

        private static async Task<byte[]> CheckChunkAsync(IContentSource source, ChunkRef chunk, string filePath, VerifyResult result, object gate, HashSet<string> verified, CancellationToken ct)
        {
            bool first;
            lock (gate)
                first = verified.Add(chunk.Sha256);

            // A chunk shared by several files only needs checking once, but a deep verify needs its bytes again for the next file.
            byte[] stored = await source.GetBytesAsync(ChunkStorePath.Of(chunk.Sha256), ct);
            if (stored == null)
            {
                lock (gate) result.Errors.Add($"Missing chunk {chunk.Sha256} ({filePath})");
                return null;
            }

            byte[] data = ChunkCodec.Decode(stored, chunk.Size);
            if (data == null)
            {
                lock (gate) result.Errors.Add($"Chunk {chunk.Sha256} does not decode to {chunk.Size} bytes ({filePath})");
                return null;
            }

            string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            if (hash != chunk.Sha256)
            {
                lock (gate) result.Errors.Add($"Chunk {chunk.Sha256} has the wrong content ({filePath})");
                return null;
            }

            if (first)
            {
                lock (gate)
                {
                    result.ChunksChecked++;
                    result.BytesChecked += data.Length;
                }
            }

            return data;
        }
    }
}

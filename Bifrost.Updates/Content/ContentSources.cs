namespace Bifrost.Updates.Content
{
    /// <summary>Where the published files (manifest, signature, chunks) are read from: a web host or, for tests and verification, a folder.</summary>
    public interface IContentSource
    {
        /// <summary>Reads a published file by its path relative to the content root. Returns null if it does not exist.</summary>
        Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken = default);
    }

    public sealed class DirectoryContentSource : IContentSource
    {
        private readonly string _root;

        public DirectoryContentSource(string root)
        {
            _root = Path.GetFullPath(root);
        }

        public async Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            string path = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (path.StartsWith(_root, StringComparison.OrdinalIgnoreCase) == false || File.Exists(path) == false)
                return null;

            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
    }

    public sealed class HttpContentSource : IContentSource, IDisposable
    {
        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly int _attempts;

        public HttpContentSource(string baseUrl, int attempts = 3, TimeSpan? timeout = null)
        {
            if (baseUrl.EndsWith('/') == false)
                baseUrl += "/";

            _baseUri = new Uri(baseUrl);
            _attempts = Math.Max(1, attempts);
            _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MHOLauncher");
        }

        public async Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Uri uri = new(_baseUri, relativePath);
            Exception last = null;

            for (int attempt = 1; attempt <= _attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using HttpResponseMessage response = await _http.GetAsync(uri, cancellationToken);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return null;

                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;

                    last = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
                }
            }

            throw new IOException($"Could not download {relativePath}: {last?.Message}", last);
        }

        public void Dispose() => _http.Dispose();
    }

    public static class ChunkStorePath
    {
        public static string Of(string sha256) => $"{UpdateConstants.ChunkDirectory}/{sha256[..2]}/{sha256}";
    }
}

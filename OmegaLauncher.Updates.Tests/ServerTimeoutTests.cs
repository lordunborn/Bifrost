using OmegaLauncher.Updates.Client;
using OmegaLauncher.Updates.Content;

namespace OmegaLauncher.Updates.Tests
{
    public class ServerTimeoutTests
    {
        /// <summary>A server that accepts the request and never answers.</summary>
        private sealed class HangingSource : IContentSource
        {
            public async Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }
        }

        private sealed class UnreachableSource : IContentSource
        {
            public Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken = default)
                => throw new IOException("connection refused");
        }

        [Fact]
        public async Task Check_ServerThatNeverAnswers_FailsWithinTheDeadline()
        {
            DateTime start = DateTime.UtcNow;

            UpdateException ex = await Assert.ThrowsAsync<UpdateException>(() =>
                UpdateService.CheckAsync(new HangingSource(), "key", Path.GetTempPath(), serverTimeout: TimeSpan.FromMilliseconds(300)));

            Assert.Contains("did not answer in time", ex.Message);
            Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Check_UnreachableServer_SaysSo()
        {
            UpdateException ex = await Assert.ThrowsAsync<UpdateException>(() => UpdateService.CheckAsync(new UnreachableSource(), "key", Path.GetTempPath()));

            Assert.Contains("could not be reached", ex.Message);
        }

        [Fact]
        public async Task Check_CancelledByThePlayer_IsACancelNotATimeout()
        {
            using CancellationTokenSource cts = new();
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                UpdateService.CheckAsync(new HangingSource(), "key", Path.GetTempPath(), cancellationToken: cts.Token, serverTimeout: TimeSpan.FromSeconds(30)));
        }
    }
}

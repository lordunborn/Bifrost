using OmegaLauncher.Core.Updates;

namespace OmegaLauncher.Updates.Tests
{
    public class ContentTokenTests
    {
        // The same fixed examples the server's ClientContentTokenTests use (computed independently with Python hmac/hashlib).
        [Theory]
        [InlineData(5, "mho1.5.db0fe923aa093663eb04dcce96cfc6e7")]
        [InlineData(1, "mho1.1.0f90528d46b2d92d9c92a39f77315295")]
        public void Create_MatchesTheServersExample(int version, string expected)
        {
            Assert.Equal(expected, ContentToken.Create(version, "test-secret"));
        }
    }
}

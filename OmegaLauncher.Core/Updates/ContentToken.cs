using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OmegaLauncher.Core.Updates
{
    /// <summary>
    /// The token the launcher passes to the game client (-webtoken) so the server can tell which version of the launcher-managed files the player has.
    /// Format: "mho1.{contentVersion}.{first 32 hex characters of HMAC-SHA256(secret, "mho1.{contentVersion}")}".
    /// The server has the same algorithm (MHServerEmu.Core ClientContentToken); both are tested against the same fixed examples.
    /// </summary>
    public static class ContentToken
    {
        public const string Prefix = "mho1";
        private const int MacLength = 32;

        public static string Create(int contentVersion, string secret)
        {
            string payload = $"{Prefix}.{contentVersion.ToString(CultureInfo.InvariantCulture)}";
            byte[] mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
            return $"{payload}.{Convert.ToHexString(mac).ToLowerInvariant()[..MacLength]}";
        }
    }
}

using System.Security.Cryptography;

namespace Bifrost.Updates.Security
{
    /// <summary>
    /// ECDSA P-256 / SHA-256 signing of manifest.json. The launcher embeds the public key (base64 SubjectPublicKeyInfo); the private key (PEM) never leaves
    /// the publisher's machine.
    /// </summary>
    public static class ManifestSigner
    {
        public static (string PrivateKeyPem, string PublicKeyBase64) GenerateKeyPair()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (key.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        public static string Sign(byte[] data, string privateKeyPem)
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(privateKeyPem);
            return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));
        }

        public static bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
        {
            try
            {
                using ECDsa key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
                return key.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
            {
                return false;
            }
        }
    }
}

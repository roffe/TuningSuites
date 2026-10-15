using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace CommonSuite
{
    /// <summary>
    /// Tuning package (.t7x/.t8x) crypto: lines are AES encrypted with a fixed key, the MD5 of the decrypted content is
    /// RSA/SHA1 signed and checked against T8Pub.pem next to the executable. The old crypt32/advapi32 PEM import is replaced
    /// by RSA.ImportFromPem.
    /// </summary>
    public class Crypto
    {
        public static string CalculateMD5Hash(string input) =>
            Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(input)));

        public static string DecodeAES(string input)
        {
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes("T8SuiteTunesSaab"); // 16 bytes secret key
            aes.IV = Encoding.UTF8.GetBytes("1234567812345678");
            byte[] cipher = Convert.FromBase64String(input);
            return Encoding.UTF8.GetString(aes.DecryptCbc(cipher, aes.IV));
        }

        public static bool VerifyRSASignature(string data, string expectedSignature) =>
            VerifyRSASignature(data, expectedSignature, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "T8Pub.pem")));

        // DigestInfo of a SHA-1 hash (RFC 8017 9.2)
        private static readonly byte[] Sha1DigestInfo = [0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2b, 0x0e, 0x03, 0x02, 0x1a, 0x05, 0x00, 0x04, 0x14];

        /// <summary>
        /// RSASSA-PKCS1-v1_5 with SHA-1 by hand: Fedora 41+ (crypto-policies) has OpenSSL refuse SHA-1 signatures, RSA.VerifyData threw
        /// there and every pack was left out. The packs are signed with a key we don't have, so they can't move to SHA-256.
        /// </summary>
        internal static bool VerifyRSASignature(string data, string expectedSignature, string publicKeyPem)
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            RSAParameters key = rsa.ExportParameters(false);
            int k = key.Modulus.Length;
            byte[] signature = Convert.FromBase64String(expectedSignature);
            var n = new BigInteger(key.Modulus, true, true);
            var s = new BigInteger(signature, true, true);
            if (signature.Length != k || s >= n) return false;
            byte[] m = BigInteger.ModPow(s, new BigInteger(key.Exponent, true, true), n).ToByteArray(true, true);
            byte[] em = new byte[k];
            m.CopyTo(em, k - m.Length);
            // EM = 00 01 FF..FF 00 DigestInfo hash, compared whole
            byte[] t = [.. Sha1DigestInfo, .. SHA1.HashData(Encoding.UTF8.GetBytes(data))];
            byte[] expected = new byte[k];
            expected[1] = 1;
            expected.AsSpan(2, k - t.Length - 3).Fill(0xff);
            t.CopyTo(expected, k - t.Length);
            return CryptographicOperations.FixedTimeEquals(em, expected);
        }
    }
}

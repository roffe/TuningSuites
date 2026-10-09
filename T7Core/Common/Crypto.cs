using System;
using System.IO;
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

        internal static bool VerifyRSASignature(string data, string expectedSignature, string publicKeyPem)
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(data), Convert.FromBase64String(expectedSignature), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace SteamDownloader
{
    public static class CredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamCollectionDownloader.v1");

        public static string Encrypt(string plaintext)
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Decrypt(string ciphertext)
        {
            var protectedBytes = Convert.FromBase64String(ciphertext);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

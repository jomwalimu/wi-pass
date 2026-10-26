using System;
using System.Security.Cryptography;
using System.Text;

namespace WiPass.Core
{
    /// <summary>
    /// Encrypts passwords with Windows DPAPI (current user).
    /// The stored value can only be decrypted by the same Windows account on the same PC.
    /// </summary>
    internal static class SecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WiPass.v1.password");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                Log.Warn("비밀번호 복호화 실패: " + ex.GetType().Name);
                return "";
            }
        }
    }
}

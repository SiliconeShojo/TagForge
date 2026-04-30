using System;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;

namespace TagForge.Services
{
    public class SecurityService : ISecurityService
    {
        private readonly byte[]? _machineSalt;

        public SecurityService()
        {
            _machineSalt = GetMachineSalt();
        }

        public string? Encrypt(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return null;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var data = Encoding.UTF8.GetBytes(plainText);
                    var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                    return Convert.ToBase64String(encrypted);
                }
                else
                {
                    // Fallback for Linux/macOS: Obfuscation with machine salt
                    return Obfuscate(plainText);
                }
            }
            catch
            {
                return null;
            }
        }

        public string? Decrypt(string? encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return null;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var data = Convert.FromBase64String(encryptedText);
                    var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decrypted);
                }
                else
                {
                    // Fallback for Linux/macOS: De-obfuscation
                    return Deobfuscate(encryptedText);
                }
            }
            catch
            {
                return null;
            }
        }

        private string? Obfuscate(string plainText)
        {
            if (_machineSalt == null) return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            
            var data = Encoding.UTF8.GetBytes(plainText);
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ _machineSalt[i % _machineSalt.Length]);
            }
            return "tf!" + Convert.ToBase64String(result); // Prefix to distinguish from DPAPI
        }

        private string? Deobfuscate(string encryptedText)
        {
            if (!encryptedText.StartsWith("tf!")) return null; // Not our obfuscated format
            
            var base64 = encryptedText.Substring(3);
            var data = Convert.FromBase64String(base64);
            if (_machineSalt == null) return Encoding.UTF8.GetString(data);

            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ _machineSalt[i % _machineSalt.Length]);
            }
            return Encoding.UTF8.GetString(result);
        }

        private byte[]? GetMachineSalt()
        {
            try
            {
                // Combine Machine Name, User Name and first MAC address for a reasonably unique salt
                var sb = new StringBuilder();
                sb.Append(Environment.MachineName);
                sb.Append(Environment.UserName);

                var ni = NetworkInterface.GetAllNetworkInterfaces()
                    .OrderBy(n => n.Id)
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up);
                
                if (ni != null)
                {
                    sb.Append(ni.GetPhysicalAddress().ToString());
                }

                using var sha = SHA256.Create();
                return sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            }
            catch
            {
                return null;
            }
        }
    }
}

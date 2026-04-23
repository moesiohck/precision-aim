using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using AimAssistPro.Models;

namespace AimAssistPro.Services
{
    public class LicenseManager
    {
        private LicenseInfo? _currentLicense;

        public LicenseInfo? CurrentLicense => _currentLicense;
        public bool IsActivated => _currentLicense?.Status == LicenseStatus.Active;

        public LicenseManager()
        {
            // Initialized empty. Wait for App.xaml.cs to set the online license.
        }

        public void SetOnlineLicense(string username, string plan, DateTime expiresAt, string? licenseKey = null)
        {
            _currentLicense = new LicenseInfo
            {
                Key = licenseKey ?? "(Online)", 
                Hwid = GetHardwareId(),
                Status = expiresAt > DateTime.Now ? LicenseStatus.Active : LicenseStatus.Expired,
                ActivatedAt = DateTime.Now,
                ExpiresAt = expiresAt,
                PlanName = string.IsNullOrEmpty(plan) ? "Standard" : plan
            };
        }

        // ─── HWID Generation ─────────────────────────────────────────────────
        public static string GetHardwareId()
        {
            try
            {
                var sb = new StringBuilder();

                // CPU ID
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                    foreach (ManagementObject mo in searcher.Get())
                        sb.Append(mo["ProcessorId"]?.ToString() ?? "");

                // Disk serial
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
                    foreach (ManagementObject mo in searcher.Get())
                    { sb.Append(mo["SerialNumber"]?.ToString()?.Trim() ?? ""); break; }

                // Board serial
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                    foreach (ManagementObject mo in searcher.Get())
                        sb.Append(mo["SerialNumber"]?.ToString() ?? "");

                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
                var raw = Convert.ToHexString(hash)[..16].ToUpper();
                return $"HW-{raw[..8]}-{raw[8..16]}";
            }
            catch
            {
                return "HW-00000000-00000000";
            }
        }

        // ─── Session Token Management ─────────────────────────────────────────
        // PROTEÇÃO: AES-256-GCM com chave derivada do HWID da máquina.
        // O session.dat é vinculado ao hardware — impossível copiar para outro PC.
        // Qualquer edição manual do arquivo invalida a tag GCM e força novo login.

        private static string TokenPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AimAssistPro", "session.dat");

        /// <summary>
        /// Deriva chave AES-256 de 32 bytes a partir do HWID único da máquina.
        /// Usa PBKDF2/SHA-256 com 10.000 iterações.
        /// </summary>
        private static byte[] DeriveSessionKey()
        {
            var hwid = Encoding.UTF8.GetBytes(GetHardwareId());
            // Salt compilado — combinado com o HWID único torna a chave irrepetível
            var salt = new byte[] {
                0x41, 0x50, 0x52, 0x2D, 0x53, 0x45, 0x53, 0x53,
                0x2D, 0x4B, 0x45, 0x59, 0x2D, 0x56, 0x31, 0x00
            };
            using var kdf = new Rfc2898DeriveBytes(hwid, salt, 10_000, HashAlgorithmName.SHA256);
            return kdf.GetBytes(32);
        }

        /// <summary>
        /// Salva o token criptografado em disco com AES-256-GCM.
        /// Formato: [nonce 12 bytes][tag 16 bytes][ciphertext]
        /// </summary>
        public static void SaveSession(string token)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);

                var key       = DeriveSessionKey();
                var plaintext = Encoding.UTF8.GetBytes(token);
                var nonce     = new byte[12];
                RandomNumberGenerator.Fill(nonce);

                var ciphertext = new byte[plaintext.Length];
                var tag        = new byte[16];

                using var aes = new AesGcm(key, 16);
                aes.Encrypt(nonce, plaintext, ciphertext, tag);

                using var ms = new MemoryStream();
                ms.Write(nonce);
                ms.Write(tag);
                ms.Write(ciphertext);
                File.WriteAllBytes(TokenPath, ms.ToArray());
            }
            catch { }
        }

        /// <summary>
        /// Carrega e descriptografa o token.
        /// Retorna null se o arquivo não existir, estiver corrompido ou for de outra máquina.
        /// </summary>
        public static string? LoadSession()
        {
            try
            {
                if (!File.Exists(TokenPath)) return null;

                var data = File.ReadAllBytes(TokenPath);
                if (data.Length < 29) return null; // nonce(12) + tag(16) + min 1 byte

                var key        = DeriveSessionKey();
                var nonce      = data[..12];
                var tag        = data[12..28];
                var ciphertext = data[28..];
                var plaintext  = new byte[ciphertext.Length];

                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                // Se a tag GCM não bater → lança AuthenticationTagMismatchException

                return Encoding.UTF8.GetString(plaintext);
            }
            catch
            {
                // Arquivo adulterado ou de outra máquina → limpa e força novo login
                ClearSession();
                return null;
            }
        }

        public static void ClearSession()
        {
            try { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
            catch { }
        }

        public void Deactivate()
        {
            _currentLicense = null;
        }
    }
}

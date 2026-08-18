using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheLibrary.Models;

namespace TheLibrary.Services
{
    public static class ConfigService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TheLibrary.v1.entropy");

        public static string Folder
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TheLibrary");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ConfigPath => Path.Combine(Folder, "config.json");

        public static AppConfig Current { get; private set; } = new AppConfig();

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    Current = cfg ?? new AppConfig();
                }
                else
                {
                    Current = new AppConfig();
                }
            }
            catch
            {
                Current = new AppConfig();
            }
            return Current;
        }

        public static void Save(AppConfig cfg)
        {
            Current = cfg;
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, opts), Encoding.UTF8);
        }

        public static void Save() => Save(Current);

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                // fallback: base64 simples (não é criptografia, mas evita texto puro no arquivo)
                return "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("b64:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(4)));

                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }

        public static string GetConnectionString() => Unprotect(Current.ConnectionProtected);

        public static void SetConnectionString(string cs) => Current.ConnectionProtected = Protect(cs);
    }
}

using System;
using System.Globalization;
using System.Security.Cryptography;

namespace TheLibrary.Services
{

    public static class PasswordHasher
    {
        private const int Iterations = 120000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static string Hash(string password)
        {
            if (password == null) password = "";
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return string.Format(CultureInfo.InvariantCulture, "pbkdf2${0}${1}${2}",
                Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        }

        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;
            try
            {
                var parts = stored.Split('$');
                if (parts.Length != 4 || parts[0] != "pbkdf2") return false;

                int iter = int.Parse(parts[1], CultureInfo.InvariantCulture);
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, iter, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        public static string Validate(string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return "Informe uma senha.";
            if (password.Length < 6) return "A senha precisa ter ao menos 6 caracteres.";
            return null;
        }
    }
}

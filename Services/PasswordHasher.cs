using System.Security.Cryptography;
using System.Text;

namespace sudokuvip.Services;

public static class PasswordHasher
{
    public const int Iterations = 600000;
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string stored, out bool needsUpgrade)
    {
        needsUpgrade = false;
        try
        {
            if (stored.Length == 64 && stored.All(Uri.IsHexDigit))
            {
                bool ok = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)),Convert.FromHexString(stored));
                needsUpgrade = ok; return ok;
            }
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1],out int iterations) || iterations < 10000 || iterations > 2000000) return false;
            byte[] salt = Convert.FromBase64String(parts[2]), hash = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || hash.Length != 32) return false;
            bool valid = CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password,salt,iterations,HashAlgorithmName.SHA256,32),hash);
            needsUpgrade = valid && iterations < Iterations;
            return valid;
        }
        catch (FormatException) { return false; }
    }
}

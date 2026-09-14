using System.Security.Cryptography;

namespace AyazTekServis.Services;

public class PasswordService
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(value, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string? value, string? stored)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(stored)) return false;
        if (!stored.StartsWith("PBKDF2$", StringComparison.Ordinal))
            return CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(value),
                System.Text.Encoding.UTF8.GetBytes(stored));

        var parts = stored.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public bool IsHashed(string? value) => !string.IsNullOrEmpty(value) && value.StartsWith("PBKDF2$", StringComparison.Ordinal);

    public bool IsStrongEnough(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length >= 6 &&
        value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit) &&
        value.Any(c => !char.IsLetterOrDigit(c));
}

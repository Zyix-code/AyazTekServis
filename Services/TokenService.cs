using System.Security.Cryptography;
using System.Text;

namespace AyazTekServis.Services;

public class TokenService
{
    public string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

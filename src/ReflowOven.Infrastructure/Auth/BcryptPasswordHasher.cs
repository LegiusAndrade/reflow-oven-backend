using BCryptNet = BCrypt.Net.BCrypt;

namespace ReflowOven.Infrastructure.Auth;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password) => BCryptNet.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return !string.IsNullOrEmpty(hash) && BCryptNet.Verify(password, hash);
        }
        catch
        {
            return false; // malformed hash
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ReflowOven.Infrastructure.Auth;

public sealed class TechnicianOptions
{
    public const string Section = "Technician";

    /// <summary>Frontend default: calibracao / calibra. Override (and hash) in production.</summary>
    public string Username { get; set; } = "calibracao";
    public string Password { get; set; } = "calibra";
}

public sealed class TechnicianCredentials(IOptions<TechnicianOptions> options) : ITechnicianCredentials
{
    public string Username => options.Value.Username;

    /// <summary>Constant-time password check. We compare SHA-256 digests with
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> so neither the timing nor an early-out on
    /// length leaks anything about the configured secret (the raw values are never compared directly).</summary>
    public bool Verify(string password)
    {
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Password ?? ""));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? ""));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

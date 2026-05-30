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

    public bool Verify(string password) => string.Equals(password, options.Value.Password, StringComparison.Ordinal);
}

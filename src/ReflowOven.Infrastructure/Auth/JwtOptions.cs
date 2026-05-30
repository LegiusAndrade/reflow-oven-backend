namespace ReflowOven.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "reflow-oven";
    public string Audience { get; set; } = "reflow-oven-ui";

    /// <summary>HMAC-SHA256 signing key (≥ 32 chars). Set via configuration/secrets in production.</summary>
    public string SigningKey { get; set; } = "dev-only-change-me-please-32-bytes-minimum!";

    public int ExpiryMinutes { get; set; } = 480;
}

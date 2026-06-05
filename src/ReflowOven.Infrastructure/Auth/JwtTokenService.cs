using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ReflowOven.Infrastructure.Auth;

/// <summary>Mints HS256 JWTs whose claims reflect the frontend Session (sub, name, role, iat, calibration).</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : IJwtTokenService
{
    /// <summary>Custom claim present only for the hidden technician session.</summary>
    public const string CalibrationClaim = "calibration";

    /// <summary>Custom claim: the user is still on the system-issued password and should change it.</summary>
    public const string MustChangeClaim = "must_change_password";

    public TokenResult CreateForUser(User user) =>
        Create(user.Id.ToString(), user.Name, user.Type.ToString(), calibration: false, mustChangePassword: user.MustChangePassword);

    public TokenResult CreateForCalibration() =>
        Create("calibration", "Calibração", nameof(UserType.Tecnico), calibration: true, mustChangePassword: false);

    private TokenResult Create(string subject, string name, string role, bool calibration, bool mustChangePassword)
    {
        var o = options.Value;
        var now = clock.UtcNow;
        var expires = now.AddMinutes(o.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        if (calibration)
            claims.Add(new Claim(CalibrationClaim, "true"));
        if (mustChangePassword)
            claims.Add(new Claim(MustChangeClaim, "true"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, now.UtcDateTime, expires.UtcDateTime, creds);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new TokenResult(jwt, expires, now.ToUnixTimeMilliseconds());
    }
}

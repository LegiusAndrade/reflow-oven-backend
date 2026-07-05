using System.Security.Claims;

namespace ReflowOven.Api.Auth;

/// <summary>
/// Shared revocation decision for a validated principal — used by both the JWT bearer
/// <c>OnTokenValidated</c> event (REST + hub handshake) and the SignalR <c>RevocationHubFilter</c>
/// (per-invocation re-check), so the seam is identical everywhere. Reads the RAW <c>sub</c>/<c>iat</c>
/// claims (JwtBearer runs with <c>MapInboundClaims = false</c>, so they are not remapped to the long
/// XML claim URIs). Only real users carry a Guid <c>sub</c>; the config-backed technician session has no
/// row to revoke and is never rejected here. A missing/garbled <c>iat</c> is treated as epoch, so any
/// revocation mark rejects such a token (fail-safe).
/// </summary>
public static class TokenRevocationCheck
{
    public static bool IsRevoked(ClaimsPrincipal? principal, ITokenRevocationList revocations)
    {
        if (!Guid.TryParse(principal?.FindFirst("sub")?.Value, out var userId))
            return false; // no revocable subject (technician session / malformed) → not our concern

        var issuedAt = long.TryParse(principal?.FindFirst("iat")?.Value, out var iat)
            ? DateTimeOffset.FromUnixTimeSeconds(iat)
            : DateTimeOffset.UnixEpoch;

        return revocations.IsRevoked(userId, issuedAt);
    }
}

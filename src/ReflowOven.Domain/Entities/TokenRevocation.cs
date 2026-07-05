namespace ReflowOven.Domain.Entities;

/// <summary>
/// Durable JWT-revocation watermark: the instant up to which a subject's tokens are no longer accepted.
/// One row per user (<see cref="Subject"/> = the user id) plus a single global row keyed on
/// <see cref="Guid.Empty"/> for a "revoke everyone" event (factory reset / bulk user cleanup). The
/// in-memory <c>ITokenRevocationList</c> is the hot-path reader; this table is its backing store so a
/// process restart (the appliance runs <c>Restart=always</c>) re-hydrates the marks instead of silently
/// un-revoking every user. Times are stored floored to the whole second (JWT <c>iat</c> resolution).
/// </summary>
public class TokenRevocation
{
    /// <summary>The revoked user's id; <see cref="Guid.Empty"/> is the sentinel for the global mark
    /// (<c>Guid.NewGuid</c> never yields Empty, so it can never collide with a real user).</summary>
    public Guid Subject { get; set; }

    /// <summary>Tokens for this subject with an <c>iat</c> before this instant are rejected.</summary>
    public DateTimeOffset RevokedAt { get; set; }
}

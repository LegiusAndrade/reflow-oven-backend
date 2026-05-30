namespace ReflowOven.Domain.Entities;

/// <summary>
/// An application account. The frontend used a string id (slug or epoch); the backend uses a
/// Guid that is serialized as a string to keep the JSON contract. Users are HARD-deleted
/// (the frontend has no user soft-delete). The hidden "calibracao" technician is NOT a row here.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Login name — unique (case-insensitive), 3..40 chars, immutable after creation.</summary>
    public string Name { get; set; } = "";

    public string Email { get; set; } = "";

    /// <summary>BCrypt hash. Net-new vs. the frontend, where every mock user shared the password "1234".</summary>
    public string PasswordHash { get; set; } = "";

    public UserType Type { get; set; } = UserType.Regular;
    public UserStatus Status { get; set; } = UserStatus.Ativo;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null = never logged in (rendered as "—" on the client).</summary>
    public DateTimeOffset? LastLogin { get; set; }

    /// <summary>Lifetime successful-login count (feeds the Diagnóstico "top users by logins" ranking).</summary>
    public long LoginCount { get; set; }

    /// <summary>Per-action activity counters shown in the user-detail modal (the 7 pt-BR labels).</summary>
    public ICollection<UserActivityStat> ActivityStats { get; set; } = new List<UserActivityStat>();
}

/// <summary>
/// Aggregate activity counter for a user, keyed by a pt-BR action label (replaces the mock
/// <c>User.events[]</c>). Incremented by the AuditService on the matching mutation.
/// </summary>
public class UserActivityStat
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>e.g. "programas criados", "usuários alterados", "configurações alteradas".</summary>
    public string Label { get; set; } = "";

    public int Count { get; set; }
}

/// <summary>Single-use, enumeration-safe password reset token (email recovery — sender stubbed).</summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

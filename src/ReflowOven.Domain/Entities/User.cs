namespace ReflowOven.Domain.Entities;

/// <summary>
/// An application account. The frontend used a string id (slug or epoch); the backend uses a
/// Guid that is serialized as a string to keep the JSON contract. Users are SOFT-deleted
/// (<see cref="IsDeleted"/> + a global query filter): a deleted user is hidden everywhere and only
/// the dev Master can view/restore/purge it. The hidden "calibracao" technician is NOT a row here.
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

    /// <summary>True from creation until the user changes the password that was emailed to them.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>When the current system-issued password was set — anchors the change deadline.</summary>
    public DateTimeOffset? PasswordIssuedAt { get; set; }

    /// <summary>When the user last set their own password (null = still on the issued one).</summary>
    public DateTimeOffset? PasswordChangedAt { get; set; }

    /// <summary>Last "please change your password" reminder we emailed (throttle: at most once a day).</summary>
    public DateTimeOffset? LastPasswordReminderAt { get; set; }

    /// <summary>Soft-delete tombstone. A deleted user is hidden by the global query filter (grid, login,
    /// rankings, …); only the dev Master can list/restore/purge it. The name stays reserved while deleted.</summary>
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>Name of whoever deleted it (shown in the Master's trash view); null while live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>Per-action activity counters shown in the user-detail modal (the 7 pt-BR labels).</summary>
    public ICollection<UserActivityStat> ActivityStats { get; set; } = new List<UserActivityStat>();

    /// <summary>Per-user UI preferences (theme + execution-chart series). Owned/jsonb; follows the user across logins.</summary>
    public UserPreferences Preferences { get; set; } = new();
}

/// <summary>
/// A user's personal UI settings (replaces the front's per-session theme + the global chart-series
/// config, so they follow the user across PCs/logins). Owned by <see cref="User"/>, stored as jsonb.
/// The chart-series flags mirror <see cref="RunSeriesPreference"/> (the global defaults).
/// </summary>
public class UserPreferences
{
    public Theme Theme { get; set; } = Theme.System;

    public bool Alvo { get; set; } = true;
    public bool Oven { get; set; } = true;
    public bool Board { get; set; }
    public bool Current { get; set; } = true;
    public bool Voltage { get; set; } = true;
    public bool OvenFan { get; set; }
    public bool BoardFan { get; set; }
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

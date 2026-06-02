namespace ReflowOven.Domain.Entities;

/// <summary>
/// A reflow temperature profile the operator can select and run (frontend <c>Program</c> /
/// "programa"). Named <c>ReflowProgram</c> to avoid clashing with the ASP.NET entry-point
/// <c>Program</c> class. Seed catalog programs live in the same table and are soft-deleted
/// (<see cref="IsDeleted"/>) rather than removed, replacing the localStorage tombstone list.
/// </summary>
public class ReflowProgram
{
    /// <summary>Slug for seeds ("smd-270", "gen-1") or a Guid string for user-created programs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Optional; empty is normalized to null on save.</summary>
    public string? Description { get; set; }

    public int RunCount { get; set; }

    /// <summary>Null = never run (rendered "Nunca" on the client).</summary>
    public DateTimeOffset? LastUsed { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>Name of whoever deleted it (shown in the Master's trash view); null while live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>True for built-in catalog programs (hidden, not removed, on delete/factory-reset).</summary>
    public bool IsSeed { get; set; }

    /// <summary>The sampled setpoint curve (always starts at t=0, temp=25). Stored as jsonb.</summary>
    public List<ProfilePoint> Profile { get; set; } = new();

    /// <summary>The editable legs the curve was built from; absent on seed programs. Stored as jsonb.</summary>
    public List<ProfileSegment>? Segments { get; set; }
}

/// <summary>A single setpoint on a profile curve (seconds × °C). Owned/jsonb — never queried per-point.</summary>
public class ProfilePoint
{
    /// <summary>Seconds since run start. Fractional after parabola interpolation, so double.</summary>
    public double T { get; set; }

    /// <summary>Target temperature (°C); fractional after interpolation.</summary>
    public double Temp { get; set; }
}

/// <summary>One editable leg of a profile (the editor's source of truth). Owned/jsonb.</summary>
public class ProfileSegment
{
    public int Temp { get; set; }
    public int DurationSec { get; set; }
    public RampShape Ramp { get; set; } = RampShape.Linear;
}

/// <summary>Per-user favorite (replaces the flat localStorage set). Works for seed and user programs.</summary>
public class FavoriteProgram
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ProgramId { get; set; } = "";
    public ReflowProgram? Program { get; set; }
}

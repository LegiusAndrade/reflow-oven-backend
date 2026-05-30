namespace ReflowOven.Domain.Entities;

/// <summary>
/// An audit record for the Relatórios → Alterações tab. Emitted server-side on every program
/// create/edit/delete and on Settings saves. A config change carries <see cref="ConfigBullets"/>;
/// a program change carries the point diff in <see cref="Points"/>.
/// </summary>
public class ChangeLogEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public ChangeAction Action { get; set; }

    /// <summary>Program name, or "Configuração do sistema" for config changes.</summary>
    public string Target { get; set; } = "";

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ProgramId { get; set; }

    public ChangeDetailKind DetailKind { get; set; }

    /// <summary>Config change: human-readable bullets (e.g. "Tensão máxima: 110V → 200V"). jsonb, null for program changes.</summary>
    public List<string>? ConfigBullets { get; set; }

    /// <summary>Program change: the before/after/added/removed point diff. Stored as jsonb.</summary>
    public List<ChangePointRow> Points { get; set; } = new();
}

/// <summary>One row of a program change-diff. Owned/jsonb.</summary>
public class ChangePointRow
{
    /// <summary>1-based point index.</summary>
    public int Index { get; set; }
    public int Temp { get; set; }
    public int TimeSec { get; set; }
    public RampShape Ramp { get; set; }
    public ChangePointRole Role { get; set; }
}

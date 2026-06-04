using System.Globalization;

namespace ReflowOven.Domain.Entities;

/// <summary>
/// One row of the universal operation log ("Log de Operação") — an append-only audit trail that records
/// every notable action across the device: program/config changes, executions, board faults, RS422 /
/// central-server communication, logins, calibration and maintenance. Mirrors the operator-facing columns
/// DATA · OPERADOR · TIPO · OBJETO · OBJETO ID · DADOS. It is protected from the Manutenção cleanup and has
/// its own retention. <b>Never stores a password value</b> (a password change is logged as "senha alterada").
/// </summary>
public class OperationLogEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }

    /// <summary>The acting user; null for automatic events (then <see cref="OperatorName"/> is "Sistema").</summary>
    public Guid? OperatorId { get; set; }
    public string OperatorName { get; set; } = "";

    /// <summary>Coarse bucket for the front's sub-tabs; derived from (Type, Object) when the row is written.</summary>
    public OperationCategory Category { get; set; }
    public OperationType Type { get; set; }
    public OperationObject Object { get; set; }

    /// <summary>Identifier of the target (program id, run id, username…); null when not applicable.</summary>
    public string? ObjectId { get; set; }

    /// <summary>The "DADOS" payload — a flat field list. Stored as jsonb.</summary>
    public List<OperationField> Data { get; set; } = [];
}

/// <summary>
/// One field of an <see cref="OperationLogEntry"/> payload. <see cref="Before"/> is null for events with no
/// prior state (an execution, a login, a communication tick). Values are display strings (a scalar, or a
/// small JSON blob) — never a raw password.
/// </summary>
public class OperationField
{
    public string Field { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }

    /// <summary>A field that only carries a new value (no prior state).</summary>
    public static OperationField Of(string field, object? after) => new() { Field = field, After = Str(after) };

    /// <summary>A field that changed from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static OperationField Change(string field, object? before, object? after) =>
        new() { Field = field, Before = Str(before), After = Str(after) };

    // Invariant formatting so a double offset/temperature never depends on the server's culture.
    private static string? Str(object? v) => v switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };
}

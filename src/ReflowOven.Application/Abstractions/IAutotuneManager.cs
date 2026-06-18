namespace ReflowOven.Application.Abstractions;

/// <summary>
/// Owns the single active PID relay auto-tune (singleton). The API drives Start/Cancel/GetStatus; the
/// background loop drives <see cref="TickAsync"/> ~1×/s to poll the board and, on a terminal state, persist
/// the <c>AutotuneRun</c>. Mutually exclusive with a run — the firmware refuses to start one while the other
/// is active. Implemented as a singleton in Infrastructure (mirrors <see cref="IRunManager"/>).
/// </summary>
public interface IAutotuneManager
{
    /// <summary>True while a tune is active (the relay experiment is still running).</summary>
    bool IsActive { get; }

    /// <summary>Live status of the active tune, or null when idle.</summary>
    AutotuneStatusDto? GetStatus();

    /// <summary>Start a relay auto-tune oscillating around <paramref name="targetTemp"/> °C.</summary>
    Task<AutotuneStatusDto> StartAsync(double targetTemp, Guid? userId, string? userName, CancellationToken ct = default);

    /// <summary>Cancel the active tune; returns the (now cancelled) status, or null when idle.</summary>
    Task<AutotuneStatusDto?> CancelAsync(CancellationToken ct = default);

    /// <summary>Poll the active tune one tick (no-op when idle). Called by the control loop.</summary>
    Task TickAsync(CancellationToken ct = default);
}

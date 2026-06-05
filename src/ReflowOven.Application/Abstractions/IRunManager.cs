namespace ReflowOven.Application.Abstractions;

/// <summary>
/// Owns the single active run. The API calls Start/Stop/GetStatus; the background control loop
/// calls <see cref="TickAsync"/> ~1×/s to read the board, push telemetry and (on a terminal
/// state) persist the ExecutionReport. Implemented as a singleton in Infrastructure.
/// </summary>
public interface IRunManager
{
    /// <summary>Status of the active run, or null when idle.</summary>
    RunStatusDto? GetStatus();

    /// <summary>True while a run is active and still running — a cheap state check for the control loop,
    /// so it doesn't build a full <see cref="RunStatusDto"/> just to read the status each tick.</summary>
    bool IsRunning { get; }

    /// <summary>Start a run for <paramref name="programId"/> on behalf of the given user (null = technician/anonymous).</summary>
    Task<RunStatusDto> StartAsync(string programId, Guid? userId, string? userName, CancellationToken ct = default);

    /// <summary>Request the active run to stop; the loop finalizes it as aborted.</summary>
    Task<RunStatusDto?> StopAsync(CancellationToken ct = default);

    /// <summary>Advance the active run one tick (no-op when idle). Called by the control loop.</summary>
    Task TickAsync(CancellationToken ct = default);
}

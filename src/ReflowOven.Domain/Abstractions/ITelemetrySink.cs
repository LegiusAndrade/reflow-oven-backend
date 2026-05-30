namespace ReflowOven.Domain.Abstractions;

/// <summary>
/// Decouples the hardware/run loop from SignalR: the loop publishes here and the API layer's
/// implementation fans the payloads out to the RunTelemetry / Diagnostics hubs. Keeping it in
/// Domain means the simulated and real boards feed identical telemetry without referencing the API.
/// </summary>
public interface ITelemetrySink
{
    /// <summary>Live execution sample for a specific run (RunTelemetryHub group).</summary>
    Task PublishTraceAsync(Guid runId, TraceSample sample);

    /// <summary>Server-classified phase change for a run.</summary>
    Task PublishPhaseAsync(Guid runId, RunPhase phase);

    /// <summary>Run lifecycle status change.</summary>
    Task PublishStatusAsync(Guid runId, RunStatus status);

    /// <summary>Run finalized — carries the persisted execution report id.</summary>
    Task PublishCompletedAsync(Guid runId, Guid executionId);

    /// <summary>Standalone sensor tick for the Diagnóstico screen (no active run required).</summary>
    Task PublishReadingAsync(SensorReadings readings);
}

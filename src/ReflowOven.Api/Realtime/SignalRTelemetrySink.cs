using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>
/// Bridges the hardware/run loop to SignalR. Caches the last sample per run so a late subscriber
/// can be primed with the current state. Singleton (depends only on the hub contexts).
/// </summary>
public sealed class SignalRTelemetrySink(
    IHubContext<RunTelemetryHub> runHub,
    IHubContext<DiagnosticsHub> diagnosticsHub) : ITelemetrySink
{
    private readonly ConcurrentDictionary<Guid, TraceSampleDto> _lastSample = new();

    public Task PublishTraceAsync(Guid runId, TraceSample sample)
    {
        var dto = TraceSampleDto.From(sample);
        _lastSample[runId] = dto;
        return runHub.Clients.Group(RunTelemetryHub.GroupName(runId)).SendAsync("TraceSample", runId, dto);
    }

    public Task PublishPhaseAsync(Guid runId, RunPhase phase) =>
        runHub.Clients.Group(RunTelemetryHub.GroupName(runId)).SendAsync("RunPhaseChanged", runId, phase);

    public Task PublishStatusAsync(Guid runId, RunStatus status) =>
        runHub.Clients.Group(RunTelemetryHub.GroupName(runId)).SendAsync("RunStatusChanged", runId, status);

    public Task PublishCompletedAsync(Guid runId, Guid executionId)
    {
        _lastSample.TryRemove(runId, out _);
        return runHub.Clients.Group(RunTelemetryHub.GroupName(runId)).SendAsync("RunCompleted", runId, executionId);
    }

    public Task PublishReadingAsync(SensorReadings readings) =>
        diagnosticsHub.Clients.All.SendAsync("ReadingTick", SensorReadingsDto.From(readings));

    /// <summary>The most recent sample for a run, if any (to prime late subscribers).</summary>
    public TraceSampleDto? LastSample(Guid runId) => _lastSample.TryGetValue(runId, out var s) ? s : null;
}

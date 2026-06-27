using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Domain.Hardware;
using ReflowOven.Infrastructure.Persistence;
using ReflowOven.Infrastructure.Run;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Regression for the bug where a power-board protection fault during a run (over-temp E-101, over-current
/// E-110, …) was never observed: <see cref="RunManager"/> did not subscribe to <see cref="IPowerBoard.FaultRaised"/>,
/// so a hardware fault — which leaves the RS422 link UP (IsConnected stays true) — let the run finish as
/// "Concluído" with no ErrorLogEntry. The fix subscribes and finalizes the active run as a Falha on the next
/// gate-serialized tick, with the E-code/severity from the seeded fault catalog. Runs against EF Core InMemory.
/// </summary>
public sealed class RunManagerFaultTests
{
    [Fact]
    public async Task BoardFault_during_run_finalizes_as_Falha_with_catalogued_code_and_error()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 6, 27, 8, 0, 0, TimeSpan.Zero));
        var sp = BuildProvider(clock);
        const string programId = "p-fault-test";
        await SeedProgramAsync(sp, programId);

        var board = new FakeBoard();
        var manager = NewManager(sp, board, clock);

        await manager.StartAsync(programId, userId: null, userName: null);
        Assert.True(manager.IsRunning);

        // A real protection fault keeps the link up (IsConnected stays true), so only the FaultRaised event
        // can interrupt the run. It arrives on the RX thread; the manager finalizes it on the next tick.
        board.RaiseFault("E-101");
        await manager.TickAsync();

        Assert.False(manager.IsRunning);
        Assert.Null(manager.GetStatus());

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();

        var report = Assert.Single(await db.Executions.ToListAsync());
        Assert.Equal(ExecutionStatus.Falha, report.Status);
        Assert.Equal("E-101", report.FaultTypeCode);
        Assert.NotNull(report.LinkedErrorId);

        var error = Assert.Single(await db.Errors.ToListAsync());
        Assert.Equal("E-101", error.FaultTypeCode);
        Assert.Equal(ErrorSeverity.Critico, error.Severity); // E-101 is Crítico in the seeded catalog
        Assert.Equal(report.LinkedErrorId, error.Id);
    }

    [Fact]
    public async Task Run_without_fault_finalizes_as_Concluido()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 6, 27, 8, 0, 0, TimeSpan.Zero));
        var sp = BuildProvider(clock);
        const string programId = "p-clean-test";
        await SeedProgramAsync(sp, programId);

        var board = new FakeBoard();
        var manager = NewManager(sp, board, clock);

        await manager.StartAsync(programId, userId: null, userName: null);
        // Advance past the total run time so the next tick completes the run normally.
        clock.Advance(TimeSpan.FromSeconds(120));
        await manager.TickAsync();

        Assert.False(manager.IsRunning);

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        var report = Assert.Single(await db.Executions.ToListAsync());
        Assert.Equal(ExecutionStatus.Concluido, report.Status);
        Assert.Null(report.FaultTypeCode);
        Assert.Empty(await db.Errors.ToListAsync());
    }

    [Fact]
    public async Task BoardFault_attaches_the_downloaded_fault_snapshot_to_the_error_and_detail_dto()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 6, 27, 8, 0, 0, TimeSpan.Zero));
        var sp = BuildProvider(clock);
        const string programId = "p-snap-test";
        await SeedProgramAsync(sp, programId);

        var board = new FakeBoard
        {
            Snapshot = new FaultSnapshot(SampleIntervalMs: 10, TriggerIndex: 1, FaultCode: 0x0102, BaseMs: 500,
            [
                new FaultSnapshotSample(25, 30, 0, 12, 20, 0, 1000, 1000, 800, 0, 0, 0, 40, 3.3, 0, 25, 0, 0),
                new FaultSnapshotSample(260, 70, 160, 12, 20, 38, 5000, 5000, 4000, 100, 95, 90, 50, 3.3, 1 << 2, 230, 88, 6080),
                new FaultSnapshotSample(255, 69, 158, 12, 20, 35, 5000, 5000, 4000, 100, 95, 90, 50, 3.3, 1 << 2, 230, 86, 5530),
            ]),
        };
        var manager = NewManager(sp, board, clock);

        await manager.StartAsync(programId, userId: null, userName: null);
        board.RaiseFault("E-101");
        await manager.TickAsync();

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        var error = Assert.Single(await db.Errors.ToListAsync());
        Assert.NotNull(error.BoardSnapshot);
        Assert.Equal(10, error.BoardSnapshot!.SampleIntervalMs);
        Assert.Equal(1, error.BoardSnapshot.TriggerIndex);
        Assert.Equal(0x0102, error.BoardSnapshot.FaultCode);
        Assert.Equal(3, error.BoardSnapshot.Samples.Count);
        Assert.Equal(160, error.BoardSnapshot.Samples[1].VbusV); // engineering units persisted verbatim

        // GET /api/errors/{id} surfaces it on the detail DTO.
        var dto = await new ReportService(db).ErrorAsync(error.Id);
        Assert.NotNull(dto.BoardSnapshot);
        Assert.Equal(1, dto.BoardSnapshot!.TriggerIndex);
        Assert.Equal(3, dto.BoardSnapshot.Samples.Count);
        Assert.Equal(38, dto.BoardSnapshot.Samples[1].CurrentA);
    }

    [Fact]
    public async Task BoardFault_is_recorded_even_when_the_snapshot_download_fails()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 6, 27, 8, 0, 0, TimeSpan.Zero));
        var sp = BuildProvider(clock);
        const string programId = "p-snap-fail";
        await SeedProgramAsync(sp, programId);

        // The board throws on the snapshot download (a dead link / no snapshot): the Falha must still record.
        var board = new FakeBoard { SnapshotThrows = new TimeoutException("RS422 sem resposta") };
        var manager = NewManager(sp, board, clock);

        await manager.StartAsync(programId, userId: null, userName: null);
        board.RaiseFault("E-110");
        await manager.TickAsync();

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        var error = Assert.Single(await db.Errors.ToListAsync());
        Assert.Equal("E-110", error.FaultTypeCode);
        Assert.Null(error.BoardSnapshot); // best-effort: no snapshot, but the error is intact
    }

    private static RunManager NewManager(IServiceProvider sp, IPowerBoard board, IClock clock) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(), board,
            new NullTelemetrySink(), new NullSystemLogSink(), new NullNotificationSink(), clock,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RunManager>>());

    private static ServiceProvider BuildProvider(IClock clock)
    {
        var dbName = $"run-fault-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReflowDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAppDbContext>(s => s.GetRequiredService<ReflowDbContext>());
        services.AddSingleton(clock);
        services.AddSingleton<ICurrentUser>(new SystemCurrentUser());
        services.AddScoped<AuditService>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedProgramAsync(IServiceProvider sp, string programId)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        db.Programs.Add(new ReflowProgram
        {
            Id = programId,
            Name = "Teste",
            CreatedAt = new DateTimeOffset(2026, 6, 27, 8, 0, 0, TimeSpan.Zero),
            Profile = [new ProfilePoint { T = 0, Temp = 25 }, new ProfilePoint { T = 60, Temp = 100 }],
        });
        await db.SaveChangesAsync();
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    private sealed class SystemCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? Id => null;
        public Guid? UserId => null;
        public string? Name => null;
        public UserType? Role => null;
        public bool IsCalibration => false;
    }

    /// <summary>A board whose link is always up; <see cref="RaiseFault"/> fires the protection-fault event.</summary>
    private sealed class FakeBoard : IPowerBoard
    {
        public event EventHandler<FaultRaised>? FaultRaised;
        public event EventHandler? ConfigRequested { add { } remove { } }
        public bool IsConnected => true;

        /// <summary>The fault snapshot the board returns from <see cref="GetFaultSnapshotAsync"/>. Default null
        /// (no snapshot); a test can set one to exercise the attach-to-error path, or a throwing one for best-effort.</summary>
        public FaultSnapshot? Snapshot { get; set; }
        public Exception? SnapshotThrows { get; set; }

        public void RaiseFault(string code) => FaultRaised?.Invoke(this, new FaultRaised(code, DateTimeOffset.UtcNow));

        public Task<FaultSnapshot?> GetFaultSnapshotAsync(CancellationToken ct = default) =>
            SnapshotThrows is { } ex ? Task.FromException<FaultSnapshot?>(ex) : Task.FromResult(Snapshot);

        public Task<SensorReadings> ReadAsync(CancellationToken ct = default) => Task.FromResult(default(SensorReadings));
        public Task StartProgramAsync(IReadOnlyList<ProfileSegment> segments, IReadOnlyList<ProfilePoint> profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task<RunReadback?> GetRunStatusAsync(CancellationToken ct = default) => Task.FromResult<RunReadback?>(null);
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AcknowledgeFaultAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyCalibrationAsync(Calibration calibration, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyControlConfigAsync(Settings settings, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SelfTestResult> RunSelfTestAsync(SelfTestId id, CancellationToken ct = default) => Task.FromResult(new SelfTestResult(id, SelfTestState.Idle));
        public Task<OutputCalStep> DriveOutputAsync(double setVoltage, CancellationToken ct = default) => Task.FromResult(new OutputCalStep(setVoltage, setVoltage));
        public Task<BoardIdentity> GetIdentityAsync(CancellationToken ct = default) => Task.FromResult(new BoardIdentity("—", "—", 0, "—", "—", 0));
        public Task<AutoTuneReadback> AutoTuneAsync(AutoTuneOp op, double? targetC = null, CancellationToken ct = default) => Task.FromResult(new AutoTuneReadback(AutoTuneState.Idle, 0, 0, 0, 0, 0, 0));
    }

    private sealed class NullTelemetrySink : ITelemetrySink
    {
        public Task PublishTraceAsync(Guid runId, TraceSample sample) => Task.CompletedTask;
        public Task PublishPhaseAsync(Guid runId, RunPhase phase) => Task.CompletedTask;
        public Task PublishStatusAsync(Guid runId, RunStatus status) => Task.CompletedTask;
        public Task PublishCompletedAsync(Guid runId, Guid executionId) => Task.CompletedTask;
        public Task PublishReadingAsync(SensorReadings readings) => Task.CompletedTask;
    }

    private sealed class NullSystemLogSink : ISystemLogSink
    {
        public Task PublishAsync(ReflowOven.Application.Dtos.SystemLogDto entry) => Task.CompletedTask;
    }

    private sealed class NullNotificationSink : INotificationSink
    {
        public Task PublishAsync(ReflowOven.Application.Dtos.NotificationDto entry) => Task.CompletedTask;
    }
}

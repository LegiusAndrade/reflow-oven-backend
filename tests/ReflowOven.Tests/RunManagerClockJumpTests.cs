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
/// Regression for the wall-clock hazard on run timing: elapsed/duration used to come from
/// <c>clock.UtcNow - StartedAt</c>, so a step of the system clock mid-run — the boot-time restore from
/// the PT7C4339 RTC, an NTP correction, a manual time set — could finalize a live burn early or stretch
/// it. The RunManager now measures elapsed on the monotonic side of <see cref="IClock"/>
/// (GetTimestamp/GetElapsedTime), keeping wall time only for the stored timestamps.
/// </summary>
public sealed class RunManagerClockJumpTests
{
    [Fact]
    public async Task Forward_wall_clock_jump_does_not_finish_the_run()
    {
        var clock = new JumpClock();
        var sp = BuildProvider(clock);
        const string programId = "p-jump-fwd";
        await SeedProgramAsync(sp, programId); // 60 s profile

        var manager = NewManager(sp, new FakeBoard(), clock);
        await manager.StartAsync(programId, userId: null, userName: null);

        // NTP/RTC steps the wall clock 5 days ahead one second into the run; monotonic time barely moved.
        clock.AdvanceReal(TimeSpan.FromSeconds(1));
        clock.JumpWall(TimeSpan.FromDays(5));
        await manager.TickAsync();

        Assert.True(manager.IsRunning); // the burn keeps running — 1 s elapsed, not 5 days
        var status = manager.GetStatus();
        Assert.NotNull(status);
        Assert.InRange(status!.ElapsedSeconds, 0, 5);

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        Assert.Empty(await db.Executions.ToListAsync()); // nothing was finalized
    }

    [Fact]
    public async Task Backward_wall_clock_jump_does_not_stretch_the_run_and_duration_stays_sane()
    {
        var clock = new JumpClock();
        var sp = BuildProvider(clock);
        const string programId = "p-jump-back";
        await SeedProgramAsync(sp, programId); // 60 s profile

        var manager = NewManager(sp, new FakeBoard(), clock);
        await manager.StartAsync(programId, userId: null, userName: null);

        // The clock is stepped 3 days BACK mid-run (e.g. a bad manual set); real time then passes 120 s.
        clock.JumpWall(TimeSpan.FromDays(-3));
        clock.AdvanceReal(TimeSpan.FromSeconds(120));
        await manager.TickAsync();

        Assert.False(manager.IsRunning); // 120 real seconds > the 60 s profile → completes normally

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        var report = Assert.Single(await db.Executions.ToListAsync());
        Assert.Equal(ExecutionStatus.Concluido, report.Status);
        Assert.InRange(report.DurationSeconds, 119, 121); // real elapsed — not negative, not days

        // D5: the stored end (CreatedAt) is StartedAt + monotonic duration, so it never precedes the start
        // even though the wall clock was stepped 3 days back mid-run.
        Assert.True(report.CreatedAt >= report.StartedAt);
        Assert.Equal(report.StartedAt.AddSeconds(report.DurationSeconds), report.CreatedAt);
    }

    [Fact]
    public async Task Backward_jump_with_a_fault_keeps_the_error_StartAt_before_or_equal_EndAt()
    {
        var clock = new JumpClock();
        var sp = BuildProvider(clock);
        const string programId = "p-jump-fault";
        await SeedProgramAsync(sp, programId);

        var board = new FakeBoard();
        var manager = NewManager(sp, board, clock);
        await manager.StartAsync(programId, userId: null, userName: null);

        // A backward step, then the RS422 link drops → the tick finalizes as an E-130 fault, which writes an
        // ErrorLogEntry with StartAt/EndAt. Both must stay ordered (EndAt derived from StartedAt + duration).
        clock.JumpWall(TimeSpan.FromDays(-2));
        clock.AdvanceReal(TimeSpan.FromSeconds(5));
        board.Connected = false;
        await manager.TickAsync();

        Assert.False(manager.IsRunning);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
        var report = Assert.Single(await db.Executions.ToListAsync());
        Assert.Equal(ExecutionStatus.Falha, report.Status);
        Assert.True(report.CreatedAt >= report.StartedAt);

        var error = Assert.Single(await db.Errors.ToListAsync());
        Assert.Equal("E-130", error.FaultTypeCode);
        Assert.True(error.StartAt <= error.EndAt); // D5: never a negative interval on the persisted fault
    }

    // ---- helpers ---------------------------------------------------------

    private static RunManager NewManager(IServiceProvider sp, IPowerBoard board, IClock clock) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(), board,
            new NullTelemetrySink(), new NullSystemLogSink(), new NullNotificationSink(), clock,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RunManager>>());

    private static ServiceProvider BuildProvider(IClock clock)
    {
        // Hoisted: the options action runs per scope (options are scoped by default), so an inline
        // Guid.NewGuid() would hand every scope a DIFFERENT in-memory database.
        var dbName = $"run-jump-{Guid.NewGuid():N}";
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
            CreatedAt = new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero),
            Profile = [new ProfilePoint { T = 0, Temp = 25 }, new ProfilePoint { T = 60, Temp = 100 }],
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Wall clock and monotonic clock advance independently: <see cref="AdvanceReal"/> moves
    /// both (real time passing); <see cref="JumpWall"/> steps only the wall clock (an NTP/RTC/manual set).</summary>
    private sealed class JumpClock : IClock
    {
        private DateTimeOffset _wall = new(2026, 7, 3, 8, 0, 0, TimeSpan.Zero);
        private long _monoTicks;

        public DateTimeOffset UtcNow => _wall;
        public long GetTimestamp() => _monoTicks;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromTicks(_monoTicks - startTimestamp);

        public void AdvanceReal(TimeSpan by) { _monoTicks += by.Ticks; _wall += by; }
        public void JumpWall(TimeSpan by) => _wall += by;
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

    /// <summary>Link-up board that accepts everything and never faults (mirrors RunManagerFaultTests').
    /// <see cref="Connected"/> can be flipped to false to exercise the comms-loss (E-130) finalize path.</summary>
    private sealed class FakeBoard : IPowerBoard
    {
        public event EventHandler<FaultRaised>? FaultRaised { add { } remove { } }
        public event EventHandler? ConfigRequested { add { } remove { } }
        public bool Connected { get; set; } = true;
        public bool IsConnected => Connected;

        public Task<FaultSnapshot?> GetFaultSnapshotAsync(CancellationToken ct = default) => Task.FromResult<FaultSnapshot?>(null);
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

using Microsoft.EntityFrameworkCore;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Unit tests for <see cref="SystemService.GetDatabaseAuditAsync"/>.
///
/// The audit method runs a batch of <c>CountAsync</c> queries grouped by the
/// status / severity / level enums, and counts programs over the UNFILTERED
/// set (so the soft-delete global query filter must be bypassed with
/// <c>IgnoreQueryFilters()</c>).
///
/// We exercise it against a real <see cref="ReflowDbContext"/> backed by the EF
/// Core InMemory provider (a fresh, uniquely-named store per test so the cases
/// are isolated). InMemory is used here — rather than SQLite — because the
/// production model emits PostgreSQL-specific DDL (a CHECK constraint using the
/// <c>~</c> regex operator) that SQLite cannot create at EnsureCreated time.
/// InMemory runs no DDL and honours the one model feature this method relies on:
/// the <see cref="ReflowProgram"/> soft-delete global query filter (asserted via
/// the IgnoreQueryFilters case). The owned-JSON columns are never read by the
/// audit method, so InMemory's lack of jsonb support does not matter here.
/// </summary>
public sealed class SystemServiceAuditTests
{
    [Fact]
    public async Task GetDatabaseAuditAsync_counts_users_by_status_and_role()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        // 2 active + 1 inactive = 3 total; 1 of the active users is an admin.
        Assert.Equal(3, audit.Users.Total);
        Assert.Equal(2, audit.Users.Active);
        Assert.Equal(1, audit.Users.Inactive);
        Assert.Equal(1, audit.Users.Admins);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_counts_programs_over_unfiltered_set()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        // 3 rows total (1 seed-active, 1 user-active, 1 user-deleted).
        // Total ignores the soft-delete filter; Active excludes the deleted one.
        // The service defines User = Active - Seed, i.e. the *active* non-seed
        // programs only — the soft-deleted one is in Total/Deleted but NOT User.
        Assert.Equal(3, audit.Programs.Total);
        Assert.Equal(2, audit.Programs.Active);   // total - deleted
        Assert.Equal(1, audit.Programs.Seed);     // seed & not deleted
        Assert.Equal(1, audit.Programs.User);     // active - seed
        Assert.Equal(1, audit.Programs.Deleted);
        // Sanity: the partition of the *active* set is Seed + User.
        Assert.Equal(audit.Programs.Active, audit.Programs.Seed + audit.Programs.User);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_splits_executions_by_status()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        Assert.Equal(3, audit.Executions.Total);
        Assert.Equal(2, audit.Executions.Concluido);
        Assert.Equal(1, audit.Executions.Falha);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_splits_errors_by_severity()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        Assert.Equal(3, audit.Errors.Total);
        Assert.Equal(1, audit.Errors.Critico);
        Assert.Equal(1, audit.Errors.Alerta);
        Assert.Equal(1, audit.Errors.Aviso);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_counts_changes()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        Assert.Equal(2, audit.Changes);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_counts_notifications_with_unread_tally()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        Assert.Equal(2, audit.Notifications.Total);
        Assert.Equal(1, audit.Notifications.Unread);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_splits_system_log_by_level()
    {
        await using var db = await SeededAsync();
        var service = NewService(db);

        var audit = await service.GetDatabaseAuditAsync();

        Assert.Equal(3, audit.SystemLog.Total);
        Assert.Equal(1, audit.SystemLog.Info);
        Assert.Equal(1, audit.SystemLog.Aviso);
        Assert.Equal(1, audit.SystemLog.Erro);
    }

    [Fact]
    public async Task GetDatabaseAuditAsync_returns_zeros_on_empty_database()
    {
        // A fresh, unseeded context: every count must be 0 (never throw).
        await using var empty = NewContext();
        var service = NewService(empty);

        var audit = await service.GetDatabaseAuditAsync();

        Assert.Equal(0, audit.Users.Total);
        Assert.Equal(0, audit.Programs.Total);
        Assert.Equal(0, audit.Executions.Total);
        Assert.Equal(0, audit.Errors.Total);
        Assert.Equal(0, audit.Changes);
        Assert.Equal(0, audit.Notifications.Total);
        Assert.Equal(0, audit.SystemLog.Total);
    }

    // ---- helpers ---------------------------------------------------------

    // A fresh InMemory-backed context with a unique store name, so each test
    // (and the empty-database test) is fully isolated.
    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    // GetDatabaseAuditAsync only touches the IAppDbContext dependency, never the
    // ISystemController, so the controller can be left null. The ctor order is
    // SystemService(ISystemController system, IAppDbContext db) — keep db second.
    private static SystemService NewService(IAppDbContext db) => new(system: null!, db: db);

    // ---- fixture ---------------------------------------------------------

    private static async Task<ReflowDbContext> SeededAsync()
    {
        var db = NewContext();

        // Users: 2 active + 1 inactive; one of the active users is an admin.
        db.Users.AddRange(
            NewUser("admin", UserType.Admin, UserStatus.Ativo),
            NewUser("operator", UserType.Regular, UserStatus.Ativo),
            NewUser("retired", UserType.Regular, UserStatus.Inativo));

        // Programs: 1 seed (active), 1 user (active), 1 user (soft-deleted).
        db.Programs.AddRange(
            NewProgram("seed-active", "Seed Active", isSeed: true, isDeleted: false),
            NewProgram("user-active", "User Active", isSeed: false, isDeleted: false),
            NewProgram("user-deleted", "User Deleted", isSeed: false, isDeleted: true));

        // Executions: 2 Concluído + 1 Falha.
        db.Executions.AddRange(
            NewExecution(ExecutionStatus.Concluido),
            NewExecution(ExecutionStatus.Concluido),
            NewExecution(ExecutionStatus.Falha));

        // Errors: one of each severity.
        db.Errors.AddRange(
            NewError(ErrorSeverity.Critico),
            NewError(ErrorSeverity.Alerta),
            NewError(ErrorSeverity.Aviso));

        // Changes: 2 audit rows.
        db.Changes.AddRange(NewChange(), NewChange());

        // Notifications: 1 read + 1 unread.
        db.Notifications.AddRange(
            NewNotification(read: true),
            NewNotification(read: false));

        // System log: one row of each level.
        db.SystemLog.AddRange(
            NewSystemLog(LogLevel.Info),
            NewSystemLog(LogLevel.Aviso),
            NewSystemLog(LogLevel.Erro));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static User NewUser(string name, UserType type, UserStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name}@example.test",
        Type = type,
        Status = status,
        PasswordHash = "x",
    };

    private static ReflowProgram NewProgram(string id, string name, bool isSeed, bool isDeleted) => new()
    {
        Id = id,
        Name = name,
        IsSeed = isSeed,
        IsDeleted = isDeleted,
    };

    private static ExecutionReport NewExecution(ExecutionStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProgramName = "Prog",
        Status = status,
    };

    private static ErrorLogEntry NewError(ErrorSeverity severity) => new()
    {
        Id = Guid.NewGuid(),
        Severity = severity,
    };

    private static ChangeLogEntry NewChange() => new() { Id = Guid.NewGuid() };

    private static Notification NewNotification(bool read) => new()
    {
        Id = Guid.NewGuid(),
        Read = read,
    };

    private static SystemLogEntry NewSystemLog(LogLevel level) => new()
    {
        Level = level,
    };
}

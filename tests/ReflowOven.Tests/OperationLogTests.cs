using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Common;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Covers the universal operation log (Log de Operação): <see cref="AuditService.Record"/> writes rows that
/// <see cref="OperationLogService.ListAsync"/> returns newest-first, paged and filtered by type/operator.
/// Runs against a real <see cref="ReflowDbContext"/> on the EF Core InMemory provider; the owned-JSON
/// <c>Data</c> collection round-trips in the object graph (no jsonb provider), like <see cref="ProgramServiceChangeLogTests"/>.
/// </summary>
public sealed class OperationLogTests
{
    [Fact]
    public async Task Record_then_List_returns_rows_newest_first_with_operator_and_data()
    {
        await using var db = NewContext();
        var audit = NewAudit(db, "lucas.silva");

        audit.Record(OperationType.Login, OperationObject.Sessao, "lucas.silva", [OperationField.Of("papel", "Admin")]);
        audit.Record(OperationType.Execucao, OperationObject.Execucao, "run-1",
            [OperationField.Of("status", "Concluido"), OperationField.Of("duracao_s", 120)]);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await new OperationLogService(db).ListAsync(new ReportQuery());

        Assert.Equal(2, page.Total);
        // Newest first: the execution was recorded last (advancing clock), so it leads.
        Assert.Equal(OperationType.Execucao, page.Items[0].Type);
        Assert.Equal(OperationCategory.Execucao, page.Items[0].Category); // derived from (type, object)
        Assert.Equal("run-1", page.Items[0].ObjectId);
        Assert.Equal("lucas.silva", page.Items[0].OperatorName);
        var statusField = page.Items[0].Data.Single(d => d.Field == "status");
        Assert.Equal("Concluido", statusField.After);
        Assert.Null(statusField.Before); // a stateless event has no "before"
    }

    [Fact]
    public async Task A_system_event_has_no_operator_id_and_is_named_Sistema()
    {
        await using var db = NewContext();
        // A background/system caller has no signed-in user — mirror that with an anonymous current, so the
        // operatorId falls back to null (and the name to "Sistema"), exactly as SystemMonitorService runs.
        var audit = new AuditService(db, new AdvancingClock(), new AnonymousUser(), NullLogger<AuditService>.Instance);

        audit.Record(OperationType.Comunicacao, OperationObject.Controlador, "servidor-central",
            [OperationField.Of("estado", "offline")], operatorName: "Sistema");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = (await new OperationLogService(db).ListAsync(new ReportQuery())).Items.Single();
        Assert.Equal("Sistema", row.OperatorName);
        Assert.Null(row.OperatorId);
        Assert.Equal(OperationType.Comunicacao, row.Type);
    }

    [Fact]
    public async Task ListAsync_filters_by_type_and_operator_and_clamps_page_size()
    {
        await using var db = NewContext();
        var audit = NewAudit(db, "admin");

        for (var k = 0; k < 3; k++) audit.Record(OperationType.Login, OperationObject.Sessao, $"u{k}", []);
        audit.Record(OperationType.Remocao, OperationObject.Usuario, "u9", []);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var svc = new OperationLogService(db);

        var logins = await svc.ListAsync(new ReportQuery(Type: "Login"));
        Assert.Equal(3, logins.Total);
        Assert.All(logins.Items, i => Assert.Equal(OperationType.Login, i.Type));

        var byOperator = await svc.ListAsync(new ReportQuery(Operator: "ADMIN")); // case-insensitive contains
        Assert.Equal(4, byOperator.Total);

        // Both logins (object Sessao) and the removal (object Usuario) bucket into the "Usuario" category.
        var usuarios = await svc.ListAsync(new ReportQuery(Category: "Usuario"));
        Assert.Equal(4, usuarios.Total); // 3 logins + 1 user removal
        Assert.All(usuarios.Items, i => Assert.Equal(OperationCategory.Usuario, i.Category));

        // Page size is clamped to ReportPageSizeMax even if the client asks for more.
        var clamped = await svc.ListAsync(new ReportQuery(PageSize: 999));
        Assert.Equal(DomainConstants.ReportPageSizeMax, clamped.PageSize);
    }

    // ---- helpers ---------------------------------------------------------

    private static AuditService NewAudit(ReflowDbContext db, string operatorName) =>
        new(db, new AdvancingClock(), new FakeUser(operatorName), NullLogger<AuditService>.Instance);

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"oplog-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    // Strictly-increasing clock so each row gets a distinct, ordered timestamp (newest-first is assertable).
    private sealed class AdvancingClock : IClock
    {
        private DateTimeOffset _now = new(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow { get { var t = _now; _now = _now.AddSeconds(1); return t; } }
    }

    private sealed class FakeUser(string name) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? Id => UserId?.ToString();
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Name { get; } = name;
        public UserType? Role => UserType.Admin;
        public bool IsCalibration => false;
    }

    // A background/technician principal: no user id or name (Record falls back to operatorName "Sistema").
    private sealed class AnonymousUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? Id => null;
        public Guid? UserId => null;
        public string? Name => null;
        public UserType? Role => null;
        public bool IsCalibration => false;
    }
}

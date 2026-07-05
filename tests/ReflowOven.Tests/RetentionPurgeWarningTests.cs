using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ReflowOven.Application.Dtos;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.BackgroundServices;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// BE-6 purge preview: <see cref="SystemMonitorService.UpsertPurgeWarningAsync"/> announces — one day
/// ahead, through ONE feed entry refreshed in place — what the next retention sweep will delete, with a
/// Relatórios deep link (<c>{ tab: "relatorios", until }</c>) when operation-log rows are involved.
/// Runs a real <see cref="ReflowDbContext"/> on the EF InMemory provider, like <see cref="OperationLogTests"/>.
/// </summary>
public sealed class RetentionPurgeWarningTests
{
    // Fixed "today" so the dates are assertable: tomorrow's op-log cutoff = 04/07/2025 (365-day default),
    // tomorrow's trash cutoff = 05/04/2026 (90-day default).
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly RetentionOptions Options = new(); // defaults: OperationLog 365 d, trash 90 d

    [Fact]
    public async Task Upserts_one_warning_with_the_ptBR_message_and_the_relatorios_deep_link()
    {
        await using var db = NewContext();
        db.OperationLog.AddRange(
            OpRow(Now.AddDays(-364.5)), // crosses the threshold within the next 24 h
            OpRow(Now.AddDays(-364.2)), // crosses too
            OpRow(Now.AddDays(-30)));   // safely inside the window
        await db.SaveChangesAsync();

        var dto = await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default);

        Assert.NotNull(dto);
        var n = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(SystemMonitorService.PurgeWarningTitle, n.Title);
        Assert.Equal(NotificationFeedKind.Warning, n.Kind); // the feed's "Atenção" level
        Assert.False(n.Read);
        Assert.Equal("2 registro(s) do log de operação serão excluídos amanhã (anteriores a 04/07/2025).", n.Message);
        Assert.NotNull(n.DeepLink);
        Assert.Equal("relatorios", n.DeepLink!.Tab);
        Assert.Equal(Now.AddDays(1 - Options.OperationLogDays), n.DeepLink.Until);
    }

    [Fact]
    public async Task Next_day_refreshes_the_same_entry_instead_of_stacking_a_new_one()
    {
        await using var db = NewContext();
        db.OperationLog.Add(OpRow(Now.AddDays(-364.5)));
        await db.SaveChangesAsync();

        Assert.NotNull(await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default));
        var first = await db.Notifications.SingleAsync();
        first.Read = true; // the operator saw it…
        await db.SaveChangesAsync();

        // …but the next daily check (new cutoff date → new text) must re-surface the SAME row.
        var dto = await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now.AddDays(1), default);

        Assert.NotNull(dto);
        var n = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(first.Id, n.Id);
        Assert.Equal(Now.AddDays(1), n.At);
        Assert.False(n.Read);
        Assert.Contains("05/07/2025", n.Message);
    }

    [Fact]
    public async Task An_unchanged_picture_is_not_re_announced()
    {
        await using var db = NewContext();
        db.OperationLog.Add(OpRow(Now.AddDays(-364.5)));
        await db.SaveChangesAsync();

        Assert.NotNull(await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default));
        // Same instant, same counts → same text: nothing to refresh (and nothing to publish).
        Assert.Null(await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default));
        Assert.Single(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Nothing_crossing_within_a_day_raises_nothing()
    {
        await using var db = NewContext();
        db.OperationLog.Add(OpRow(Now.AddDays(-363.5))); // only crosses in ~1.5 days, not tomorrow
        await db.SaveChangesAsync();

        Assert.Null(await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default));
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Trash_only_warning_speaks_of_notifications_and_carries_no_deep_link()
    {
        await using var db = NewContext();
        db.Notifications.Add(TrashedNotification(deletedAt: Now.AddDays(-89.5)));
        await db.SaveChangesAsync();

        var dto = await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default);

        Assert.NotNull(dto);
        Assert.Equal("1 notificação(ões) da lixeira serão excluídas amanhã (anteriores a 05/04/2026).", dto!.Message);
        Assert.Null(dto.DeepLink); // the trash is Master-only — no Relatórios view applies
    }

    [Fact]
    public async Task Both_categories_compose_both_parts_each_with_its_own_cutoff()
    {
        await using var db = NewContext();
        db.OperationLog.Add(OpRow(Now.AddDays(-364.5)));
        db.Notifications.Add(TrashedNotification(deletedAt: Now.AddDays(-89.5)));
        await db.SaveChangesAsync();

        var dto = await SystemMonitorService.UpsertPurgeWarningAsync(db, Options, Now, default);

        Assert.NotNull(dto);
        Assert.Equal(
            "1 registro(s) do log de operação (anteriores a 04/07/2025) e 1 notificação(ões) da lixeira " +
            "(anteriores a 05/04/2026) serão excluídos amanhã.",
            dto!.Message);
        Assert.Equal("relatorios", dto.DeepLink!.Tab);
    }

    [Fact]
    public void Wire_shape_is_camelCase_deepLink_with_tab_and_iso_until_and_is_omitted_when_null()
    {
        // Mirror the API's JSON setup (Program.cs): web defaults (camelCase) + enum strings + omit nulls.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        var linked = NotificationDto.From(new Notification
        {
            Id = Guid.Empty,
            At = Now,
            Kind = NotificationFeedKind.Warning,
            Title = "t",
            Message = "m",
            DeepLink = new NotificationDeepLink
            {
                Tab = "relatorios",
                Until = new DateTimeOffset(2025, 7, 4, 12, 0, 0, TimeSpan.Zero),
            },
        });
        var json = JsonSerializer.Serialize(linked, options);
        Assert.Contains("\"kind\":\"warning\"", json);
        Assert.Contains("\"deepLink\":{\"tab\":\"relatorios\",\"until\":\"2025-07-04T12:00:00+00:00\"}", json);

        var plain = JsonSerializer.Serialize(NotificationDto.From(new Notification { Id = Guid.Empty }), options);
        Assert.DoesNotContain("deepLink", plain);
    }

    // ---- helpers ---------------------------------------------------------

    private static ReflowDbContext NewContext() => new(
        new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"purge-{Guid.NewGuid():N}")
            .Options);

    private static OperationLogEntry OpRow(DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        At = at,
        OperatorName = "Sistema",
        Category = OperationCategory.Execucao,
        Type = OperationType.Execucao,
        Object = OperationObject.Execucao,
        ObjectId = "run-1",
        Data = [],
    };

    private static Notification TrashedNotification(DateTimeOffset deletedAt) => new()
    {
        Id = Guid.NewGuid(),
        At = deletedAt.AddDays(-10),
        Kind = NotificationFeedKind.Info,
        Title = "antiga",
        Message = "…",
        IsDeleted = true,
        DeletedAt = deletedAt,
        DeletedBy = "dev.pandewilly",
    };
}

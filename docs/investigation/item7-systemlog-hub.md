# item7-systemlog-hub

I have all the information needed. The `Level` column is plain `text` with no CHECK constraint. Here is the report.

## Files

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/SystemLogEntry.cs** (full shape)
```csharp
namespace ReflowOven.Domain.Entities;

/// <summary>
/// A line in the global system log (Diagnóstico → Manutenção), newest-first. Written by the
/// SystemLogCollector and on notable board/run events.
/// </summary>
public class SystemLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = "";
}
```
Note the XML doc already claims a "SystemLogCollector" writer "on notable board/run events" — but no such writer exists in `src/` today (only the demo seeder inserts rows).

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs** (the LogLevel enum — wire literals)
```csharp
/// <summary>Severity label of a system-log line (frontend <c>LogLevel</c>).</summary>
public enum LogLevel
{
    [JsonStringEnumMemberName("INFO")] Info,
    [JsonStringEnumMemberName("Aviso")] Aviso,
    [JsonStringEnumMemberName("Erro")] Erro,
}
```
Important: this is `ReflowOven.Domain.Enums.LogLevel` — a 3-member pt-BR enum, NOT `Microsoft.Extensions.Logging.LogLevel`. The wire literal for Info is `"INFO"` (uppercase), but `Aviso`/`Erro` are title-case.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Realtime/DiagnosticsHub.cs** (template — broadcast-to-all hub)
```csharp
using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>Live sensor readings for the Diagnóstico screen. Server pushes ReadingTick to all clients at 1 Hz.</summary>
[Authorize]
public sealed class DiagnosticsHub : Hub;
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Realtime/RunTelemetryHub.cs** (template — per-group hub)
```csharp
using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>
/// Live execution trace. Clients join a per-run group via <see cref="SubscribeRun"/>; the server
/// pushes TraceSample / RunPhaseChanged / RunStatusChanged / RunCompleted to that group.
/// </summary>
[Authorize]
public sealed class RunTelemetryHub : Hub
{
    public Task SubscribeRun(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));

    public Task UnsubscribeRun(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId));

    public static string GroupName(Guid runId) => $"run:{runId}";
}
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Abstractions/ITelemetrySink.cs** (the abstraction-pattern template — lives in Domain)
```csharp
namespace ReflowOven.Domain.Abstractions;

/// <summary>
/// Decouples the hardware/run loop from SignalR: the loop publishes here and the API layer's
/// implementation fans the payloads out to the RunTelemetry / Diagnostics hubs. Keeping it in
/// Domain means the simulated and real boards feed identical telemetry without referencing the API.
/// </summary>
public interface ITelemetrySink
{
    Task PublishTraceAsync(Guid runId, TraceSample sample);
    Task PublishPhaseAsync(Guid runId, RunPhase phase);
    Task PublishStatusAsync(Guid runId, RunStatus status);
    Task PublishCompletedAsync(Guid runId, Guid executionId);
    /// <summary>Standalone sensor tick for the Diagnóstico screen (no active run required).</summary>
    Task PublishReadingAsync(SensorReadings readings);
}
```
Caveat: `ITelemetrySink` is in **Domain** and its methods take **Domain types** (`TraceSample`, `SensorReadings`). The SignalR impl (`SignalRTelemetrySink`, in Api) is what converts to DTOs. The DTOs (`TraceSampleDto`, `SensorReadingsDto`) live in **Application**. A new `ISystemLogSink` that emits a `SystemLogEntryDto` would have to take the DTO — but DTOs are in Application, and Domain cannot reference Application. So the seam should be placed in **`Application/Abstractions`** (next to `IAppDbContext`, `IRunManager`), NOT Domain — that lets it use `SystemLogDto`. (Contrast: `ITelemetrySink` only works in Domain because it uses Domain types, not DTOs.)

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Realtime/SignalRTelemetrySink.cs** (the IHubContext fan-out impl — exact template)
```csharp
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

    public Task PublishReadingAsync(SensorReadings readings) =>
        diagnosticsHub.Clients.All.SendAsync("ReadingTick", SensorReadingsDto.From(readings));
    // ... (other Publish* members omitted)
}
```
This is the exact pattern to copy: a `sealed class` with primary-ctor `IHubContext<TheHub>`, registered as a **singleton** in `Program.cs`, calling `hub.Clients.All.SendAsync("EventName", dto)`. The diagnostics broadcast-to-all variant (`Clients.All`) is the right model for system-log (device-wide, like notifications).

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/ReportDtos.cs** (the existing payload DTO — REUSE this)
```csharp
public sealed record SystemLogDto(long Id, DateTimeOffset At, LogLevel Level, string Message);
```
Reuse `SystemLogDto` as the hub payload — same record the polled `GET /api/system-log` returns, so the front already parses it.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ReportService.cs** (`SystemLogAsync` — how Log do Sistema reads it; level filter)
```csharp
public sealed class ReportService(IAppDbContext db)
// ...
public async Task<PagedResult<SystemLogDto>> SystemLogAsync(ReportQuery q, CancellationToken ct = default)
{
    IQueryable<SystemLogEntry> query = db.SystemLog;
    if (!string.IsNullOrWhiteSpace(q.Search))
    {
        var s = q.Search.Trim().ToLower();
        query = query.Where(l => l.Message.ToLower().Contains(s));
    }
    if (q.From is not null) query = query.Where(l => l.At >= q.From);
    if (ToExclusive(q) is { } toExc) query = query.Where(l => l.At < toExc);
    if (EnumWire.TryFromWire<LogLevel>(q.Level, out var level)) query = query.Where(l => l.Level == level);

    var total = await query.CountAsync(ct);
    var (page, size) = Paging(q);
    var items = await query
        .OrderByDescending(l => l.Id)
        .Skip((page - 1) * size).Take(size)
        .Select(l => new SystemLogDto(l.Id, l.At, l.Level, l.Message))
        .ToListAsync(ct);
    return new PagedResult<SystemLogDto>(items, total, page, size);
}
```
The level filter accepts the pt-BR wire literal via `EnumWire.TryFromWire<LogLevel>(q.Level, …)`. Reads are ordered newest-first (`OrderByDescending(l => l.Id)`), so a real-time push prepended to the front's list keeps the same ordering.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/ReportControllers.cs** (the GET /api/system-log endpoint)
```csharp
[ApiController]
[Route("api/system-log")]
public sealed class SystemLogController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SystemLogDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => reports.SystemLogAsync(query, ct);
}
```
Authenticated-by-default (no `AllowAnonymous`, no `AdminOnly`) — the new hub should match: `[Authorize]` only.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Program.cs** (hub mapping + SignalR/JWT-from-query wiring + Serilog)
```csharp
// SignalR registration (line 56-57):
builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// telemetry sink singleton (line 47):
builder.Services.AddSingleton<ITelemetrySink, SignalRTelemetrySink>();

// JWT-from-query for hubs (lines 87-96):
options.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var accessToken = ctx.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            ctx.Token = accessToken;
        return Task.CompletedTask;
    },
};

// Serilog wiring (lines 33-39):
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));
// ... later: app.UseSerilogRequestLogging();  (line 180)

// hub mapping (lines 201-202):
app.MapHub<RunTelemetryHub>("/hubs/telemetry");
app.MapHub<DiagnosticsHub>("/hubs/diagnostics");
```
The `/hubs` JWT-from-query check uses `StartsWithSegments("/hubs")`, so any `/hubs/...` path inherits the access_token auth automatically — a new `/hubs/systemlog` needs no JWT change. `AddHttpLogging` is wired (lines 115-126) and only enabled in Development (`app.UseHttpLogging()`, line 185); request bodies on `/api/auth/*` are redacted by `AuthRedactionInterceptor`.

**How SystemLog is written today — CONFIRMED no runtime writer.** Every insertion site for `db.SystemLog.Add(...)`:
```
src/ReflowOven.Infrastructure/Persistence/DbSeeder.Demo.cs:94-99
    if (!await db.SystemLog.AnyAsync(ct))
        for (var i = 0; i < 45; i++)
        {
            var (level, message) = SystemLogMessages[i % SystemLogMessages.Length];
            db.SystemLog.Add(new SystemLogEntry { At = now.AddMinutes(-(45 - i) * 7), Level = level, Message = message });
        }
```
That is the ONLY `.Add(new SystemLogEntry…)` in the entire `src/`. Everywhere else `db.SystemLog` is read (`ReportService`, `SystemService`, `MaintenanceService.CountAsync`/`ExecuteDeleteAsync`, `StartupDiagnostics`) or mapped (`ReflowDbContext`, `IAppDbContext`). The XML doc's "SystemLogCollector" does not exist. **The hub has nothing real to push until a writer is added.** By contrast, the notification feed already has runtime writers (`SystemMonitorService.RaiseAsync`, `RunManager` finalize) — but those write `db.Notifications`, not `db.SystemLog`, and notifications are still polled (no notification hub exists). So `ITelemetrySink`/`SignalRTelemetrySink` is the only existing real-time push template.

## Change plan

The minimal, lowest-risk design: a `SystemLogService` writer (persist + push) used at key seams, NOT a DB-writing Serilog sink (the Serilog-sink option risks recursion/volume — see Risks). Sink seam lives in **Application/Abstractions** (so it can use the `SystemLogDto` from Application; Domain can't reference DTOs).

1. **Add `src/ReflowOven.Application/Abstractions/ISystemLogSink.cs`** — the API-layer push seam (mirrors `ITelemetrySink`):
   ```csharp
   namespace ReflowOven.Application.Abstractions;

   /// <summary>Fans a freshly-written system-log line out to the SystemLog SignalR hub (Api impl).</summary>
   public interface ISystemLogSink
   {
       Task PublishAsync(SystemLogDto entry);
   }
   ```
   Add `using ReflowOven.Application.Dtos;` (or rely on the Application `GlobalUsings.cs` — confirm `Dtos` is global there; if not, add the using).

2. **Add `src/ReflowOven.Application/Services/SystemLogService.cs`** — the writer that BOTH persists and pushes (mirrors `SystemMonitorService.RaiseAsync`):
   ```csharp
   namespace ReflowOven.Application.Services;

   public sealed class SystemLogService(IAppDbContext db, IClock clock, ISystemLogSink sink)
   {
       public async Task WriteAsync(LogLevel level, string message, CancellationToken ct = default)
       {
           var entry = new SystemLogEntry { At = clock.UtcNow, Level = level, Message = message };
           db.SystemLog.Add(entry);
           await db.SaveChangesAsync(ct);
           await sink.PublishAsync(new SystemLogDto(entry.Id, entry.At, entry.Level, entry.Message));
       }
   }
   ```
   `IClock` is in `Domain.Abstractions` (already global in Application GlobalUsings — verify). `entry.Id` is populated by EF after `SaveChangesAsync` (identity column), so the pushed DTO carries the real id, matching the front's ordering-by-id.

3. **Register the service** in `src/ReflowOven.Application/DependencyInjection.cs`, after line 22 (`services.AddScoped<NotificationService>();`):
   - old: `        services.AddScoped<NotificationService>();`
   - new: `        services.AddScoped<NotificationService>();\n        services.AddScoped<SystemLogService>();`
   Note: `SystemLogService` is scoped (uses `IAppDbContext`), but `ISystemLogSink` will be a singleton (like `ITelemetrySink`) — scoped→singleton injection is fine.

4. **Add `src/ReflowOven.Api/Realtime/SystemLogHub.cs`** — copy `DiagnosticsHub` (broadcast-to-all, no groups needed since the feed is device-wide):
   ```csharp
   using Microsoft.AspNetCore.SignalR;

   namespace ReflowOven.Api.Realtime;

   /// <summary>Live system-log lines (Log do Sistema). Server pushes SystemLogLine to all clients.</summary>
   [Authorize]
   public sealed class SystemLogHub : Hub;
   ```
   (`Authorize` resolves via Api `GlobalUsings.cs` → `Microsoft.AspNetCore.Authorization`.)

5. **Add `src/ReflowOven.Api/Realtime/SignalRSystemLogSink.cs`** — copy the `PublishReadingAsync` shape from `SignalRTelemetrySink`:
   ```csharp
   using Microsoft.AspNetCore.SignalR;

   namespace ReflowOven.Api.Realtime;

   /// <summary>Bridges SystemLogService writes to SignalR. Singleton (depends only on the hub context).</summary>
   public sealed class SignalRSystemLogSink(IHubContext<SystemLogHub> hub) : ISystemLogSink
   {
       public Task PublishAsync(SystemLogDto entry) =>
           hub.Clients.All.SendAsync("SystemLogLine", entry);
   }
   ```
   (`ISystemLogSink` + `SystemLogDto` resolve via Api `GlobalUsings.cs` → `ReflowOven.Application.Abstractions` + `ReflowOven.Application.Dtos`.)

6. **In `src/ReflowOven.Api/Program.cs`**, register the sink as a singleton, beside the telemetry sink (line 47):
   - old: `builder.Services.AddSingleton<ITelemetrySink, SignalRTelemetrySink>();`
   - new: add a line below it: `builder.Services.AddSingleton<ISystemLogSink, SignalRSystemLogSink>();`

7. **In `src/ReflowOven.Api/Program.cs`**, map the hub beside the others (line 202):
   - old: `app.MapHub<DiagnosticsHub>("/hubs/diagnostics");`
   - new: add below it: `app.MapHub<SystemLogHub>("/hubs/systemlog");`
   No JWT change needed — `OnMessageReceived` already matches any `/hubs/...` path.

8. **Choose the SOURCE (wire the writer at real seams).** Inject `SystemLogService` and call `WriteAsync(...)` where today only a `Notification` or a Serilog line is emitted. Minimal high-value seams:
   - `RunManager` finalize (around line 226-239, where it already adds a `Notification`): emit one `SystemLogEntry` per run start/finalize/board-error. `RunManager` is in Infrastructure and resolves `IAppDbContext` via a scope — it could either inject `ISystemLogSink` + write directly, or (cleaner) a thin `ISystemLogSink.PublishAsync` after adding the row to its existing `db` + `SaveChangesAsync`. Keep `SystemLogService` for Application-layer callers; for `RunManager` reuse its existing scoped `db` and just add `ISystemLogSink` to push.
   - `SystemMonitorService` (around line 119 `RaiseAsync`): central-server up/down + update-available also warrant a SystemLog line.
   Do NOT make this a global Serilog sink in this first pass (recursion/volume risk). If the TODO's "idealmente as linhas do Serilog" is desired later, do it as a separate, filtered custom Serilog `ILogEventSink` (exclude `Microsoft.*`/EF categories, min level ≥ Warning, fire-and-forget to a scoped writer) — but that is a bigger, riskier change; flag it as a follow-up.

## Migration?

**no.** The `SystemLog` table already exists (created in `20260530165250_AddFaultTypes.cs`), mapped in `ReflowDbContext.OnModelCreating` (lines 149-154), and the schema is unchanged — no new entity, no new column, no new index. The `Level` column is `table.Column<string>(type: "text", nullable: false)` with **no CHECK constraint** (the table's only constraint is `table.PrimaryKey("PK_SystemLog", x => x.Id)`). Enum values are stored as pt-BR text via the generic `PtBrEnumConverter<T>` loop in `OnModelCreating` (lines 221-232), so no enum-member or constraint change is involved. SignalR hubs and DI registrations are not part of the EF model, so nothing to scaffold.

## Contract/enum notes

- **`LogLevel` wire literals are the contract — never change them.** `Info → "INFO"` (uppercase), `Aviso → "Aviso"`, `Erro → "Erro"` (`[JsonStringEnumMemberName(...)]` in `Enums.cs`). The hub serializes `SystemLogDto.Level` through the global `JsonStringEnumConverter` registered on SignalR's `AddJsonProtocol` (Program.cs lines 56-57), so the pushed JSON matches `GET /api/system-log` exactly. The DB stores the same text via `PtBrEnumConverter<LogLevel>` — JSON and DB stay identical.
- **Reuse `SystemLogDto(long Id, DateTimeOffset At, LogLevel Level, string Message)`** as the hub payload — it is the exact record `GET /api/system-log` returns, so the front parses pushed lines with zero new mapping. `At` is ISO 8601 (`DateTimeOffset`); the front already converts ISO timestamps to epoch-ms for its local models (as the CLAUDE.md notes for `DeviceInfoDto`).
- The new event method name (recommended `"SystemLogLine"`) is a new client-listener contract the front must subscribe to — pick it and document it (mirrors `"ReadingTick"`/`"TraceSample"`).
- Re the prompt's `UserType` note: not used by this change. For completeness it is accurate — `UserType` has no `[JsonStringEnumMemberName]`, so its wire literal equals the C# member name (e.g. a `Master` member serializes as `"Master"`). `LogLevel` here is the opposite: it DOES carry attributes, so do not assume member==wire for it.

## Risks / open questions

- **No runtime writer exists today** — the hub alone pushes nothing real. The deliverable must include step 8 (seam wiring) or the feature is inert. Confirmed: the only `db.SystemLog.Add` is the demo seeder.
- **Serilog-sink option is the tempting-but-dangerous path.** A DB-writing Serilog sink can recurse (EF/Npgsql/the request logger emit their own log events; writing them to the DB triggers more events). If pursued later it MUST filter by category (drop `Microsoft.*`, `Microsoft.EntityFrameworkCore.*`, Serilog request logging) and by min level, and write fire-and-forget off the logging thread. Recommend deferring; use the explicit `SystemLogService` seams first.
- **Scoped-writer-from-singleton:** `SystemLogService` is scoped (`IAppDbContext`), `ISystemLogSink` is singleton — the service depends on the singleton sink (fine). But background services (`RunManager`, `SystemMonitorService`) already create their own scope per tick; they should reuse their existing scoped `db` and only take the singleton `ISystemLogSink` to push, rather than resolving the scoped `SystemLogService`.
- **`SystemLogService.WriteAsync` calls `SaveChangesAsync` on its own** — do not call it inside a larger unit-of-work that another service will also save, or you double-commit. For the seams in `RunManager`/`SystemMonitorService` that already batch a `SaveChangesAsync`, add the `SystemLogEntry` to that same `db` and push via `ISystemLogSink` after their existing save (don't nest a second `WriteAsync`).
- **Authorization scope:** the polled `GET /api/system-log` is authenticated-by-default (any logged-in user). The hub uses `[Authorize]` to match. If Log do Sistema should be admin-only, that's a product decision not reflected in the current endpoint — flag if the front restricts the screen.
- **Volume/throttle:** unlike the 1 Hz diagnostics tick, log lines are event-driven and low-rate at the chosen seams, so no caching/last-value priming is needed (unlike `SignalRTelemetrySink._lastSample`). If a future Serilog sink is added, revisit (could be high-rate).
- **Open question — event name + payload envelope:** confirm with the front team the listener name (`"SystemLogLine"`) and that it expects a single `SystemLogDto` (not a list / not wrapped in `PagedResult`). The front's 2s poll returns `PagedResult<SystemLogDto>`; the push should send the bare DTO to prepend.
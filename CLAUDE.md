# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Backend for a **reflow oven** (SMD solder-reflow) — the API/control service paired with the Next.js
touchscreen frontend at `../reflow-oven-front`. It replaces that UI's `localStorage` mock with a real
**ASP.NET Core (.NET 10) + EF Core + PostgreSQL** backend, drives a run, streams live telemetry, and
persists profiles, runs, faults and audit history. It runs on a Raspberry/Orange Pi and talks to an
STM32 power board over **RS422** (abstracted; a simulator ships by default).

The UI it serves is **pt-BR**; C# identifiers are English. Several domain string literals are pt-BR
and **part of the API contract** — see "Enums" below.

## Commands

The .NET 10 SDK is installed under `~/.dotnet` (not on the global PATH). Prefix shells with:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```

```bash
docker compose up -d                              # start PostgreSQL (required to run/migrate-apply)
dotnet build ReflowOven.slnx                      # build everything (note: .slnx, the new XML solution format)
dotnet run --project src/ReflowOven.Api           # run API (migrates + seeds on startup); Scalar API ref at /scalar
dotnet test                                       # run all tests
dotnet test --filter FullyQualifiedName~ProfileBuilderTests   # run one test class

# EF Core migrations (design-time factory => no running host/DB needed to scaffold)
dotnet ef migrations add <Name> -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations
dotnet ef database update      -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api   # needs Postgres up
```

`dotnet ef` is installed as a global tool. The migration **applies automatically on API startup**
(`Database.MigrateAsync()` + `DbSeeder`), so normally you only run `migrations add`.

Seeded dev login: any seeded user (e.g. `lucas.silva`, admin) with password **`reflow1234`**, or the
hidden technician `calibracao` / `calibra`. See `Application/Common/Defaults.cs`.

## Architecture

Clean Architecture, four projects + tests; dependencies point inward (Api → Infrastructure → Application → Domain):

- **`ReflowOven.Domain`** — entities, enums, `DomainConstants` (the C# mirror of the frontend's
  `limits.ts`), the hardware/clock/telemetry abstractions (`IPowerBoard`, `IClock`, `ITelemetrySink`), and
  the OS/platform abstraction (`Platform/ISystemController` — network/clock/update/power on the Pi).
  No external dependencies.
- **`ReflowOven.Application`** — `IAppDbContext`, DTOs (the JSON contract), services
  (`Auth/User/Program/Settings/Calibration/Report/Diagnostics/Maintenance/Device/Audit/System/Notification`),
  `ProfileBuilder` (server-side port of the editor's `toProfile` + run interpolation), and `Defaults`
  (seed/factory data).
- **`ReflowOven.Infrastructure`** — `ReflowDbContext` (+ all mapping inline in `OnModelCreating`),
  EF migrations, `DbSeeder`, the power board (`SimulatedPowerBoard` default / `Rs422PowerBoard` stub),
  the OS controller (`Platform/SimulatedSystemController` default / `LinuxSystemController`),
  `RunManager`, the `RunControlLoopService` + `SystemMonitorService` background loops,
  JWT/BCrypt/clock/email implementations.
- **`ReflowOven.Api`** — `Program.cs` wiring, controllers, the two SignalR hubs + `SignalRTelemetrySink`,
  `CurrentUser` (reads JWT claims), the ProblemDetails exception middleware, **Serilog** (console) and the
  **Scalar** API reference (`/scalar`, OpenAPI doc at `/openapi/v1.json`) — both dev-only, `AllowAnonymous`.

### Things that span multiple files

- **Live run + telemetry.** `RunManager` (singleton) owns the one active run; `RunControlLoopService`
  ticks it at 1 Hz. Each tick reads `IPowerBoard`, interpolates the setpoint (`alvo`) from the program
  profile, classifies the phase, and pushes a `TraceSample` through `ITelemetrySink` →
  `RunTelemetryHub` (`/hubs/telemetry`, per-run groups). When idle, the loop pushes sensor readings to
  `DiagnosticsHub` (`/hubs/diagnostics`). On a terminal state the run is persisted as an `ExecutionReport`.
  The board is fully abstracted — `SimulatedPowerBoard` interpolates the profile so telemetry is coherent
  with no hardware; swap to `Rs422PowerBoard` via `Hardware:Mode=Rs422`.
- **OS control + notification feed (OrangePi).** `ISystemController` (Domain `Platform`) is the device
  counterpart of `IPowerBoard`: OS metrics (CPU/mem/disk/uptime/temp), network/Wi-Fi/IP, clock/NTP,
  software update (OTA) and reboot/shutdown, plus central-server reachability. `SimulatedSystemController`
  (default) returns plausible data and no-ops mutations so the API runs on a dev box; `LinuxSystemController`
  shells out to `nmcli`/`timedatectl`/`systemctl` + `/proc`/`/sys` (selected by **`System:Mode=Linux`**).
  Mutating OS commands need privileges — run under a user with the matching **sudoers/polkit** rules.
  `SystemService` wraps it (and adds the real DB size); `SystemController` exposes `/api/system/*`
  (reads authenticated, every mutation `AdminOnly`). The **notification feed** (`Notification` entity →
  `/api/notifications`, the TopBar bell + Notificações screen) is device-wide: `RunManager` raises one on
  each run finalize, `SystemMonitorService` (a `BackgroundService`) raises one when the central server goes
  up/down or an update appears. Feed `kind` is `info`/`error`/`update`; `at` is ISO 8601 (the frontend maps
  it to the epoch-ms its local `AppNotification` uses, like it already does for `DeviceInfoDto`).
- **Network changes apply to the OS.** `SettingsService.UpdateAsync` persists the Rede form to the DB **and**
  best-effort calls `ISystemController.ApplyNetworkConfigAsync` when a network field changed (a failed apply
  is logged, never fails the save). Note the OS-level config record is `Platform.OsNetworkConfig` — named to
  avoid colliding with the persisted `Entities.NetworkConfig` (both namespaces are globally imported).
- **Enums are pt-BR text on the wire AND in the DB.** Enum members carry `[JsonStringEnumMemberName(...)]`
  with the exact accented literal the frontend expects (`Concluído`, `Crítico`, `Parábola positiva`,
  `Contínuo`, `Atenção`, network link `Cabo`/`WiFi`/`Nenhum`, feed `info`/`error`/`update`, …). A global
  `JsonStringEnumConverter` handles JSON; `PtBrEnumConverter<T>` (applied to every non-JSON enum column in
  `OnModelCreating`) reads the *same* attribute so DB text and JSON stay identical. **Never change these
  literals** without changing the frontend — they are the contract.
- **Limits are a single source of truth.** `DomainConstants` mirrors `../reflow-oven-front/src/lib/limits.ts`
  exactly. Enforce caps via the validators/services; keep the two files in lock-step.
- **Soft-delete & audit.** Programs are soft-deleted (`IsDeleted` + a global query filter hides them and the
  seeded catalog); everything else is hard-deleted. `AuditService` writes a `ChangeLogEntry` and bumps the
  per-user activity counters on every program/config mutation.
- **Logging is console-only (Serilog).** `Program.cs` wires Serilog (`UseSerilog` + `UseSerilogRequestLogging`
  → one line per HTTP request). Operational events are logged at the service seams: `AuthService`
  (login OK/falha/logout), `UserService` (CRUD), `AuditService` (program + config changes — covers
  `ProgramService`/`SettingsService`), `RunManager` (run start/finalize/board errors). `ExceptionMiddleware`
  logs `AppException` as **Warning** (it is handled → 400/409, never a crash) and unhandled errors as Error.
  EF Core SQL + detailed errors are on in Development only. This is **separate** from the `SystemLog` DB
  table (read by the *Log do Sistema* screen, currently demo-seeded).
- **Server-side paging caps.** Every list query clamps `pageSize` so a client can't pull an unbounded set:
  reports/logs ≤ `DomainConstants.ReportPageSizeMax` (200), programs ≤ `ProgramPageSizeMax` (100),
  notifications ≤ `NotificationFeedMax` (200). `MaintenanceService` reports the **real** DB size via
  `IAppDbContext.GetDatabaseSizeBytesAsync` (`pg_database_size`); only the per-category split is an estimate.
- **Auth.** JWT bearer, authenticated-by-default (fallback policy). `AdminOnly` guards writes; `CalibrationOnly`
  (the `calibration` claim) guards the hidden Calibração endpoints. The technician login is config, not a
  `User` row. SignalR takes the JWT from the `access_token` query string.
- **Seeding / factory reset.** `Defaults` is the one source for the fault catalog (8), notification rows (11),
  run-series prefs (7), default settings, the factory program and the ~50-program catalog. Both `DbSeeder`
  and `MaintenanceService.FactoryResetAsync` use it.

## API contract notes (differs from the frontend mock)

Timestamps are sent as **ISO 8601** (or null), not pre-formatted strings — the client formats them
(null `lastUsed` → "Nunca", null `lastLogin` → "—"). Login returns the frontend `{ ok, error }` shape with
the exact pt-BR error strings. `Program` ids are slugs for seeds and GUID strings for new programs.

## Conventions

- **Commits in English** (subject + body), conventional-commit prefixes. **No `Co-Authored-By` / authorship
  trailer.** Never commit/push without an explicit request. Active branch: **`develop`**
  (remote: `github.com/LegiusAndrade/reflow-oven-backend`).
- Match the surrounding style: file-scoped namespaces, primary constructors, expression-bodied members,
  collection expressions (`[...]`), nullable enabled. Keep `GlobalUsings.cs` per project.
- When adding an entity: map it in `ReflowDbContext.OnModelCreating`, add a `DbSet` to both `ReflowDbContext`
  and `IAppDbContext`, then `migrations add`. Wide/read-whole data (curves, snapshots) is stored as `jsonb`
  via `OwnsMany(...).ToJson()`; singletons (`Settings`, `Calibration`, `DeviceInfo`) use an `Id = 1` check
  constraint.

## API & security guidelines

These hold across the API; follow them when adding or changing endpoints.

- **DTOs in, DTOs out.** Every endpoint takes and returns a typed DTO (`Application/Dtos`), never an entity.
  Login already uses a request/response pair (`LoginRequest` → `LoginResult`); keep that shape. **Validate**
  incoming data (lengths/ranges/required) in the service before persisting.
- **Strong typing, async, REST.** No `object`/`dynamic` on the wire; use `async`/`await` for all I/O; keep
  REST conventions and consistent names. Don't rename/restructure existing endpoints without a real need.
- **Separation of concerns.** Controllers are thin: they call Application **services** and never touch
  `IAppDbContext`/EF directly. DB access lives in services (or the run/system background services via a scope).
- **Never expose or hardcode secrets.** Output DTOs must never carry a password or hash (`UserDto` doesn't).
  The **JWT signing key is never hardcoded**: it comes from config/env (`Jwt__SigningKey`) or user-secrets;
  `Program.cs` *fails fast* if a non-Development run is left on the empty/dev-placeholder key. The committed
  `appsettings.json` values (dev DB password, the `dev-only-change-me…` key, technician creds) are
  **dev-only placeholders** — set real secrets via environment in production.
- **Auth.** Authenticated-by-default (fallback policy) is the project's `RequireAuthorization()`; only
  `login`/`forgot-password`/`health`/Scalar are `AllowAnonymous`. Guard writes with `AdminOnly`
  (and `CalibrationOnly` for Calibração). Token minting is centralized in `JwtTokenService` — never build a
  JWT elsewhere (`Program.cs` only uses the key to *validate*).

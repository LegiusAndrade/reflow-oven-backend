namespace ReflowOven.Infrastructure.Persistence;

/// <summary>
/// Dev-only demo data: a LARGE, coherent dataset so every frontend screen has realistic content —
/// ~200 programs, 120 executions (failures linked to a real error + failure chart), standalone errors,
/// 200 program/config changes (several on the same program), the universal operation log mirroring all of
/// it (linked via ObjectId), plus favorites, per-user counters, board hour-meters and a few soft-deleted
/// rows for the Master's "Lixeira". Idempotent (skips a table that already has rows). Enabled by config
/// `Seed:Demo` (true in Development); never seed this in production.
/// </summary>
public static partial class DbSeeder
{
    private const int SnapshotSamples = 40;
    private const string EditChurnProgramId = "demo-edit-churn";

    // Scale knobs for the "huge" demo dataset.
    private const int DemoProgramCount = 150;   // + the ~50 catalog programs ≈ 200 total
    private const int DemoExecutions = 120;     // ~1 in 4 fails (and links to an error)
    private const int DemoStandaloneErrors = 20; // extra errors not tied to an execution
    private const int DemoChanges = 188;        // + the 12-row churn history = 200 changes

    private static readonly string[][] ConfigBulletSets =
    [
        ["Tensão máxima: 110 V → 200 V"],
        ["Corrente máxima: 8 A → 12 A", "Desligamento por sobretemperatura: ativado"],
        ["Limite do dissipador: 75 °C → 85 °C"],
        ["RS422: 57600 → 115200 bps", "Amostragem: 500 ms → 250 ms"],
        ["PID P: 12,0 → 14,5", "PID I: 0,80 → 0,65"],
        ["Tempo extra máximo: 30 s → 45 s"],
    ];

    private static readonly (LogLevel Level, string Message)[] SystemLogMessages =
    [
        (LogLevel.Info, "Sistema iniciado"),
        (LogLevel.Info, "Comunicação RS422 estabelecida (115200 bps)"),
        (LogLevel.Info, "Leitura do termopar tipo-K: ok"),
        (LogLevel.Info, "Programa carregado"),
        (LogLevel.Info, "Processo iniciado pelo usuário"),
        (LogLevel.Aviso, "Temperatura do dissipador atingiu 80 °C"),
        (LogLevel.Aviso, "Ventoinha 1 abaixo da rotação esperada"),
        (LogLevel.Aviso, "Subtensão na entrada 127 VAC"),
        (LogLevel.Erro, "Sobrecorrente detectada (sensor Hall)"),
        (LogLevel.Erro, "Perda de comunicação RS422 com a placa de potência"),
        (LogLevel.Info, "Calibração do sensor de corrente aplicada"),
        (LogLevel.Info, "Configurações salvas"),
    ];

    public static async Task SeedDemoAsync(ReflowDbContext db, IPasswordHasher hasher, IClock clock, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // The extra dev operators (operador1/operador2) are demo-only — seeded here, never in production.
        foreach (var u in Defaults.Users())
            await SeedAccountAsync(db, hasher, clock, u.Name, u.Email, Defaults.DefaultDevPassword, u.Type, u.Status, ct);
        await db.SaveChangesAsync(ct);

        var users = await db.Users.OrderBy(u => u.CreatedAt).ToListAsync(ct);
        if (users.Count == 0) return;

        // --- Bulk demo programs (so the gallery + reports reference ~200 programs) ----------------------
        if (await db.Programs.CountAsync(ct) < DemoProgramCount)
        {
            foreach (var p in BuildDemoPrograms(now))
                db.Programs.Add(p);
            await db.SaveChangesAsync(ct);
        }

        var programs = await db.Programs.OrderBy(p => p.Id).ToListAsync(ct);
        var faults = await db.FaultTypes.OrderBy(f => f.Code).ToListAsync(ct);
        if (programs.Count == 0) return;

        // Per-user login counts + activity counters.
        for (var i = 0; i < users.Count; i++)
        {
            var u = users[i];
            if (u.LoginCount == 0)
                u.LoginCount = 12 + Hash(u.Id.ToString()) % 200 + (u.Type == UserType.Admin ? 80 : 0);
            if (!await db.UserActivityStats.AnyAsync(s => s.UserId == u.Id, ct))
                for (var k = 0; k < Defaults.ActivityLabels.Length; k++)
                    db.UserActivityStats.Add(new UserActivityStat { UserId = u.Id, Label = Defaults.ActivityLabels[k], Count = (i + 1) * (k + 2) % 37 });
        }

        // Favorites for the first admin.
        var admin = users.FirstOrDefault(u => u.Type == UserType.Admin) ?? users[0];
        if (!await db.Favorites.AnyAsync(ct))
            foreach (var p in programs.Take(8))
                db.Favorites.Add(new FavoriteProgram { UserId = admin.Id, ProgramId = p.Id });

        // Audit-trail rows are accumulated here and added once at the end (mirroring the data below).
        var ops = new List<OperationLogEntry>();
        var seedOps = !await db.OperationLog.AnyAsync(ct);

        // --- Executions — a failed run creates a linked ErrorLogEntry so the report deep-links + charts it.
        if (!await db.Executions.AnyAsync(ct))
            for (var i = 0; i < DemoExecutions; i++)
            {
                var program = programs[i % programs.Count];
                var user = users[i % users.Count];
                var fault = i % 4 == 3 && faults.Count > 0 ? faults[i % faults.Count] : null;

                var (exec, error) = BuildRun(i, now, program, user, fault);
                if (error is not null)
                {
                    db.Errors.Add(error);
                    ops.Add(OpRow(error.At, user, OperationType.Erro, OperationObject.Falha, error.Id.ToString(),
                        OperationField.Of("codigo", error.FaultTypeCode), OperationField.Of("motivo", error.Message)));
                }
                db.Executions.Add(exec);
                ops.Add(OpRow(exec.StartedAt, user, OperationType.Execucao, OperationObject.Execucao, exec.Id.ToString(),
                    OperationField.Of("status", exec.Status), OperationField.Of("duracao_s", exec.DurationSeconds), OperationField.Of("pico_C", exec.PeakTemp)));
            }

        // --- Standalone errors (not tied to an execution), for a fuller Erros report ---------------------
        if (faults.Count > 0 && await db.Errors.CountAsync(ct) < DemoStandaloneErrors)
            for (var i = 0; i < DemoStandaloneErrors; i++)
            {
                var user = users[i % users.Count];
                var error = BuildErrorFor(900 + i, now.AddDays(-(i + 1)).AddHours(-(i % 9)),
                    faults[i % faults.Count], programs[i % programs.Count], user);
                db.Errors.Add(error);
                ops.Add(OpRow(error.At, user, OperationType.Erro, OperationObject.Falha, error.Id.ToString(),
                    OperationField.Of("codigo", error.FaultTypeCode), OperationField.Of("motivo", error.Message)));
            }

        // --- Changes — 188 generated (program create/edit/remove, clustered so the same program is edited
        //     several times, plus config changes) + a 12-row churn history = 200 total. -------------------
        if (!await db.Changes.AnyAsync(ct))
        {
            for (var i = 0; i < DemoChanges; i++)
            {
                var c = BuildChange(i, now, programs, users);
                db.Changes.Add(c);
                ops.Add(OpRowForChange(c));
            }

            var churnProgram = await SeedEditChurnProgramAsync(db, now, ct);
            foreach (var c in BuildEditChurnHistory(churnProgram, now, users))
            {
                db.Changes.Add(c);
                ops.Add(OpRowForChange(c));
            }
        }

        // --- A spread of session/calibration/communication/maintenance audit rows, so every Log de
        //     Operação sub-tab (and operator "Sistema") has content. ---------------------------------------
        if (seedOps)
            ops.AddRange(BuildMiscOperations(now, users));

        if (!await db.SystemLog.AnyAsync(ct))
            for (var i = 0; i < 45; i++)
            {
                var (level, message) = SystemLogMessages[i % SystemLogMessages.Length];
                db.SystemLog.Add(new SystemLogEntry { At = now.AddMinutes(-(45 - i) * 7), Level = level, Message = message });
            }

        // --- A few notifications, with a couple soft-deleted for the Master's Lixeira --------------------
        if (!await db.Notifications.AnyAsync(ct))
            foreach (var n in BuildNotifications(now))
                db.Notifications.Add(n);

        // --- Soft-delete a handful of demo programs + one user for the Master's trash & cleanup view. The two
        //     user states stay distinct (per Lucas): operador2 is Inativo — still visible to the Admin but can't
        //     log in; operador1 is soft-deleted — hidden from the Admin (only the Master's Lixeira) and can't log in.
        var deletedBy = users.FirstOrDefault(u => u.Type == UserType.Master)?.Name ?? admin.Name;
        foreach (var p in programs.Where(p => !p.IsDeleted && p.Id.StartsWith("demo-prog-")).TakeLast(8))
        {
            p.IsDeleted = true;
            p.DeletedAt = now.AddDays(-(Hash(p.Id) % 20 + 1));
            p.DeletedBy = deletedBy;
        }
        var deletedUser = users.FirstOrDefault(u => u.Name == "operador1" && !u.IsDeleted);
        if (deletedUser is not null)
        {
            deletedUser.IsDeleted = true;
            deletedUser.DeletedAt = now.AddDays(-3);
            deletedUser.DeletedBy = deletedBy;
        }

        foreach (var b in await db.Boards.ToListAsync(ct))
            if (b.Hours == 0)
                b.Hours = b.Role == BoardRole.Power ? 842 : 1287;

        if (seedOps && ops.Count > 0)
            db.OperationLog.AddRange(ops);

        await db.SaveChangesAsync(ct);
    }

    // --- Demo programs ----------------------------------------------------------------------------------

    /// <summary>A batch of reflow-shaped demo programs (non-seed, so they show in the gallery), varied by a
    /// deterministic hash of the index. IDs are <c>demo-prog-N</c>.</summary>
    private static IEnumerable<ReflowProgram> BuildDemoPrograms(DateTimeOffset now)
    {
        string[] types = ["SMD", "BGA", "QFN", "Sem Chumbo", "Cura", "Reballing", "Teste", "Pré-aquec.", "Protótipo", "Linha"];
        for (var i = 0; i < DemoProgramCount; i++)
        {
            var peak = 100 + Hash($"dp{i}") % 180;      // 100–279 °C
            var totalSec = 260 + Hash($"ds{i}") % 360;  // 260–619 s
            yield return new ReflowProgram
            {
                Id = $"demo-prog-{i + 1}",
                Name = $"{types[i % types.Length]} {peak}°C #{i + 1}",
                Description = i % 3 == 0 ? $"Perfil de demonstração ({types[i % types.Length]})." : null,
                RunCount = Hash($"dr{i}") % 60,
                LastUsed = null,
                CreatedAt = now.AddDays(-(Hash($"dc{i}") % 120) - 1), // spread over the last ~4 months
                IsSeed = false,
                Profile = DemoProfile(peak, totalSec),
            };
        }
    }

    /// <summary>A reflow curve (8 points) from a peak/duration; the t=0 baseline stays at 0 °C, every other
    /// point is raised to the entry floor — same rule the catalog and editor use.</summary>
    private static List<ProfilePoint> DemoProfile(double peak, double totalSec)
    {
        ProfilePoint P(double frac, double temp) => new()
        {
            T = Math.Round(totalSec * frac),
            Temp = Math.Max(DomainConstants.PointTempMin, Math.Round(temp)),
        };
        return
        [
            new() { T = 0, Temp = 0 },
            P(0.2, peak * 0.55), P(0.42, peak * 0.7), P(0.55, peak * 0.85),
            P(0.62, peak), P(0.72, peak * 0.8), P(0.86, peak * 0.45), P(1, 40),
        ];
    }

    // --- Executions + errors ----------------------------------------------------------------------------

    /// <summary>Builds one execution; when <paramref name="fault"/> is non-null the run failed, and a matching
    /// <see cref="ErrorLogEntry"/> is created and linked (LinkedErrorId/FaultTypeCode/FailureReason) so the
    /// Execução detail deep-links to the Erros report and both render the multi-signal failure chart.</summary>
    private static (ExecutionReport exec, ErrorLogEntry? error) BuildRun(
        int i, DateTimeOffset now, ReflowProgram program, User user, FaultType? fault)
    {
        var failed = fault is not null;
        var startedAt = now.AddDays(-(i % 45 + 1)).AddHours(-(i % 6)).AddMinutes(-(i % 47));
        var profile = program.Profile;
        var total = profile.Count > 0 ? (int)Math.Round(profile[^1].T) : 300;
        var peakTemp = profile.Count > 0 ? (int)Math.Round(profile.Max(p => p.Temp)) : 250;

        var points = new List<ExecProfilePoint>();
        foreach (var p in profile)
            points.Add(new ExecProfilePoint { T = (int)Math.Round(p.T), Temp = (int)Math.Round(p.Temp), Kind = ProfileRole.Programmed });

        var cut = failed ? Math.Max(2, (int)(profile.Count * 0.62)) : profile.Count;
        for (var k = 0; k < cut && k < profile.Count; k++)
        {
            var p = profile[k];
            var drift = Hash($"{i}-m{k}") % 9 - 4;
            points.Add(new ExecProfilePoint { T = (int)Math.Round(p.T), Temp = Math.Max(20, (int)Math.Round(p.Temp) + drift), Kind = ProfileRole.Measured });
        }

        int? faultAtT = null, faultAtTemp = null;
        if (failed && cut > 0)
        {
            var last = profile[cut - 1];
            faultAtT = (int)Math.Round(last.T) + 8;
            faultAtTemp = (int)Math.Round(last.Temp) + 20;
            points.Add(new ExecProfilePoint { T = faultAtT.Value, Temp = faultAtTemp.Value, Kind = ProfileRole.Measured });
        }

        var comparison = new List<ProfileComparisonRow>();
        for (var k = 1; k < profile.Count && comparison.Count < 4; k++)
        {
            var prev = profile[k - 1];
            var cur = profile[k];
            var segSec = (int)Math.Round(cur.T - prev.T);
            comparison.Add(new ProfileComparisonRow
            {
                TempProg = (int)Math.Round(cur.Temp),
                TempReal = (int)Math.Round(cur.Temp) + (Hash($"{i}-c{k}") % 9 - 4),
                TimeProgSeconds = segSec,
                TimeRealSeconds = Math.Max(1, segSec + (Hash($"{i}-s{k}") % 7 - 3)),
                StageIndex = k,
            });
        }

        var duration = failed && faultAtT.HasValue ? faultAtT.Value : total;
        var endedAt = startedAt.AddSeconds(duration);

        ErrorLogEntry? error = null;
        if (failed)
        {
            error = new ErrorLogEntry
            {
                Id = Guid.NewGuid(),
                At = endedAt,
                FaultTypeCode = fault!.Code,
                Severity = fault.Severity,
                Message = fault.Message,
                UserId = user.Id,
                UserName = user.Name,
                ProgramId = program.Id,
                ProgramName = program.Name,
                OvenTemp = faultAtTemp ?? peakTemp,
                PcbTemp = 55 + Hash($"pt{i}") % 35,
                StartAt = startedAt,
                EndAt = endedAt,
                InputVoltage = 120 + Hash($"iv{i}") % 14,
                OutputVoltage = 130 + Hash($"ov{i}") % 60,
                Snapshot = new FailureSnapshot { DurationSec = duration, Series = BuildSnapshotSeries(7 * i + 13, faulted: true) },
                Events =
                [
                    new() { At = startedAt.AddSeconds(duration * 0.5), Kind = LogEventKind.Alerta, Message = "Condição anormal detectada", OrderIndex = 0 },
                    new() { At = endedAt, Kind = LogEventKind.Falha, Message = fault.Message, OrderIndex = 1 },
                ],
            };
        }

        var exec = new ExecutionReport
        {
            Id = Guid.NewGuid(),
            ProgramId = program.Id,
            ProgramName = program.Name,
            UserId = user.Id,
            UserName = user.Name,
            StartedAt = startedAt,
            DurationSeconds = duration,
            Status = failed ? ExecutionStatus.Falha : ExecutionStatus.Concluido,
            PeakTemp = peakTemp,
            PeakCurrent = Math.Round(10m + Hash($"pc{i}") % 90 / 10m, 1),
            FaultAtT = faultAtT,
            FaultAtTemp = faultAtTemp,
            FailureReason = error?.Message,
            FaultTypeCode = error?.FaultTypeCode,
            LinkedErrorId = error?.Id,
            CreatedAt = endedAt,
            Points = points,
            Comparison = comparison,
            // Multi-signal trace so the Execução detail renders a chart — a clean shape for a successful run,
            // a fault signature for a failed one (the same snapshot the Erros report uses).
            Trace = new FailureSnapshot { DurationSec = duration, Series = BuildSnapshotSeries(7 * i + 13, faulted: failed) },
            Events =
            [
                new() { At = startedAt, Kind = LogEventKind.Info, Message = "Execução iniciada", OrderIndex = 0 },
                failed
                    ? new() { At = endedAt, Kind = LogEventKind.Falha, Message = $"Falha: {fault!.Message}", OrderIndex = 1 }
                    : new() { At = endedAt, Kind = LogEventKind.Info, Message = "Execução concluída", OrderIndex = 1 },
            ],
        };
        return (exec, error);
    }

    private static ErrorLogEntry BuildErrorFor(int seed, DateTimeOffset at, FaultType ft, ReflowProgram program, User user) => new()
    {
        Id = Guid.NewGuid(),
        At = at,
        FaultTypeCode = ft.Code,
        Severity = ft.Severity,
        Message = ft.Message,
        UserId = user.Id,
        UserName = user.Name,
        ProgramId = program.Id,
        ProgramName = program.Name,
        OvenTemp = 180 + Hash($"ot{seed}") % 90,
        PcbTemp = 45 + Hash($"pt{seed}") % 40,
        StartAt = at.AddSeconds(-60),
        EndAt = at,
        InputVoltage = 120 + Hash($"iv{seed}") % 14,
        OutputVoltage = Hash($"ov{seed}") % 180,
        Snapshot = new FailureSnapshot { DurationSec = 60, Series = BuildSnapshotSeries(seed) },
        Events =
        [
            new() { At = at.AddSeconds(-30), Kind = LogEventKind.Alerta, Message = "Condição anormal detectada", OrderIndex = 0 },
            new() { At = at, Kind = LogEventKind.Falha, Message = ft.Message, OrderIndex = 1 },
        ],
    };

    /// <summary>
    /// A 60-second multi-signal failure snapshot with realistic, visually-distinct curves (so the chart shows
    /// seven different shapes, not seven parallel straight lines). Each signal has its own characteristic
    /// profile plus a fault signature near the end (over-temp spike, over-current spike, voltage sag, fan
    /// trip); <paramref name="seed"/> varies the ripple phase, where the anomaly starts and how severe it is,
    /// so different errors look different.
    /// </summary>
    private static List<SnapshotSeries> BuildSnapshotSeries(int seed, bool faulted = true)
    {
        double V(int salt) => Hash($"{seed}:{salt}") % 1000 / 1000.0;
        var ph = V(1) * 6.2832;             // ripple phase shift
        var faultPos = 0.70 + 0.20 * V(2);  // where the anomaly kicks in (70–90% of the window)
        var sev = 0.6 + 0.8 * V(3);         // anomaly severity

        double[] Gen(Func<double, double> f) =>
            Enumerable.Range(0, SnapshotSamples).Select(k => Math.Round(f(k / (double)(SnapshotSamples - 1)), 1)).ToArray();
        // A clean (successful) run has no fault signature, so every `* Fault(x)` term vanishes and the base
        // curves (S-curve temp, PWM ripple, fans spinning up) stand alone.
        double Fault(double x) => !faulted || x < faultPos ? 0 : (x - faultPos) / (1 - faultPos); // 0 → 1 after faultPos
        double Ripple(double x, double freq) => Math.Sin(x * freq + ph);

        return
        [
            new() { Name = "Temp. Grelha", Unit = "°C", Color = "#fbbf24",
                Values = Gen(x => 235 + 25 * Math.Tanh(4 * (x - 0.4)) + 35 * sev * Fault(x) + 2 * Ripple(x, 9)) },
            new() { Name = "Temp. Dissipador", Unit = "°C", Color = "#a78bfa",
                Values = Gen(x => 60 + 45 * x * x + 10 * sev * Fault(x)) },
            new() { Name = "Corrente", Unit = "A", Color = "#f87171",
                Values = Gen(x => Math.Max(0, 12 + 3 * Ripple(x, 22) + 11 * sev * Fault(x))) },
            new() { Name = "Tensão", Unit = "V", Color = "#22d3ee",
                Values = Gen(x => 127 + 2 * Ripple(x, 30) - 18 * sev * Fault(x)) },
            new() { Name = "Fan Forno", Unit = "rpm", Color = "#f472b6",
                Values = Gen(x => 2200 + 2600 * Math.Min(1.0, x / faultPos) - 1600 * sev * Fault(x)) },
            new() { Name = "Fan Diss.", Unit = "rpm", Color = "#34d399",
                Values = Gen(x => 2600 + 2100 * x + 150 * Ripple(x, 12)) },
            new() { Name = "Alvo", Unit = "°C", Color = "#93c5fd",
                Values = Gen(x => 245 - 5 * x) },
        ];
    }

    // --- Changes ----------------------------------------------------------------------------------------

    private static ChangeLogEntry BuildChange(int i, DateTimeOffset now, List<ReflowProgram> programs, List<User> users)
    {
        var user = users[i % users.Count];
        var at = now.AddDays(-(i % 60 + 1)).AddHours(-(i % 7)).AddMinutes(-(i % 53));

        // Every 4th row is a system-configuration change (varied bullet sets).
        if (i % 4 == 1)
            return new ChangeLogEntry
            {
                Id = Guid.NewGuid(),
                At = at,
                Action = ChangeAction.Editado,
                Target = "Configuração do sistema",
                UserId = user.Id,
                UserName = user.Name,
                DetailKind = ChangeDetailKind.Config,
                ConfigBullets = [.. ConfigBulletSets[i % ConfigBulletSets.Length]],
            };

        var action = (ChangeAction)(i % 3); // Criado | Editado | Removido
        // Cluster program changes onto a small pool so the same program is edited several times (rich history).
        var program = programs[Hash($"chg{i}") % Math.Min(programs.Count, 40)];
        var curve = program.Profile.Skip(1).Take(8).ToList();

        List<ChangePointRow> Rows(ChangePointRole role) =>
            [.. curve.Select((p, k) => new ChangePointRow
            {
                Index = k + 1,
                Temp = (int)Math.Round(p.Temp),
                TimeSec = (int)Math.Round(p.T),
                Ramp = RampShape.Linear,
                Role = role,
            })];

        // Editado shows a real per-point diff so the Alterações screen exercises every role: point 1
        // unchanged, the middle points changed (previous ~8 °C cooler), the last removed, plus one appended.
        List<ChangePointRow> EditDiff()
        {
            var diff = new List<ChangePointRow>();
            for (var k = 0; k < curve.Count; k++)
            {
                var t = (int)Math.Round(curve[k].T);
                var temp = (int)Math.Round(curve[k].Temp);
                if (k == 0)
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.Unchanged });
                else if (k == curve.Count - 1)
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.Removed });
                else
                {
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = Math.Max(0, temp - 8), TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedBefore });
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedAfter });
                }
            }
            var lastT = curve.Count > 0 ? (int)Math.Round(curve[^1].T) : 0;
            diff.Add(new ChangePointRow { Index = curve.Count + 1, Temp = 60, TimeSec = lastT + 30, Ramp = RampShape.Linear, Role = ChangePointRole.Added });
            return diff;
        }

        var points = action switch
        {
            ChangeAction.Criado => Rows(ChangePointRole.Added),
            ChangeAction.Removido => Rows(ChangePointRole.Removed),
            _ => EditDiff(),
        };

        return new ChangeLogEntry
        {
            Id = Guid.NewGuid(),
            At = at,
            Action = action,
            Target = program.Name,
            UserId = user.Id,
            UserName = user.Name,
            ProgramId = program.Id,
            DetailKind = ChangeDetailKind.Program,
            Points = points,
        };
    }

    /// <summary>Idempotently create the one program whose edit history demonstrates the retention cutoff.</summary>
    private static async Task<ReflowProgram> SeedEditChurnProgramAsync(ReflowDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Programs.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == EditChurnProgramId, ct);
        if (existing is not null) return existing;

        var program = new ReflowProgram
        {
            Id = EditChurnProgramId,
            Name = "Perfil Teste de Edições",
            Description = "Editado várias vezes (demo da retenção do histórico de alterações).",
            RunCount = 0,
            LastUsed = null,
            CreatedAt = now.AddDays(-30), // created at the start of its 30-day edit history
            IsSeed = false,
            Profile =
            [
                new() { T = 0, Temp = 0 },
                new() { T = 90, Temp = 150 },
                new() { T = 180, Temp = 180 },
                new() { T = 210, Temp = 217 },
                new() { T = 240, Temp = 245 },
                new() { T = 270, Temp = 210 },
                new() { T = 330, Temp = 120 },
                new() { T = 390, Temp = 45 },
            ],
        };
        db.Programs.Add(program);
        return program;
    }

    /// <summary>12 change rows for one program — 1 Criado + 11 Editado, oldest→newest, each nudging two
    /// mid-curve points warmer so the per-point diff exercises ChangedBefore/ChangedAfter alongside Unchanged.</summary>
    private static List<ChangeLogEntry> BuildEditChurnHistory(ReflowProgram program, DateTimeOffset now, List<User> users)
    {
        var rows = new List<ChangeLogEntry>();
        var basePts = program.Profile.Skip(1).Take(8).ToList();
        const int edits = 11;

        DateTimeOffset At(int rev) => now.AddDays(-30).AddHours(rev * 6);

        ChangeLogEntry Row(int rev, ChangeAction action, List<ChangePointRow> points) => new()
        {
            Id = Guid.NewGuid(),
            At = At(rev),
            Action = action,
            Target = program.Name,
            UserId = users[rev % users.Count].Id,
            UserName = users[rev % users.Count].Name,
            ProgramId = program.Id,
            DetailKind = ChangeDetailKind.Program,
            Points = points,
        };

        rows.Add(Row(0, ChangeAction.Criado,
            [.. basePts.Select((p, k) => new ChangePointRow
            {
                Index = k + 1,
                Temp = (int)Math.Round(p.Temp),
                TimeSec = (int)Math.Round(p.T),
                Ramp = RampShape.Linear,
                Role = ChangePointRole.Added,
            })]));

        for (var rev = 1; rev <= edits; rev++)
        {
            var diff = new List<ChangePointRow>();
            for (var k = 0; k < basePts.Count; k++)
            {
                var t = (int)Math.Round(basePts[k].T);
                var temp = (int)Math.Round(basePts[k].Temp);
                if (k is 3 or 4)
                {
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedBefore });
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp + rev, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedAfter });
                }
                else
                {
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.Unchanged });
                }
            }
            rows.Add(Row(rev, ChangeAction.Editado, diff));
        }
        return rows;
    }

    // --- Operation log (universal audit trail) ----------------------------------------------------------

    /// <summary>Build one operation-log row. Category is derived from (type, object) exactly like
    /// <c>AuditService.Record</c> does at runtime, so seeded and live rows are categorised identically.</summary>
    private static OperationLogEntry OpRow(
        DateTimeOffset at, User? user, OperationType type, OperationObject obj, string? objectId, params OperationField[] data) => new()
    {
        Id = Guid.NewGuid(),
        At = at,
        OperatorId = user?.Id,
        OperatorName = user?.Name ?? "Sistema",
        Category = OpCategory(type, obj),
        Type = type,
        Object = obj,
        ObjectId = objectId,
        Data = [.. data],
    };

    // Mirror of AuditService.CategoryFor (kept in sync so demo data buckets like the real thing).
    private static OperationCategory OpCategory(OperationType type, OperationObject obj) => type switch
    {
        OperationType.Execucao => OperationCategory.Execucao,
        OperationType.Erro => OperationCategory.Falha, // a board fault feeds the "Falha" sub-tab
        OperationType.Comunicacao => OperationCategory.Comunicacao,
        OperationType.Calibracao => OperationCategory.Calibracao,
        OperationType.Limpeza or OperationType.ResetFabrica => OperationCategory.Manutencao,
        OperationType.Login or OperationType.Logout => OperationCategory.Usuario,
        _ => obj == OperationObject.Usuario ? OperationCategory.Usuario : OperationCategory.Alteracao,
    };

    /// <summary>An operation-log row mirroring a change, linked to the program (or system config) via ObjectId.</summary>
    private static OperationLogEntry OpRowForChange(ChangeLogEntry c)
    {
        var type = c.Action switch
        {
            ChangeAction.Criado => OperationType.Criacao,
            ChangeAction.Removido => OperationType.Remocao,
            _ => OperationType.Alteracao,
        };
        var isConfig = c.DetailKind == ChangeDetailKind.Config;
        var obj = isConfig ? OperationObject.Configuracao : OperationObject.Programa;
        OperationField[] data = isConfig && c.ConfigBullets is { Count: > 0 } bullets
            ? [.. bullets.Select(b => OperationField.Of("configuração", b))]
            : [OperationField.Of("programa", c.Target)];

        return new OperationLogEntry
        {
            Id = Guid.NewGuid(),
            At = c.At,
            OperatorId = c.UserId,
            OperatorName = c.UserName ?? "Sistema",
            Category = OpCategory(type, obj),
            Type = type,
            Object = obj,
            ObjectId = c.ProgramId,
            Data = [.. data],
        };
    }

    /// <summary>Session (login/logout), calibration, communication and maintenance audit rows, so every Log
    /// de Operação sub-tab has content (including the automatic "Sistema" operator).</summary>
    private static IEnumerable<OperationLogEntry> BuildMiscOperations(DateTimeOffset now, List<User> users)
    {
        var rows = new List<OperationLogEntry>();
        for (var i = 0; i < users.Count; i++)
        {
            var u = users[i];
            rows.Add(OpRow(now.AddHours(-(i * 5 + 2)), u, OperationType.Login, OperationObject.Sessao, u.Name, OperationField.Of("papel", u.Type)));
            if (i % 2 == 0)
                rows.Add(OpRow(now.AddHours(-(i * 5 + 1)), u, OperationType.Logout, OperationObject.Sessao, u.Name));
        }
        // A failed login attempt (security audit).
        rows.Add(OpRow(now.AddHours(-9), null, OperationType.Login, OperationObject.Sessao, "desconhecido", OperationField.Of("resultado", "falha")));

        // Calibration (technician).
        rows.Add(OpRow(now.AddDays(-2), null, OperationType.Calibracao, OperationObject.Controlador, null,
            OperationField.Change("currentGain", "100", "102.5"), OperationField.Change("thermoOffset", "0", "-1.5")));

        // Communication — central server flapping + an OTA notice (automatic "Sistema").
        rows.Add(OpRow(now.AddDays(-1).AddHours(-2), null, OperationType.Comunicacao, OperationObject.Controlador, "servidor-central", OperationField.Of("estado", "offline")));
        rows.Add(OpRow(now.AddDays(-1).AddHours(-1), null, OperationType.Comunicacao, OperationObject.Controlador, "servidor-central", OperationField.Of("estado", "online")));
        rows.Add(OpRow(now.AddHours(-6), null, OperationType.Comunicacao, OperationObject.Sistema, "ota", OperationField.Of("versao", "1.4.0")));

        // Maintenance — a cleanup and a factory reset.
        var admin = users.FirstOrDefault(u => u.Type == UserType.Admin);
        rows.Add(OpRow(now.AddDays(-4), admin, OperationType.Limpeza, OperationObject.Configuracao, null,
            OperationField.Of("categorias", "logs, falhas"), OperationField.Of("removidos", 37)));
        return rows;
    }

    private static IEnumerable<Notification> BuildNotifications(DateTimeOffset now)
    {
        (NotificationFeedKind kind, string title, string message, int daysAgo, bool read, bool deleted)[] items =
        [
            (NotificationFeedKind.Warning, "Execução abortada", "'BGA Rework 250°C' foi abortada após 38 s.", 0, false, false),
            (NotificationFeedKind.Error, "Falha na execução", "Sobretemperatura na grelha (E-101).", 1, false, false),
            (NotificationFeedKind.Update, "Atualização disponível", "Nova versão 1.4.0 disponível para instalação.", 1, true, false),
            (NotificationFeedKind.Info, "Servidor central reconectado", "A conexão com o servidor central foi restabelecida.", 2, true, false),
            (NotificationFeedKind.Info, "Execução concluída", "'SMD 270°C' concluída em 410 s.", 3, true, true),
            (NotificationFeedKind.Error, "Espaço em disco crítico", "Espaço livre em disco em 9% (2,9 GB de 32 GB).", 5, true, true),
        ];
        foreach (var n in items)
            yield return new Notification
            {
                Id = Guid.NewGuid(),
                At = now.AddDays(-n.daysAgo),
                Kind = n.kind,
                Title = n.title,
                Message = n.message,
                Read = n.read,
                IsDeleted = n.deleted,
                DeletedAt = n.deleted ? now.AddDays(-n.daysAgo).AddHours(2) : null,
                DeletedBy = n.deleted ? "dev.pandewilly" : null,
            };
    }

    /// <summary>Small stable FNV-1a hash → non-negative int, for deterministic demo variation.</summary>
    private static int Hash(string s)
    {
        uint h = 2166136261;
        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619;
        }
        return (int)(h & 0x7fffffff);
    }
}

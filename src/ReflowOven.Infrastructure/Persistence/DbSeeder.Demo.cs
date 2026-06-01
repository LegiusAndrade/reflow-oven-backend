namespace ReflowOven.Infrastructure.Persistence;

/// <summary>
/// Dev-only demo data: populates the history tables (executions, errors, changes, system log) plus
/// favorites, per-user activity counters and board hour-meters, so the frontend's Relatórios /
/// Diagnóstico / Informação screens have realistic data. Idempotent (skips a table that already
/// has rows). Enabled by config `Seed:Demo` (true in Development); never seed this in production.
/// </summary>
public static partial class DbSeeder
{
    private const int SnapshotSamples = 40;
    private const string EditChurnProgramId = "demo-edit-churn";

    private static readonly string[][] ConfigBulletSets =
    [
        ["Tensão máxima: 110 V → 200 V"],
        ["Corrente máxima: 8 A → 12 A", "Desligamento por sobretemperatura: ativado"],
        ["Limite do dissipador: 75 °C → 85 °C"],
        ["RS422: 57600 → 115200 bps", "Amostragem: 500 ms → 250 ms"],
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

    public static async Task SeedDemoAsync(ReflowDbContext db, IClock clock, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var users = await db.Users.OrderBy(u => u.CreatedAt).ToListAsync(ct);
        var programs = await db.Programs.OrderBy(p => p.Id).ToListAsync(ct);
        var faults = await db.FaultTypes.OrderBy(f => f.Code).ToListAsync(ct);
        if (users.Count == 0 || programs.Count == 0) return;

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
            foreach (var p in programs.Take(6))
                db.Favorites.Add(new FavoriteProgram { UserId = admin.Id, ProgramId = p.Id });

        if (!await db.Executions.AnyAsync(ct))
            for (var i = 0; i < 20; i++)
                db.Executions.Add(BuildExecution(i, now, programs, users));

        if (faults.Count > 0 && !await db.Errors.AnyAsync(ct))
            for (var i = 0; i < 14; i++)
                db.Errors.Add(BuildError(i, now, faults, programs, users));

        // Item 4: one failed execution wired to a real ErrorLogEntry via LinkedErrorId so the Execução
        // detail can deep-link to the Erros report. Idempotent: only when no linked failure exists yet.
        if (faults.Count > 0 && !await db.Executions.AnyAsync(e => e.LinkedErrorId != null, ct))
            db.Executions.Add(BuildLinkedFailureExecution(db, now, programs, users, faults));

        if (!await db.Changes.AnyAsync(ct))
        {
            for (var i = 0; i < 16; i++)
                db.Changes.Add(BuildChange(i, now, programs, users));

            // Item J: one program edited ~12 times so the Alterações report demonstrates the per-program
            // retention cutoff (ChangeRetentionPerProgramMax). Retention is enforced on WRITE only
            // (AuditService.PruneProgramChangesAsync keeps the most recent N), so mirror that here: build
            // 12 rows (1 Criado + 11 Editado) and persist only the most recent N by timestamp — the 2
            // oldest (the Criado + the first Editado) drop out, visibly showing the cutoff.
            var churnProgram = await SeedEditChurnProgramAsync(db, ct);
            var churn = BuildEditChurnHistory(churnProgram, now, users);
            foreach (var c in churn
                         .OrderByDescending(c => c.At)
                         .Take(DomainConstants.ChangeRetentionPerProgramMax))
                db.Changes.Add(c);
        }

        if (!await db.SystemLog.AnyAsync(ct))
            for (var i = 0; i < 45; i++)
            {
                var (level, message) = SystemLogMessages[i % SystemLogMessages.Length];
                db.SystemLog.Add(new SystemLogEntry { At = now.AddMinutes(-(45 - i) * 7), Level = level, Message = message });
            }

        foreach (var b in await db.Boards.ToListAsync(ct))
            if (b.Hours == 0)
                b.Hours = b.Role == BoardRole.Power ? 842 : 1287;

        await db.SaveChangesAsync(ct);
    }

    private static ExecutionReport BuildExecution(int i, DateTimeOffset now, List<ReflowProgram> programs, List<User> users)
    {
        var program = programs[i % programs.Count];
        var user = users[i % users.Count];
        var failed = i % 5 == 4;
        var startedAt = now.AddDays(-(i + 1)).AddHours(-(i % 6));
        var profile = program.Profile;
        var total = profile.Count > 0 ? (int)Math.Round(profile[^1].T) : 300;
        var peakTemp = profile.Count > 0 ? (int)Math.Round(profile.Max(p => p.Temp)) : 250;

        var points = new List<ExecProfilePoint>();
        foreach (var p in profile)
            points.Add(new ExecProfilePoint { T = (int)Math.Round(p.T), Temp = (int)Math.Round(p.Temp), Kind = ProfileRole.Programmed });

        var cut = failed ? Math.Max(2, (int)(profile.Count * 0.62)) : profile.Count;
        for (var k = 0; k < cut; k++)
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
            var dT = Hash($"{i}-c{k}") % 9 - 4;
            var dS = Hash($"{i}-s{k}") % 7 - 3;
            comparison.Add(new ProfileComparisonRow
            {
                TempProg = (int)Math.Round(cur.Temp),
                TempReal = (int)Math.Round(cur.Temp) + dT,
                TimeProgSeconds = segSec,
                TimeRealSeconds = Math.Max(1, segSec + dS),
                StageIndex = k,
            });
        }

        var duration = failed && faultAtT.HasValue ? faultAtT.Value : total;
        var events = new List<LogEvent>
        {
            new() { At = startedAt, Kind = LogEventKind.Info, Message = "Execução iniciada", OrderIndex = 0 },
            failed
                ? new() { At = startedAt.AddSeconds(duration), Kind = LogEventKind.Falha, Message = "Falha durante a execução", OrderIndex = 1 }
                : new() { At = startedAt.AddSeconds(duration), Kind = LogEventKind.Info, Message = "Execução concluída", OrderIndex = 1 },
        };

        return new ExecutionReport
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
            PeakCurrent = Math.Round(10m + Hash($"pc{i}") % 50 / 10m, 1),
            FaultAtT = faultAtT,
            FaultAtTemp = faultAtTemp,
            CreatedAt = startedAt.AddSeconds(duration),
            Points = points,
            Comparison = comparison,
            Events = events,
        };
    }

    private static ErrorLogEntry BuildError(int i, DateTimeOffset now, List<FaultType> faults, List<ReflowProgram> programs, List<User> users)
    {
        var ft = faults[i % faults.Count];
        var program = programs[i % programs.Count];
        var user = users[i % users.Count];
        var at = now.AddDays(-(i + 1)).AddHours(-(i % 9));

        return new ErrorLogEntry
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
            OvenTemp = 180 + Hash($"ot{i}") % 90,
            PcbTemp = 45 + Hash($"pt{i}") % 40,
            StartAt = at.AddSeconds(-60),
            EndAt = at,
            InputVoltage = 120 + Hash($"iv{i}") % 14,
            OutputVoltage = Hash($"ov{i}") % 180,
            Snapshot = new FailureSnapshot { DurationSec = 60, Series = BuildSnapshotSeries() },
            Events =
            [
                new() { At = at.AddSeconds(-30), Kind = LogEventKind.Alerta, Message = "Condição anormal detectada", OrderIndex = 0 },
                new() { At = at, Kind = LogEventKind.Falha, Message = ft.Message, OrderIndex = 1 },
            ],
        };
    }

    private static List<SnapshotSeries> BuildSnapshotSeries()
    {
        double[] Gen(Func<double, double> f) =>
            Enumerable.Range(0, SnapshotSamples).Select(k => Math.Round(f(k / (double)(SnapshotSamples - 1)), 1)).ToArray();

        return
        [
            new() { Name = "Temp. Grelha", Unit = "°C", Color = "#fbbf24", Values = Gen(x => 30 + 240 * x + (x > 0.85 ? 60 * (x - 0.85) / 0.15 : 0)) },
            new() { Name = "Temp. Dissipador", Unit = "°C", Color = "#a78bfa", Values = Gen(x => 35 + 50 * x) },
            new() { Name = "Corrente", Unit = "A", Color = "#f87171", Values = Gen(x => 2 + 12 * x) },
            new() { Name = "Tensão", Unit = "V", Color = "#22d3ee", Values = Gen(x => 30 + 150 * x) },
            new() { Name = "Fan Forno", Unit = "rpm", Color = "#f472b6", Values = Gen(x => 2000 + 3000 * x) },
            new() { Name = "Fan Diss.", Unit = "rpm", Color = "#34d399", Values = Gen(x => 2500 + 2500 * x) },
            new() { Name = "Alvo", Unit = "°C", Color = "#93c5fd", Values = Gen(x => 30 + 230 * x) },
        ];
    }

    /// <summary>Item 4: a failed execution joined to a freshly-created ErrorLogEntry via
    /// LinkedErrorId/FaultTypeCode, so the Execução detail can deep-link to the Erros report.</summary>
    private static ExecutionReport BuildLinkedFailureExecution(
        ReflowDbContext db, DateTimeOffset now, List<ReflowProgram> programs, List<User> users, List<FaultType> faults)
    {
        var program = programs[0];
        var user = users[0];
        var fault = faults.FirstOrDefault(f => f.Severity == ErrorSeverity.Critico) ?? faults[0];
        var started = now.AddHours(-3);
        var faultAtT = program.Profile.Count > 1 ? (int)Math.Round(program.Profile[^1].T) / 2 : 120;
        var peak = program.Profile.Count > 0 ? (int)Math.Round(program.Profile.Max(p => p.Temp)) : 240;

        var error = new ErrorLogEntry
        {
            Id = Guid.NewGuid(),
            At = started.AddSeconds(faultAtT),
            FaultTypeCode = fault.Code,
            Severity = fault.Severity,
            Message = fault.Message,
            UserId = user.Id,
            UserName = user.Name,
            ProgramId = program.Id,
            ProgramName = program.Name,
            OvenTemp = peak,
            PcbTemp = 72,
            StartAt = started,
            EndAt = started.AddSeconds(faultAtT),
            InputVoltage = 127,
            OutputVoltage = 150,
            Snapshot = new FailureSnapshot { DurationSec = 60, Series = BuildSnapshotSeries() },
            Events = [new() { At = started.AddSeconds(faultAtT), Kind = LogEventKind.Falha, Message = fault.Message, OrderIndex = 0 }],
        };
        db.Errors.Add(error);

        // Curve: the programmed points + the measured points up to the fault.
        var points = new List<ExecProfilePoint>();
        foreach (var p in program.Profile)
            points.Add(new ExecProfilePoint { T = (int)Math.Round(p.T), Temp = (int)Math.Round(p.Temp), Kind = ProfileRole.Programmed });
        var cut = Math.Max(2, (int)(program.Profile.Count * 0.62));
        for (var k = 0; k < cut && k < program.Profile.Count; k++)
        {
            var p = program.Profile[k];
            points.Add(new ExecProfilePoint { T = (int)Math.Round(p.T), Temp = Math.Max(20, (int)Math.Round(p.Temp) - 3), Kind = ProfileRole.Measured });
        }

        return new ExecutionReport
        {
            Id = Guid.NewGuid(),
            ProgramId = program.Id,
            ProgramName = program.Name,
            UserId = user.Id,
            UserName = user.Name,
            StartedAt = started,
            DurationSeconds = faultAtT,
            Status = ExecutionStatus.Falha,
            PeakTemp = peak,
            PeakCurrent = 18m,
            FaultAtT = faultAtT,
            FaultAtTemp = peak,
            FailureReason = fault.Message,
            FaultTypeCode = fault.Code,
            LinkedErrorId = error.Id,
            CreatedAt = error.At,
            Points = points,
            Trace = new FailureSnapshot { DurationSec = faultAtT, Series = BuildSnapshotSeries() },
            Events =
            [
                new() { At = started, Kind = LogEventKind.Info, Message = "Execução iniciada", OrderIndex = 0 },
                new() { At = error.At, Kind = LogEventKind.Falha, Message = $"Falha: {fault.Message}", OrderIndex = 1 },
            ],
        };
    }

    /// <summary>Item J: idempotently create the one program whose edit history demonstrates the retention
    /// cutoff. A normal (non-seed, non-deleted) program so it shows in the gallery and the Alterações report.</summary>
    private static async Task<ReflowProgram> SeedEditChurnProgramAsync(ReflowDbContext db, CancellationToken ct)
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
            IsSeed = false,
            Profile =
            [
                new() { T = 0, Temp = 25 },
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

    /// <summary>
    /// Item J: 12 program-change rows for one program — 1 Criado + 11 Editado, oldest→newest, each Editado
    /// nudging two mid-curve points warmer so the per-point diff exercises ChangedBefore/ChangedAfter
    /// alongside Unchanged. Timestamps strictly increase so the most-recent-N retention cut is deterministic.
    /// </summary>
    private static List<ChangeLogEntry> BuildEditChurnHistory(ReflowProgram program, DateTimeOffset now, List<User> users)
    {
        var rows = new List<ChangeLogEntry>();
        var basePts = program.Profile.Skip(1).Take(8).ToList(); // skip the t=0/25 anchor
        const int edits = 11;

        DateTimeOffset At(int rev) => now.AddDays(-30).AddHours(rev * 6); // rev 0 oldest, rev 11 newest

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

        // rev 0: Criado — the whole curve as Added.
        rows.Add(Row(0, ChangeAction.Criado,
            [.. basePts.Select((p, k) => new ChangePointRow
            {
                Index = k + 1,
                Temp = (int)Math.Round(p.Temp),
                TimeSec = (int)Math.Round(p.T),
                Ramp = RampShape.Linear,
                Role = ChangePointRole.Added,
            })]));

        // rev 1..11: Editado — bump points 4 & 5 (peak region) by +rev °C; the rest stay Unchanged.
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

    private static ChangeLogEntry BuildChange(int i, DateTimeOffset now, List<ReflowProgram> programs, List<User> users)
    {
        var user = users[i % users.Count];
        var at = now.AddDays(-(i + 1)).AddHours(-(i % 7));

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
        var program = programs[i % programs.Count];
        var curve = program.Profile.Skip(1).Take(8).ToList();

        List<ChangePointRow> Rows(ChangePointRole role, int tempDelta) =>
            [.. curve.Select((p, k) => new ChangePointRow
            {
                Index = k + 1,
                Temp = Math.Max(0, (int)Math.Round(p.Temp) + tempDelta),
                TimeSec = (int)Math.Round(p.T),
                Ramp = RampShape.Linear,
                Role = role,
            })];

        // Editado shows a real per-point diff so the Alterações screen exercises every role: point 1
        // unchanged, the middle points changed (previous ~8 °C cooler), the last removed, plus one
        // appended (added). Criado/Removido carry the single added/removed curve.
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
            ChangeAction.Criado => Rows(ChangePointRole.Added, 0),
            ChangeAction.Removido => Rows(ChangePointRole.Removed, 0),
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

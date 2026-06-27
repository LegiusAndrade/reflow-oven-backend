using System.Net.NetworkInformation;

namespace ReflowOven.Application.Services;

/// <summary>Diagnóstico backend: statistics & rankings, live sensor readings, self-tests and network ping.</summary>
public sealed class DiagnosticsService(IAppDbContext db, IPowerBoard board, AuditService audit)
{
    public async Task<DiagnosticsOverviewDto> OverviewAsync(int rank = DomainConstants.DiagRankDefault, CancellationToken ct = default)
    {
        rank = Math.Clamp(rank, DomainConstants.DiagRankMin, DomainConstants.DiagRankMax);

        // The dev Master is a hidden superuser — it must never surface in any count or ranking shown to an
        // Admin/Regular (it isn't in the Usuários list either), so every user query here excludes it.
        var visibleUsers = db.Users.Where(u => u.Type != UserType.Master);
        // One pass over Users instead of three separate COUNT scans (active / inactive / admin).
        var userCounts = await visibleUsers.GroupBy(_ => 1).Select(g => new
        {
            Active = g.Count(u => u.Status == UserStatus.Ativo),
            Inactive = g.Count(u => u.Status == UserStatus.Inativo),
            Admins = g.Count(u => u.Type == UserType.Admin),
        }).FirstOrDefaultAsync(ct);
        var stats = new DiagnosticsStatsDto(
            Programs: await db.Programs.CountAsync(ct),
            Executions: await db.Executions.CountAsync(ct),
            Failures: await db.Errors.CountAsync(ct),
            ActiveUsers: userCounts?.Active ?? 0,
            InactiveUsers: userCounts?.Inactive ?? 0,
            Admins: userCounts?.Admins ?? 0);

        var counts = await db.Errors.GroupBy(e => e.FaultTypeCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var faultTypes = await db.FaultTypes.ToListAsync(ct);
        var faults = faultTypes
            .Select(ft => new FaultStatDto(ft.Code, ft.Severity, ft.Message, counts.FirstOrDefault(c => c.Code == ft.Code)?.Count ?? 0))
            .OrderByDescending(f => f.Count).ThenBy(f => f.Code)
            .ToList();

        var topUsers = await visibleUsers
            .OrderByDescending(u => u.LoginCount).ThenBy(u => u.Name).Take(rank)
            .Select(u => new RankedUserDto(u.Id.ToString(), u.Name, u.LoginCount))
            .ToListAsync(ct);

        var topPrograms = await db.Programs
            .OrderByDescending(p => p.RunCount).ThenBy(p => p.Name).Take(rank)
            .Select(p => new RankedProgramDto(p.Id, p.Name, p.RunCount))
            .ToListAsync(ct);

        return new DiagnosticsOverviewDto(stats, faults, topUsers, topPrograms);
    }

    public async Task<SensorReadingsDto> ReadingsAsync(CancellationToken ct = default) =>
        SensorReadingsDto.From(await board.ReadAsync(ct));

    /// <summary>Acknowledge the board's latched protection fault (ACK_FAULT): ask the board to clear its FAULT
    /// latch and record who acknowledged it on the universal operation log (category Falha). Any authenticated
    /// operator may do this — clearing a fault is a panel safety action, not an Admin-only mutation. Returns
    /// the post-ack readings; the authoritative cleared state arrives on the next 1 Hz diagnostics tick (the
    /// board only releases the latch once no critical condition remains).</summary>
    public async Task<SensorReadingsDto> AcknowledgeFaultAsync(CancellationToken ct = default)
    {
        // Capture the currently-latched code (if any) before the ack so the audit row names which fault was
        // acknowledged. ReadAsync returns the cached last status — no extra round-trip on the real board.
        var code = (await board.ReadAsync(ct)).FaultCode;
        await board.AcknowledgeFaultAsync(ct);

        // Logged as OperationType.Erro/OperationObject.Falha so it lands in the "Falha" sub-tab next to the
        // fault it acknowledges; the operator (quem reconheceu) is taken from the current principal by AuditService.
        audit.Record(OperationType.Erro, OperationObject.Falha, code,
            [OperationField.Of("evento", "falha reconhecida"), OperationField.Of("codigo", code ?? "—")]);
        await db.SaveChangesAsync(ct);

        return await ReadingsAsync(ct);
    }

    public async Task<SelfTestResultDto> SelfTestAsync(SelfTestId id, CancellationToken ct = default) =>
        SelfTestResultDto.From(await board.RunSelfTestAsync(id, ct));

    public async Task<PingResultDto> PingAsync(string host, int? port = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ValidationAppException("Informe um host.");
        var target = host.Trim();

        // With a port, probe a TCP connect (handshake latency) — useful to test a specific service/port.
        if (port is { } p)
        {
            if (p is < 1 or > 65535)
                throw new ValidationAppException("Porta inválida (1..65535).");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await tcp.ConnectAsync(target, p, cts.Token);
                sw.Stop();
                return new PingResultDto(tcp.Connected, Math.Round(sw.Elapsed.TotalMilliseconds, 1), target);
            }
            catch
            {
                return new PingResultDto(false, 0, target);
            }
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, TimeSpan.FromSeconds(2));
            return new PingResultDto(reply.Status == IPStatus.Success, reply.RoundtripTime, target);
        }
        catch
        {
            return new PingResultDto(false, 0, target);
        }
    }
}

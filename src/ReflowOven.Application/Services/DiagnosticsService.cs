using System.Net.NetworkInformation;

namespace ReflowOven.Application.Services;

/// <summary>Diagnóstico backend: statistics & rankings, live sensor readings, self-tests and network ping.</summary>
public sealed class DiagnosticsService(IAppDbContext db, IPowerBoard board)
{
    public async Task<DiagnosticsOverviewDto> OverviewAsync(int rank = DomainConstants.DiagRankDefault, CancellationToken ct = default)
    {
        rank = Math.Clamp(rank, DomainConstants.DiagRankMin, DomainConstants.DiagRankMax);

        var stats = new DiagnosticsStatsDto(
            Programs: await db.Programs.CountAsync(ct),
            Executions: await db.Executions.CountAsync(ct),
            Failures: await db.Errors.CountAsync(ct),
            ActiveUsers: await db.Users.CountAsync(u => u.Status == UserStatus.Ativo, ct),
            InactiveUsers: await db.Users.CountAsync(u => u.Status == UserStatus.Inativo, ct),
            Admins: await db.Users.CountAsync(u => u.Type == UserType.Admin, ct));

        var counts = await db.Errors.GroupBy(e => e.FaultTypeCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var faultTypes = await db.FaultTypes.ToListAsync(ct);
        var faults = faultTypes
            .Select(ft => new FaultStatDto(ft.Code, ft.Severity, ft.Message, counts.FirstOrDefault(c => c.Code == ft.Code)?.Count ?? 0))
            .OrderByDescending(f => f.Count).ThenBy(f => f.Code)
            .ToList();

        var topUsers = await db.Users
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

    public async Task<SelfTestResultDto> SelfTestAsync(SelfTestId id, CancellationToken ct = default) =>
        SelfTestResultDto.From(await board.RunSelfTestAsync(id, ct));

    public async Task<PingResultDto> PingAsync(string host, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ValidationAppException("Informe um host.");
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host.Trim(), TimeSpan.FromSeconds(2));
            return new PingResultDto(reply.Status == IPStatus.Success, reply.RoundtripTime, host.Trim());
        }
        catch
        {
            return new PingResultDto(false, 0, host.Trim());
        }
    }
}

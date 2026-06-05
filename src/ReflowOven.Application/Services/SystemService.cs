namespace ReflowOven.Application.Services;

/// <summary>
/// OrangePi system facade: wraps <see cref="ISystemController"/> (OS metrics / network / Wi-Fi / clock /
/// update / power / central-server reachability) and adds the real database size, mapping the domain
/// Platform records to the JSON DTO contract. Nothing here persists state except via the controller's
/// OS-level effects.
/// </summary>
public sealed class SystemService(ISystemController system, IAppDbContext db)
{
    /// <summary>One round-trip aggregate for the Sistema dashboard.</summary>
    public async Task<SystemStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var metricsT = system.GetMetricsAsync(ct);
        var netT = system.GetNetworkStatusAsync(ct);
        var timeT = system.GetTimeStatusAsync(ct);
        var updateT = system.GetUpdateStatusAsync(ct);
        var onlineT = system.PingCentralServerAsync(ct);
        await Task.WhenAll(metricsT, netT, timeT, updateT, onlineT);
        var dbBytes = await db.GetDatabaseSizeBytesAsync(ct);

        return new SystemStatusDto(
            SystemMetricsDto.From(metricsT.Result),
            NetworkStatusDto.From(netT.Result),
            TimeStatusDto.From(timeT.Result),
            UpdateStatusDto.From(updateT.Result),
            onlineT.Result,
            dbBytes);
    }

    public async Task<SystemMetricsDto> MetricsAsync(CancellationToken ct = default) =>
        SystemMetricsDto.From(await system.GetMetricsAsync(ct));

    public async Task<NetworkStatusDto> NetworkAsync(CancellationToken ct = default) =>
        NetworkStatusDto.From(await system.GetNetworkStatusAsync(ct));

    public Task ApplyNetworkAsync(ApplyNetworkRequest req, CancellationToken ct = default)
    {
        ValidateNetwork(req.Ip, req.Mask, req.Gateway, req.DnsPrimary, req.DnsSecondary);
        return system.ApplyNetworkConfigAsync(
            new OsNetworkConfig(req.StaticIp, req.Ip, req.Mask, req.Gateway, req.DnsPrimary, req.DnsSecondary, req.PreferredLink), ct);
    }

    public async Task<IReadOnlyList<WifiNetworkDto>> ScanWifiAsync(CancellationToken ct = default) =>
        (await system.ScanWifiAsync(ct)).Select(WifiNetworkDto.From).ToList();

    public async Task<IReadOnlyList<NetworkInterfaceDto>> InterfacesAsync(CancellationToken ct = default) =>
        (await system.ListInterfacesAsync(ct)).Select(NetworkInterfaceDto.From).ToList();

    public Task SetPriorityInterfaceAsync(SetPriorityInterfaceRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.InterfaceName))
            throw new ValidationAppException("Informe a interface.");
        return system.SetPriorityInterfaceAsync(req.InterfaceName, ct);
    }

    public Task ConnectWifiAsync(ConnectWifiRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Ssid))
            throw new ValidationAppException("Informe a rede Wi-Fi (SSID).");
        if (req.Ssid.Length > DomainConstants.WifiSsidMaxLength)
            throw new ValidationAppException($"SSID excede {DomainConstants.WifiSsidMaxLength} caracteres.");
        if ((req.Password ?? "").Length > DomainConstants.WifiPasswordMaxLength)
            throw new ValidationAppException($"Senha Wi-Fi excede {DomainConstants.WifiPasswordMaxLength} caracteres.");
        return system.ConnectWifiAsync(req.Ssid, req.Password, ct);
    }

    public async Task<TimeStatusDto> TimeAsync(CancellationToken ct = default) =>
        TimeStatusDto.From(await system.GetTimeStatusAsync(ct));

    public Task SetTimeAsync(SetTimeRequest req, CancellationToken ct = default) => system.SetTimeAsync(req.Time, ct);

    public Task SetNtpAsync(SetNtpRequest req, CancellationToken ct = default) => system.SetNtpAsync(req.Enabled, ct);

    public async Task<UpdateStatusDto> UpdateStatusAsync(CancellationToken ct = default) =>
        UpdateStatusDto.From(await system.GetUpdateStatusAsync(ct));

    public Task ApplyUpdateAsync(CancellationToken ct = default) => system.ApplyUpdateAsync(ct);

    public async Task<ConnectivityDto> ConnectivityAsync(CancellationToken ct = default) =>
        new(await system.PingCentralServerAsync(ct));

    /// <summary>
    /// One read-only census of the database — the single source for the startup "auditoria" log and the
    /// <c>/api/system/audit</c> endpoint. Counts every category in one pass of efficient <c>CountAsync</c>
    /// queries (programs over the unfiltered set so soft-deleted/seed rows are visible); empty tables yield 0.
    /// </summary>
    public async Task<DatabaseAuditDto> GetDatabaseAuditAsync(CancellationToken ct = default)
    {
        // Users — total/active/admins in ONE aggregate pass. The dev Master is a hidden superuser: it must
        // never be counted in data an Admin can read (it isn't in the Usuários list either), so it's excluded.
        var visibleUsers = db.Users.Where(u => u.Type != UserType.Master);
        var u = await visibleUsers.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Active = g.Count(x => x.Status == UserStatus.Ativo),
            Admins = g.Count(x => x.Type == UserType.Admin),
        }).FirstOrDefaultAsync(ct);
        var usersTotal = u?.Total ?? 0;
        var usersActive = u?.Active ?? 0;
        var admins = u?.Admins ?? 0;

        // Programs — bypass the soft-delete/seed query filter to count everything, in one pass.
        var p = await db.Programs.IgnoreQueryFilters().GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Deleted = g.Count(x => x.IsDeleted),
            Seed = g.Count(x => x.IsSeed && !x.IsDeleted),
        }).FirstOrDefaultAsync(ct);
        var programsTotal = p?.Total ?? 0;
        var programsDeleted = p?.Deleted ?? 0;
        var programsSeed = p?.Seed ?? 0;
        var programsActive = programsTotal - programsDeleted;
        var programsUser = programsActive - programsSeed;

        // Executions — by terminal status (Concluído / Falha).
        var ex = await db.Executions.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Concluido = g.Count(x => x.Status == ExecutionStatus.Concluido),
            Falha = g.Count(x => x.Status == ExecutionStatus.Falha),
        }).FirstOrDefaultAsync(ct);
        var execTotal = ex?.Total ?? 0;
        var execConcluido = ex?.Concluido ?? 0;
        var execFalha = ex?.Falha ?? 0;

        // Errors / falhas — by severity (Crítico / Alerta / Aviso).
        var er = await db.Errors.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Critico = g.Count(x => x.Severity == ErrorSeverity.Critico),
            Alerta = g.Count(x => x.Severity == ErrorSeverity.Alerta),
            Aviso = g.Count(x => x.Severity == ErrorSeverity.Aviso),
        }).FirstOrDefaultAsync(ct);
        var errTotal = er?.Total ?? 0;
        var errCritico = er?.Critico ?? 0;
        var errAlerta = er?.Alerta ?? 0;
        var errAviso = er?.Aviso ?? 0;

        // Changes + notifications (with the unread tally).
        var changes = await db.Changes.CountAsync(ct);
        var nt = await db.Notifications.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Unread = g.Count(x => !x.Read),
        }).FirstOrDefaultAsync(ct);
        var notifTotal = nt?.Total ?? 0;
        var notifUnread = nt?.Unread ?? 0;

        // System log — by level (INFO / Aviso / Erro). `LogLevel` is the domain enum (see Application GlobalUsings).
        var lg = await db.SystemLog.GroupBy(_ => 1).Select(g => new
        {
            Info = g.Count(x => x.Level == LogLevel.Info),
            Aviso = g.Count(x => x.Level == LogLevel.Aviso),
            Erro = g.Count(x => x.Level == LogLevel.Erro),
        }).FirstOrDefaultAsync(ct);
        var logInfo = lg?.Info ?? 0;
        var logAviso = lg?.Aviso ?? 0;
        var logErro = lg?.Erro ?? 0;

        return new DatabaseAuditDto(
            new UserAuditDto(usersTotal, usersActive, usersTotal - usersActive, admins),
            new ProgramAuditDto(programsTotal, programsActive, programsSeed, programsUser, programsDeleted),
            new ExecutionAuditDto(execTotal, execConcluido, execFalha),
            new ErrorAuditDto(errTotal, errCritico, errAlerta, errAviso),
            changes,
            new NotificationAuditDto(notifTotal, notifUnread),
            new SystemLogAuditDto(logInfo + logAviso + logErro, logInfo, logAviso, logErro));
    }

    public Task RebootAsync(CancellationToken ct = default) => system.RebootAsync(ct);

    public Task ShutdownAsync(CancellationToken ct = default) => system.ShutdownAsync(ct);

    private static void ValidateNetwork(params string?[] fields)
    {
        foreach (var v in fields)
            if ((v ?? "").Length > DomainConstants.NetworkFieldMaxLength)
                throw new ValidationAppException($"Campo de rede excede {DomainConstants.NetworkFieldMaxLength} caracteres.");
    }
}

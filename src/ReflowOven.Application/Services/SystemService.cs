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
        // Users — by status and role.
        var usersTotal = await db.Users.CountAsync(ct);
        var usersActive = await db.Users.CountAsync(u => u.Status == UserStatus.Ativo, ct);
        var admins = await db.Users.CountAsync(u => u.Type == UserType.Admin, ct);

        // Programs — bypass the soft-delete/seed query filter to count everything.
        var allPrograms = db.Programs.IgnoreQueryFilters();
        var programsTotal = await allPrograms.CountAsync(ct);
        var programsDeleted = await allPrograms.CountAsync(p => p.IsDeleted, ct);
        var programsSeed = await allPrograms.CountAsync(p => p.IsSeed && !p.IsDeleted, ct);
        var programsActive = programsTotal - programsDeleted;
        var programsUser = programsActive - programsSeed;

        // Executions — by terminal status (Concluído / Falha).
        var execTotal = await db.Executions.CountAsync(ct);
        var execConcluido = await db.Executions.CountAsync(e => e.Status == ExecutionStatus.Concluido, ct);
        var execFalha = await db.Executions.CountAsync(e => e.Status == ExecutionStatus.Falha, ct);

        // Errors / falhas — by severity (Crítico / Alerta / Aviso).
        var errTotal = await db.Errors.CountAsync(ct);
        var errCritico = await db.Errors.CountAsync(e => e.Severity == ErrorSeverity.Critico, ct);
        var errAlerta = await db.Errors.CountAsync(e => e.Severity == ErrorSeverity.Alerta, ct);
        var errAviso = await db.Errors.CountAsync(e => e.Severity == ErrorSeverity.Aviso, ct);

        // Changes + notifications (with the unread tally).
        var changes = await db.Changes.CountAsync(ct);
        var notifTotal = await db.Notifications.CountAsync(ct);
        var notifUnread = await db.Notifications.CountAsync(n => !n.Read, ct);

        // System log — by level (INFO / Aviso / Erro). `LogLevel` is the domain enum (see Application GlobalUsings).
        var logInfo = await db.SystemLog.CountAsync(l => l.Level == LogLevel.Info, ct);
        var logAviso = await db.SystemLog.CountAsync(l => l.Level == LogLevel.Aviso, ct);
        var logErro = await db.SystemLog.CountAsync(l => l.Level == LogLevel.Erro, ct);

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

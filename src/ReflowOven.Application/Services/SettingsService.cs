namespace ReflowOven.Application.Services;

/// <summary>Reads/updates the Configurações singleton, audits config changes, pushes control config to
/// the board, and applies network changes to the OS (best-effort) via <see cref="ISystemController"/>.</summary>
public sealed class SettingsService(IAppDbContext db, IPowerBoard board, ISystemController system, AuditService audit, ILogger<SettingsService> logger)
{
    public async Task<SettingsDto> GetAsync(CancellationToken ct = default) => Map(await LoadAsync(ct));

    public async Task<SettingsDto> UpdateAsync(SettingsDto dto, CancellationToken ct = default)
    {
        Validate(dto);
        var s = await LoadAsync(ct);

        var bullets = Diff(s, dto);

        // Capture (pre-mutation) whether any OS-level network field changed, so we can re-apply it below.
        var networkChanged =
            s.Network.StaticIp != dto.Network.StaticIp ||
            s.Network.Ip != dto.Network.Ip ||
            s.Network.Mask != dto.Network.Mask ||
            s.Network.Gateway != dto.Network.Gateway ||
            s.Network.DnsPrimary != dto.Network.DnsPrimary ||
            s.Network.DnsSecondary != dto.Network.DnsSecondary;

        s.Pid.P = dto.Pid.P;
        s.Pid.I = dto.Pid.I;
        s.Pid.D = dto.Pid.D;
        s.Oven.MaxTemp = dto.Oven.MaxTemp;
        s.Oven.MaxFanRpm = dto.Oven.MaxFanRpm;
        s.Process.MaxExtraTimeSec = dto.Process.MaxExtraTimeSec;
        s.Voltage.Min = dto.Voltage.Min;
        s.Voltage.Max = dto.Voltage.Max;
        s.Network.Ip = dto.Network.Ip;
        s.Network.Mask = dto.Network.Mask;
        s.Network.Gateway = dto.Network.Gateway;
        s.Network.DnsPrimary = dto.Network.DnsPrimary;
        s.Network.DnsSecondary = dto.Network.DnsSecondary;
        s.Network.StaticIp = dto.Network.StaticIp;

        foreach (var n in dto.Notifications)
        {
            var row = s.Notifications.FirstOrDefault(x => x.Id == n.Id);
            if (row is null) continue; // rows are fixed; ignore unknown ids
            row.Alert = n.Alert;
            row.Process = n.Process;
            row.Buzzer = n.Buzzer;
            row.Sound = n.Sound;
            row.Kind = n.Kind;
        }

        SetSeries(s, RunSignalId.Alvo, dto.Run.Series.Alvo);
        SetSeries(s, RunSignalId.Oven, dto.Run.Series.Oven);
        SetSeries(s, RunSignalId.Board, dto.Run.Series.Board);
        SetSeries(s, RunSignalId.Current, dto.Run.Series.Current);
        SetSeries(s, RunSignalId.Voltage, dto.Run.Series.Voltage);
        SetSeries(s, RunSignalId.OvenFan, dto.Run.Series.OvenFan);
        SetSeries(s, RunSignalId.BoardFan, dto.Run.Series.BoardFan);

        if (bullets.Count > 0) audit.RecordConfigChange(bullets);
        await audit.BumpActivityAsync(Defaults.ActivityLabels[0], ct); // configurações alteradas
        await db.SaveChangesAsync(ct);

        await board.ApplyControlConfigAsync(s, ct);

        // Push the new network config to the OS. Best-effort: the settings are already persisted, so a
        // failed apply (no privileges, no active connection, dev box) is logged but never fails the save.
        if (networkChanged)
        {
            try
            {
                var current = await system.GetNetworkStatusAsync(ct);
                var link = current.Link == NetworkLink.None ? NetworkLink.Cable : current.Link;
                await system.ApplyNetworkConfigAsync(new OsNetworkConfig(
                    s.Network.StaticIp, s.Network.Ip, s.Network.Mask, s.Network.Gateway,
                    s.Network.DnsPrimary, s.Network.DnsSecondary, link), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Configuração de rede salva, mas não foi aplicada no sistema operacional.");
            }
        }

        return Map(s);
    }

    private async Task<Settings> LoadAsync(CancellationToken ct) =>
        // Loads two collections (Notifications + RunSeries); the single-query splitting default is set globally
        // in Infrastructure (AddInfrastructure) — both are tiny fixed config sets, so one query is the cheap choice.
        await db.Settings.Include(x => x.Notifications).Include(x => x.RunSeries).FirstOrDefaultAsync(x => x.Id == 1, ct)
        ?? throw new NotFoundException("Configurações não inicializadas.");

    private static void Validate(SettingsDto d)
    {
        InRange(d.Pid.P, DomainConstants.PidMin, DomainConstants.PidMax, "PID P");
        InRange(d.Pid.I, DomainConstants.PidMin, DomainConstants.PidMax, "PID I");
        InRange(d.Pid.D, DomainConstants.PidMin, DomainConstants.PidMax, "PID D");
        InRange(d.Oven.MaxTemp, DomainConstants.ConfigTempMin, DomainConstants.ConfigTempMax, "Temperatura máxima");
        InRange(d.Oven.MaxFanRpm, DomainConstants.ConfigFanRpmMin, DomainConstants.ConfigFanRpmMax, "RPM máximo");
        InRange(d.Process.MaxExtraTimeSec, DomainConstants.ConfigExtraTimeMin, DomainConstants.ConfigExtraTimeMax, "Tempo extra");
        InRange(d.Voltage.Min, DomainConstants.ConfigVoltageMin, DomainConstants.ConfigVoltageMax, "Tensão mínima");
        InRange(d.Voltage.Max, DomainConstants.ConfigVoltageMin, DomainConstants.ConfigVoltageMax, "Tensão máxima");
        if (d.Voltage.Min > d.Voltage.Max)
            throw new ValidationAppException("Tensão mínima não pode exceder a máxima.");
        foreach (var (label, value) in new[]
                 {
                     ("IP", d.Network.Ip), ("Máscara", d.Network.Mask), ("Gateway", d.Network.Gateway),
                     ("DNS primário", d.Network.DnsPrimary), ("DNS secundário", d.Network.DnsSecondary),
                 })
        {
            if ((value ?? "").Length > DomainConstants.NetworkFieldMaxLength)
                throw new ValidationAppException($"{label} excede {DomainConstants.NetworkFieldMaxLength} caracteres.");
        }
    }

    private static void InRange(double value, double min, double max, string label)
    {
        if (value < min || value > max)
            throw new ValidationAppException($"{label} fora da faixa {min}..{max}.");
    }

    private static List<string> Diff(Settings s, SettingsDto d)
    {
        var b = new List<string>();
        void Num(string label, double oldV, double newV, string unit = "")
        {
            if (Math.Abs(oldV - newV) > 1e-9) b.Add($"{label}: {oldV}{unit} → {newV}{unit}");
        }
        Num("PID P", s.Pid.P, d.Pid.P);
        Num("PID I", s.Pid.I, d.Pid.I);
        Num("PID D", s.Pid.D, d.Pid.D);
        Num("Temperatura máxima do forno", s.Oven.MaxTemp, d.Oven.MaxTemp, " °C");
        Num("RPM máximo", s.Oven.MaxFanRpm, d.Oven.MaxFanRpm, " rpm");
        Num("Tempo extra do processo", s.Process.MaxExtraTimeSec, d.Process.MaxExtraTimeSec, " s");
        Num("Tensão mínima", s.Voltage.Min, d.Voltage.Min, " V");
        Num("Tensão máxima", s.Voltage.Max, d.Voltage.Max, " V");
        if (s.Network.Ip != d.Network.Ip) b.Add($"IP: {s.Network.Ip} → {d.Network.Ip}");
        if (s.Network.StaticIp != d.Network.StaticIp) b.Add($"IP fixo: {(s.Network.StaticIp ? "sim" : "não")} → {(d.Network.StaticIp ? "sim" : "não")}");
        return b;
    }

    private static void SetSeries(Settings s, RunSignalId id, bool visible)
    {
        var row = s.RunSeries.FirstOrDefault(x => x.Signal == id);
        if (row is null)
        {
            row = new RunSeriesPreference { SettingsId = 1, Signal = id };
            s.RunSeries.Add(row);
        }
        row.Visible = visible;
    }

    private static SettingsDto Map(Settings s) => new(
        new PidDto(s.Pid.P, s.Pid.I, s.Pid.D),
        new OvenDto(s.Oven.MaxTemp, s.Oven.MaxFanRpm),
        new ProcessDto(s.Process.MaxExtraTimeSec),
        new VoltageDto(s.Voltage.Min, s.Voltage.Max),
        new NetworkDto(s.Network.Ip, s.Network.Mask, s.Network.Gateway, s.Network.DnsPrimary, s.Network.DnsSecondary, s.Network.StaticIp),
        s.Notifications.OrderBy(n => n.Order).Select(n => new NotificationSettingDto(n.Id, n.Alert, n.Process, n.Buzzer, n.Sound, n.Kind)).ToList(),
        new RunConfigDto(MapSeries(s.RunSeries)));

    private static RunSeriesDto MapSeries(IEnumerable<RunSeriesPreference> rs)
    {
        var list = rs.ToList();
        bool V(RunSignalId id, bool def) => list.FirstOrDefault(x => x.Signal == id)?.Visible ?? def;
        return new RunSeriesDto(
            V(RunSignalId.Alvo, true),
            V(RunSignalId.Oven, true),
            V(RunSignalId.Board, false),
            V(RunSignalId.Current, true),
            V(RunSignalId.Voltage, true),
            V(RunSignalId.OvenFan, false),
            V(RunSignalId.BoardFan, false));
    }
}

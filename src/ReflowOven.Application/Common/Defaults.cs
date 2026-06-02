using System.Globalization;

namespace ReflowOven.Application.Common;

/// <summary>
/// Single source of truth for seed/default data — the C# mirror of the frontend's
/// <c>programs.ts</c> (catalog) and <c>settings.ts</c> (DEFAULT_SETTINGS), plus the fault
/// catalog and the activity labels. Used by both the DbSeeder and the factory-reset path.
/// </summary>
public static class Defaults
{
    /// <summary>The 7 per-user activity counters shown in the user-detail modal.</summary>
    public static readonly string[] ActivityLabels =
    [
        "configurações alteradas",
        "usuários criados",
        "usuários alterados",
        "usuários deletados",
        "programas criados",
        "programas alterados",
        "programas deletados",
    ];

    public const string FactoryProgramId = "factory-default";

    /// <summary>Dev password every seeded user (and the factory-reset admin) gets. Change in production.</summary>
    public const string DefaultDevPassword = "reflow1234";

    // --- Fault catalog (E-101..E-160) -----------------------------------------------------
    public static List<FaultType> FaultTypes() =>
    [
        new() { Code = "E-101", Severity = ErrorSeverity.Critico, Message = "Sobretemperatura na grelha (termopar tipo-K)" },
        new() { Code = "E-102", Severity = ErrorSeverity.Critico, Message = "Falha de leitura do termopar tipo-K" },
        new() { Code = "E-110", Severity = ErrorSeverity.Critico, Message = "Sobrecorrente detectada (sensor Hall)" },
        new() { Code = "E-120", Severity = ErrorSeverity.Alerta, Message = "Tensão de saída fora da faixa (0–180 VDC)" },
        new() { Code = "E-130", Severity = ErrorSeverity.Alerta, Message = "Perda de comunicação RS422 com a placa de potência" },
        new() { Code = "E-140", Severity = ErrorSeverity.Alerta, Message = "Dissipador acima do limite (NTC)" },
        new() { Code = "E-150", Severity = ErrorSeverity.Aviso, Message = "Ventoinha 1 com rotação abaixo do esperado" },
        new() { Code = "E-160", Severity = ErrorSeverity.Aviso, Message = "Subtensão na entrada 127 VAC" },
    ];

    // --- Settings singleton (DEFAULT_SETTINGS) --------------------------------------------
    public static Settings Settings() => new()
    {
        Id = 1,
        Pid = new PidGains { P = 2, I = 0.5, D = 0.1 },
        Oven = new OvenLimits { MaxTemp = 300, MaxFanRpm = 5000 },
        Process = new ProcessConfig { MaxExtraTimeSec = 60 },
        Voltage = new VoltageThresholds { Min = 100, Max = 250 },
        Network = new NetworkConfig
        {
            Ip = "192.168.0.1",
            Mask = "255.255.255.0",
            Gateway = "192.168.0.1",
            DnsPrimary = "8.8.8.8",
            DnsSecondary = "8.8.4.4",
            StaticIp = false,
        },
        Notifications = NotificationSettings(),
        RunSeries = RunSeries(),
    };

    public static List<NotificationSetting> NotificationSettings()
    {
        var list = NotificationRows();
        for (var i = 0; i < list.Count; i++) list[i].Order = i;
        return list;
    }

    private static List<NotificationSetting> NotificationRows() =>
    [
        Notif("fault", "Fault", NotificationProcess.PararProcesso, true, BuzzerSound.Continuo, NotificationKind.Normal),
        Notif("oven-overtemp", "Excesso de temperatura Forno", NotificationProcess.ContinuarProcesso, true, BuzzerSound.Pulsante, NotificationKind.Atencao),
        Notif("board-overtemp", "Excesso de temperatura Placa", NotificationProcess.ContinuarProcesso, false, BuzzerSound.Continuo, NotificationKind.Critica),
        Notif("temp-timeout", "Temperatura não atingida no tempo limite", NotificationProcess.ContinuarProcesso, false, BuzzerSound.Continuo, NotificationKind.Grave),
        Notif("pcb-fan-stopped", "Ventilador PCB parado", NotificationProcess.PararProcesso, true, BuzzerSound.Continuo, NotificationKind.Critica),
        Notif("oven-temp-sensor-fail", "Sensor temperatura do forno com falha", NotificationProcess.PararProcesso, true, BuzzerSound.Continuo, NotificationKind.Grave),
        Notif("board-temp-sensor-fail", "Sensor temperatura da placa com falha", NotificationProcess.PararProcesso, true, BuzzerSound.Continuo, NotificationKind.Grave),
        Notif("mains-undervoltage", "Tensão da rede abaixo do valor estipulado", NotificationProcess.ContinuarProcesso, true, BuzzerSound.Pulsante, NotificationKind.Atencao),
        Notif("mains-overvoltage", "Tensão da rede acima do valor estipulado", NotificationProcess.ContinuarProcesso, true, BuzzerSound.Pulsante, NotificationKind.Atencao),
        Notif("board-supply-undervoltage", "Tensão de alimentação da placa abaixo do valor estipulado", NotificationProcess.PararProcesso, true, BuzzerSound.Continuo, NotificationKind.Critica),
        Notif("board-supply-overvoltage", "Tensão de alimentação da placa acima do valor estipulado", NotificationProcess.PararProcesso, true, BuzzerSound.Continuo, NotificationKind.Critica),
    ];

    private static NotificationSetting Notif(string id, string alert, NotificationProcess process, bool buzzer, BuzzerSound sound, NotificationKind kind) =>
        new() { Id = id, SettingsId = 1, Alert = alert, Process = process, Buzzer = buzzer, Sound = sound, Kind = kind };

    public static List<RunSeriesPreference> RunSeries() =>
    [
        new() { Signal = RunSignalId.Alvo, Visible = true },
        new() { Signal = RunSignalId.Oven, Visible = true },
        new() { Signal = RunSignalId.Board, Visible = false },
        new() { Signal = RunSignalId.Current, Visible = true },
        new() { Signal = RunSignalId.Voltage, Visible = true },
        new() { Signal = RunSignalId.OvenFan, Visible = false },
        new() { Signal = RunSignalId.BoardFan, Visible = false },
    ];

    public static Calibration Calibration() => new()
    {
        Id = 1,
        ThermoOffset = 0,
        CurrentOffset = 0,
        CurrentGain = 100,
        FanPwmMin = 20,
        FanPwmMax = 100,
    };

    public static DeviceInfo DeviceInfo() => new()
    {
        Id = 1,
        FirmwareVersion = "1.0.0",
        HtmlVersion = "1.0.0",
        BackendVersion = "1.0.0",
        BoardIp = "192.168.0.1",
        Os = new OsInfo { Name = "Linux", Kernel = "6.x" },
    };

    public static List<Board> Boards() =>
    [
        new() { Role = BoardRole.Power, Version = "Rev.C", Serial = "PWR-2024-0042", Hours = 0 },
        new() { Role = BoardRole.Control, Version = "Rev.B", Serial = "CTRL-2024-0001", Hours = 0 },
    ];

    // --- Users (dev parity; password is hashed by the seeder) -----------------------------
    public sealed record SeedUser(string Name, string Email, UserType Type, UserStatus Status);

    // Usernames must satisfy DomainConstants.UserNameRegex (letters/digits . _ - only — no spaces/specials).
    public static List<SeedUser> Users() =>
    [
        new("lucas.silva", "lucas@reflow.local", UserType.Admin, UserStatus.Ativo),
        new("vanessa", "vanessa@reflow.local", UserType.Regular, UserStatus.Ativo),
        new("operador1", "op1@reflow.local", UserType.Regular, UserStatus.Ativo),
        new("operador2", "op2@reflow.local", UserType.Regular, UserStatus.Inativo),
    ];

    /// <summary>The single Admin kept after a factory reset.</summary>
    public static SeedUser FactoryAdmin() => new("lucas.silva", "lucas@reflow.local", UserType.Admin, UserStatus.Ativo);

    /// <summary>Builds the single dev <c>Master</c> superuser row from config-bound credentials. Shared by
    /// the first-run seeder and the factory reset so the Master always exists (and survives a reset).
    /// <c>MustChangePassword=false</c>: a dev superuser is never force-expired by the login gate.</summary>
    public static User BuildMasterUser(IMasterCredentials creds, IPasswordHasher hasher, IClock clock) => new()
    {
        Id = Guid.NewGuid(),
        Name = creds.Username,
        Email = creds.Email,
        PasswordHash = hasher.Hash(creds.Password),
        Type = UserType.Master,
        Status = UserStatus.Ativo,
        CreatedAt = clock.UtcNow,
        MustChangePassword = false,
    };

    // --- Programs (factory default + catalog) ---------------------------------------------
    public static ReflowProgram FactoryProgram() => Catalog(
        FactoryProgramId, "Perfil Padrão", 0, null,
        [(0, 25), (90, 150), (180, 180), (210, 217), (240, 245), (270, 210), (330, 120), (390, 45)]);

    /// <summary>The ~50 built-in catalog programs (6 hand-crafted + 44 generated), mirroring programs.ts.</summary>
    public static List<ReflowProgram> CatalogPrograms()
    {
        var list = new List<ReflowProgram>
        {
            Catalog("smd-270", "ReflowOven SMD 270ºC", 23, "01/03/1993",
                [(0, 25), (90, 150), (180, 180), (210, 217), (240, 270), (270, 230), (330, 120), (390, 45)]),
            Catalog("smd-lead-free", "SMD Sem Chumbo 245ºC", 8, "12/04/2026",
                [(0, 25), (90, 150), (180, 175), (225, 217), (255, 245), (285, 210), (345, 110), (400, 45)]),
            Catalog("test-large-board", "Teste Placa Grande", 2, "20/05/2026",
                [(0, 25), (120, 120), (240, 150), (300, 180), (360, 150), (450, 80), (520, 40)]),
            Catalog("adhesive-cure", "Cura de Adesivo 120ºC", 5, "03/05/2026",
                [(0, 25), (60, 80), (150, 120), (300, 120), (380, 60), (440, 35)]),
            Catalog("bga-rework", "BGA Rework 250ºC", 12, "18/05/2026",
                [(0, 25), (90, 150), (180, 200), (230, 235), (260, 250), (290, 215), (360, 120), (420, 50)]),
            Catalog("preheat-90", "Pré-aquecimento 90ºC", 41, "22/05/2026",
                [(0, 25), (120, 90), (300, 90), (400, 45)]),
        };

        string[] types = ["SMD", "BGA", "QFN", "Sem Chumbo", "Cura", "Reballing", "Teste", "Pré-aquec."];
        for (var i = 0; i < 44; i++)
        {
            var peak = 110 + (i * 17) % 170;   // 110–279 °C
            var totalSec = 280 + (i * 53) % 320; // 280–599 s
            var type = types[i % types.Length];
            var day = ((i % 28) + 1).ToString("00");
            var month = ((i % 12) + 1).ToString("00");
            list.Add(Catalog($"gen-{i + 1}", $"{type} {peak}ºC", (i * 7) % 95, $"{day}/{month}/2026", GenProfile(peak, totalSec)));
        }

        return list;
    }

    /// <summary>Reflow-shaped curve derived from a peak/duration (mirrors programs.ts genProfile).</summary>
    private static (double t, double temp)[] GenProfile(double peak, double totalSec)
    {
        (double, double) At(double frac, double temp) => (Math.Round(totalSec * frac), Math.Round(temp));
        return
        [
            (0, 25),
            At(0.2, peak * 0.55),
            At(0.42, peak * 0.7),
            At(0.55, peak * 0.85),
            At(0.62, peak),
            At(0.72, peak * 0.8),
            At(0.86, peak * 0.45),
            At(1, 40),
        ];
    }

    private static ReflowProgram Catalog(string id, string name, int runCount, string? lastUsed, (double t, double temp)[] pts) => new()
    {
        Id = id,
        Name = name,
        RunCount = runCount,
        LastUsed = ParseDate(lastUsed),
        IsSeed = true,
        // Reflow cooldowns dip toward ambient; raise any t>0 point to the entry floor (PointTempMin),
        // leaving the fixed t=0 ambient start (25 °C) untouched so seeds honor the same rule as the editor.
        Profile = pts.Select(p => new ProfilePoint
        {
            T = p.t,
            Temp = p.t > 0 ? Math.Max(p.temp, DomainConstants.PointTempMin) : p.temp,
        }).ToList(),
    };

    private static DateTimeOffset? ParseDate(string? ddMMyyyy)
    {
        if (string.IsNullOrWhiteSpace(ddMMyyyy)) return null;
        return DateTimeOffset.ParseExact(ddMMyyyy, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}

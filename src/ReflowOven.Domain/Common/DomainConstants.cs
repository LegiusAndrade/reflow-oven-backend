namespace ReflowOven.Domain.Common;

/// <summary>
/// Single source of truth for every input/domain limit — the C# mirror of the frontend's
/// <c>src/lib/limits.ts</c>. Keep the two files in lock-step; never hard-code a cap elsewhere.
/// </summary>
public static class DomainConstants
{
    // --- Programs / profiles --------------------------------------------------------------
    public const int ProgramNameMaxLength = 40;
    public const int ProgramDescriptionMaxLength = 120;

    /// <summary>Max number of editable <b>segments</b> (the cap is on segments, not derived points).</summary>
    public const int ProfileMaxPoints = 30;

    public const int PointTempMin = 0;
    public const int PointTempMax = 500;
    public const int PointDurationMin = 0;
    public const int PointDurationMax = 3600;

    /// <summary>Every profile starts at this temperature (°C) at t=0.</summary>
    public const int StartTemp = 25;

    // --- Configurações --------------------------------------------------------------------
    public const double PidMin = 0;
    public const double PidMax = 1000;
    public const int ConfigTempMin = 0;
    public const int ConfigTempMax = 500;
    public const int ConfigFanRpmMin = 0;
    public const int ConfigFanRpmMax = 10000;
    public const int ConfigExtraTimeMin = 0;
    public const int ConfigExtraTimeMax = 3600;
    public const int ConfigVoltageMin = 0;
    public const int ConfigVoltageMax = 300;
    public const int NetworkFieldMaxLength = 15;

    /// <summary>Wi-Fi SSID max length (IEEE 802.11 caps an SSID at 32 bytes).</summary>
    public const int WifiSsidMaxLength = 32;

    /// <summary>WPA-PSK passphrase max length (63 chars; spaces/quotes are valid and not restricted).</summary>
    public const int WifiPasswordMaxLength = 63;

    // --- Users ----------------------------------------------------------------------------
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 40;
    public const int EmailMaxLength = 254;

    /// <summary>Username: Unicode letters/digits and the dot only — no spaces or other specials.</summary>
    public const string UserNameRegex = @"^[\p{L}\p{N}.]+$";

    /// <summary>DB-level (PostgreSQL POSIX) mirror of <see cref="UserNameRegex"/> — alnum + dot, no spaces.</summary>
    public const string UserNameDbCheck = "^[[:alnum:].]+$";

    /// <summary>Password length. Any character is allowed; 72 = BCrypt's effective byte limit.</summary>
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 72;

    /// <summary>A new user must change the emailed password within this many days; after that, each
    /// login resends the reminder email (login is still allowed).</summary>
    public const int PasswordChangeWithinDays = 7;

    /// <summary>Length of the system-generated initial password emailed to a new user.</summary>
    public const int GeneratedPasswordLength = 14;

    /// <summary>Loose email shape mirroring the frontend's isValidEmail.</summary>
    public const string EmailRegex = @"^[^\s@]+@[^\s@]+\.[^\s@]+$";

    // --- Paginação (proteção do serviço) --------------------------------------------------
    /// <summary>Max rows a single Relatórios/log page may return; the server clamps to this even
    /// if the client asks for more (so "me dá 1000 relatórios" never sobrecarrega o serviço).</summary>
    public const int ReportPageSizeMax = 200;

    /// <summary>Max rows a single Programas gallery page may return (server-clamped).</summary>
    public const int ProgramPageSizeMax = 100;

    /// <summary>Max notification-feed rows a single list call returns (server-clamped); the bell keeps
    /// only the most recent.</summary>
    public const int NotificationFeedMax = 200;

    // --- Diagnóstico (rankings) -----------------------------------------------------------
    public const int DiagRankMin = 3;
    public const int DiagRankMax = 10;
    public const int DiagRankDefault = 5;

    // --- Execução (live run) --------------------------------------------------------------
    /// <summary>Max samples kept in the live measured trace; decimated past this.</summary>
    public const int RunMeasuredMaxPoints = 600;

    /// <summary>Live telemetry tick period (ms).</summary>
    public const int RunTickMs = 1000;

    // --- Relatórios (failure snapshot) ----------------------------------------------------
    /// <summary>Fixed sample count of each channel in a stored failure snapshot.</summary>
    public const int SnapshotSamples = 40;

    // --- Calibração -----------------------------------------------------------------------
    public const int CalibThermoOffsetMin = -20;
    public const int CalibThermoOffsetMax = 20;
    public const int CalibCurrentOffsetMin = -5;
    public const int CalibCurrentOffsetMax = 5;
    public const int CalibGainMin = 50;
    public const int CalibGainMax = 150;
    public const int CalibPwmMin = 0;
    public const int CalibPwmMax = 100;
}

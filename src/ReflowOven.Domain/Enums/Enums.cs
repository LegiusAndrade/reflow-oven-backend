using System.Text.Json.Serialization;

namespace ReflowOven.Domain.Enums;

// Enums are stored in PostgreSQL as TEXT and serialized to JSON using the EXACT pt-BR
// literals the Next.js frontend expects (accents, casing and spacing matter — they are part
// of the API contract). C# identifiers can't contain accents/spaces, so the wire value is
// pinned with [JsonStringEnumMemberName]; the EF value converter reads the same attribute
// (see Infrastructure/Persistence/Conversions/PtBrEnumConverter) so DB and JSON stay in sync.

public enum UserType
{
    Admin,
    Regular,
    Master, // dev superuser: inherits Admin + unlocks the Diagnóstico → Log tab (no JsonStringEnumMemberName → wire literal "Master")
    Tecnico, // hidden calibration technician session (no User row): satisfies ONLY CalibrationOnly, never the Admin/Master policies. Wire literal "Tecnico".
}

public enum UserStatus
{
    Ativo,
    Inativo,
}

/// <summary>Ramp shape between two setpoints in the profile editor (frontend <c>Ramp</c>).</summary>
public enum RampShape
{
    Linear,
    Fixo,
    [JsonStringEnumMemberName("Parábola positiva")] ParabolaPositiva,
    [JsonStringEnumMemberName("Parábola negativa")] ParabolaNegativa,
}

public enum RunPhase
{
    Aquecimento,
    Patamar,
    Pico,
    Resfriamento,
}

/// <summary>Signals plotted on the live execution chart.</summary>
public enum RunSignalId
{
    [JsonStringEnumMemberName("alvo")] Alvo,
    [JsonStringEnumMemberName("oven")] Oven,
    [JsonStringEnumMemberName("board")] Board,
    [JsonStringEnumMemberName("current")] Current,
    [JsonStringEnumMemberName("voltage")] Voltage,
    [JsonStringEnumMemberName("ovenFan")] OvenFan,
    [JsonStringEnumMemberName("boardFan")] BoardFan,
}

public enum RunStatus
{
    [JsonStringEnumMemberName("running")] Running,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("aborted")] Aborted,
}

public enum ExecutionStatus
{
    [JsonStringEnumMemberName("Concluído")] Concluido,
    Falha,
    Abortado, // manual stop; no accent so the response wire AND the ?status= filter literal are both "Abortado"
}

public enum LogEventKind
{
    [JsonStringEnumMemberName("info")] Info,
    [JsonStringEnumMemberName("alerta")] Alerta,
    [JsonStringEnumMemberName("falha")] Falha,
}

public enum ErrorSeverity
{
    [JsonStringEnumMemberName("Crítico")] Critico,
    Alerta,
    Aviso,
}

public enum ChangeAction
{
    Criado,
    Editado,
    Removido,
}

public enum ChangeDetailKind
{
    [JsonStringEnumMemberName("config")] Config,
    [JsonStringEnumMemberName("program")] Program,
}

/// <summary>Severity label of a system-log line (frontend <c>LogLevel</c>).</summary>
public enum LogLevel
{
    [JsonStringEnumMemberName("INFO")] Info,
    [JsonStringEnumMemberName("Aviso")] Aviso,
    [JsonStringEnumMemberName("Erro")] Erro,
}

/// <summary>Per-user UI theme preference (frontend contract: lowercase wire literals).</summary>
public enum Theme
{
    [JsonStringEnumMemberName("light")] Light,
    [JsonStringEnumMemberName("dark")] Dark,
    [JsonStringEnumMemberName("system")] System,
}

public enum NotificationKind
{
    Normal,
    [JsonStringEnumMemberName("Atenção")] Atencao,
    [JsonStringEnumMemberName("Crítica")] Critica,
    Grave,
}

public enum NotificationProcess
{
    [JsonStringEnumMemberName("Parar Processo")] PararProcesso,
    [JsonStringEnumMemberName("Continuar Processo")] ContinuarProcesso,
}

public enum BuzzerSound
{
    [JsonStringEnumMemberName("Contínuo")] Continuo,
    Pulsante,
}

public enum SelfTestId
{
    [JsonStringEnumMemberName("fan-oven")] FanOven,
    [JsonStringEnumMemberName("fan-board")] FanBoard,
    [JsonStringEnumMemberName("buzzer")] Buzzer,
    [JsonStringEnumMemberName("rs422")] Rs422,
    [JsonStringEnumMemberName("heater")] Heater,
    [JsonStringEnumMemberName("thermocouple")] Thermocouple,
}

public enum SelfTestState
{
    [JsonStringEnumMemberName("idle")] Idle,
    [JsonStringEnumMemberName("running")] Running,
    [JsonStringEnumMemberName("ok")] Ok,
    [JsonStringEnumMemberName("fail")] Fail,
}

/// <summary>Categories the maintenance "limpar banco" screen can clear (frontend <c>CleanupId</c>).</summary>
public enum CleanupId
{
    [JsonStringEnumMemberName("execucoes")] Execucoes,
    [JsonStringEnumMemberName("alteracoes")] Alteracoes,
    [JsonStringEnumMemberName("falhas")] Falhas,
    [JsonStringEnumMemberName("logs")] Logs,
    [JsonStringEnumMemberName("inativos")] Inativos,
    [JsonStringEnumMemberName("programas")] Programas,
    [JsonStringEnumMemberName("usuarios")] Usuarios,
    [JsonStringEnumMemberName("programas_deletados")] ProgramasDeletados,
    [JsonStringEnumMemberName("usuarios_deletados")] UsuariosDeletados,
}

/// <summary>The coarse CATEGORY of an operation-log row — the front's sub-tab/filter dimension ("Tudo" = no
/// filter). Derived from (type, object) at write time and stored indexed. Accent-free PascalCase wire.</summary>
public enum OperationCategory
{
    Execucao,
    Alteracao,
    Usuario,
    Erro,
    Comunicacao,
    Falha,
    Calibracao,
    Manutencao,
}

/// <summary>The TIPO column of the operation log ("Log de Operação"). The wire literal is the member name
/// (accent-free PascalCase), like <see cref="UserType"/>; the DB stores the same text via PtBrEnumConverter.</summary>
public enum OperationType
{
    Criacao,
    Alteracao,
    Remocao,
    Execucao,
    Erro,
    Comunicacao,
    Login,
    Logout,
    Calibracao,
    Limpeza,
    ResetFabrica,
}

/// <summary>The OBJETO column of the operation log — what the operation acted on.</summary>
public enum OperationObject
{
    Programa,
    Execucao,
    Falha,
    Usuario,
    Configuracao,
    Controlador,
    Sessao,
    Sistema,
}

/// <summary>Whether a stored execution-curve point is the programmed setpoint or the measured value.</summary>
public enum ProfileRole
{
    [JsonStringEnumMemberName("programmed")] Programmed,
    [JsonStringEnumMemberName("measured")] Measured,
}

/// <summary>Role of a row in a program change-diff. An edit emits a per-point diff: a point that changed
/// appears twice (<c>changed-before</c> + <c>changed-after</c>, same index); a point present in only one
/// side is <c>added</c>/<c>removed</c>; a point with the same value on both sides is <c>unchanged</c>
/// (emitted once so the curve stays complete without being flagged as a change).</summary>
public enum ChangePointRole
{
    [JsonStringEnumMemberName("added")] Added,
    [JsonStringEnumMemberName("removed")] Removed,
    [JsonStringEnumMemberName("changed-before")] ChangedBefore,
    [JsonStringEnumMemberName("changed-after")] ChangedAfter,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
}

public enum BoardRole
{
    [JsonStringEnumMemberName("power")] Power,
    [JsonStringEnumMemberName("control")] Control,
}

namespace ReflowOven.Domain.Entities;

/// <summary>
/// A line in the global system log (Diagnóstico → Manutenção), newest-first. Written by the
/// SystemLogCollector and on notable board/run events.
/// </summary>
public class SystemLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = "";
}

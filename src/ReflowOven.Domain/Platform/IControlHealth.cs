namespace ReflowOven.Domain.Platform;

/// <summary>
/// One sample of the CONTROL board's own health — the Pi running the backend, distinct from the power
/// board's protection faults. Each flag is <c>true</c> when that subsystem is healthy. <see cref="BlinkCode"/>
/// is the prioritised fault as a STATUS-LED blip count (the numbers are a stable contract — never renumber
/// an existing condition; a new one appends the next free number).
/// </summary>
public readonly record struct ControlHealthSnapshot(
    bool PowerGood,
    bool PowerLinkUp,
    bool DatabaseUp,
    bool FrontUp,
    bool CentralUp,
    bool DiskOk,
    bool ClockSynced)
{
    /// <summary>A fully-healthy sample (the default before the first health check).</summary>
    public static ControlHealthSnapshot Healthy => new(true, true, true, true, true, true, true);

    /// <summary>Prioritised control-board fault as a STATUS-LED blip count (0 = all OK), most severe first:
    /// 1 power-good lost, 2 power-board link lost, 3 database down, 4 front down, 5 central server down,
    /// 6 disk critical, 7 clock not synchronised.</summary>
    public int BlinkCode =>
        !PowerGood ? 1
        : !PowerLinkUp ? 2
        : !DatabaseUp ? 3
        : !FrontUp ? 4
        : !CentralUp ? 5
        : !DiskOk ? 6
        : !ClockSynced ? 7
        : 0;
}

/// <summary>
/// Holds the latest <see cref="ControlHealthSnapshot"/> (set by the health monitor, read by the STATUS-LED
/// service and anything else that wants the control board's health). Thread-safe singleton.
/// </summary>
public interface IControlHealth
{
    /// <summary>The latest health sample (defaults to <see cref="ControlHealthSnapshot.Healthy"/>).</summary>
    ControlHealthSnapshot Snapshot { get; }

    /// <summary>Publish a fresh health sample.</summary>
    void Set(ControlHealthSnapshot snapshot);

    /// <summary>Prioritised control-board fault as a STATUS-LED blip count (0 = OK) — <see cref="ControlHealthSnapshot.BlinkCode"/>.</summary>
    int CurrentFaultBlinkCode { get; }
}

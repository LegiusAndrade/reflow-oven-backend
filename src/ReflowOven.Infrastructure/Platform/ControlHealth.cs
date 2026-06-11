using ReflowOven.Domain.Platform;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>Thread-safe holder for the latest <see cref="ControlHealthSnapshot"/> (singleton). Starts fully
/// healthy so the STATUS LED shows the OK/run pattern until the first health check runs.</summary>
public sealed class ControlHealth : IControlHealth
{
    private readonly Lock _gate = new();
    private ControlHealthSnapshot _snapshot = ControlHealthSnapshot.Healthy;

    public ControlHealthSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public void Set(ControlHealthSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
    }

    public int CurrentFaultBlinkCode
    {
        get { lock (_gate) return _snapshot.BlinkCode; }
    }
}

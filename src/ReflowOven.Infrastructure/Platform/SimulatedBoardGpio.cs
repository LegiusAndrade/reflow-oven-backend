using ReflowOven.Domain.Platform;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>
/// Default GPIO for a dev box (no real pins): both LED calls are silent no-ops — they fire at the RS422
/// receive rate / the 100 ms status-LED cadence, so logging would spam — and power-good always reads good.
/// Selected unless <c>System:Mode=Linux</c>, mirroring <see cref="SimulatedSystemController"/>.
/// </summary>
public sealed class SimulatedBoardGpio : IBoardGpio
{
    public void ToggleCommLed() { /* no-op on a dev box */ }

    public void SetStatusLed(bool on) { /* no-op on a dev box */ }

    public bool ReadPowerGood() => true;
}

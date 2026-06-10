using Microsoft.Extensions.Logging;
using ReflowOven.Domain.Platform;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>
/// Default GPIO for a dev box (no real pins): the comms toggle is a silent no-op (it fires at the RS422
/// receive rate, so logging would spam), the status LED is logged, and power-good always reads good.
/// Selected unless <c>System:Mode=Linux</c>, mirroring <see cref="SimulatedSystemController"/>.
/// </summary>
public sealed class SimulatedBoardGpio(ILogger<SimulatedBoardGpio> logger) : IBoardGpio
{
    public void ToggleCommLed() { /* no-op on a dev box */ }

    public void SetStatusLed(bool on) => logger.LogDebug("[sim] STATUS LED {State}.", on ? "on" : "off");

    public bool ReadPowerGood() => true;
}

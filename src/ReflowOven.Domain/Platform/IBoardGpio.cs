namespace ReflowOven.Domain.Platform;

/// <summary>
/// The control board's discrete GPIO lines, abstracted like <see cref="ISystemController"/>: the
/// comms-activity LED (toggled on every frame received from the power board), the status LED (device
/// health), and the power-good input. A simulator no-ops on a dev box; the Linux implementation drives
/// the real pins (selected by <c>System:Mode=Linux</c>). Pin numbers come from <c>System</c> config.
/// </summary>
public interface IBoardGpio
{
    /// <summary>Flip the comms LED — called once per valid frame received from the power board, so the LED
    /// blinks at the RS422 receive rate (≈ the 1 Hz status push, faster while commands are exchanged).</summary>
    void ToggleCommLed();

    /// <summary>Drive the status LED (on = device powered and the service is running).</summary>
    void SetStatusLed(bool on);

    /// <summary>Read the power-good input (true = power good). Returns true when GPIO is unavailable.</summary>
    bool ReadPowerGood();
}

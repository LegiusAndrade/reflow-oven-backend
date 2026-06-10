namespace ReflowOven.Infrastructure.Hardware;

/// <summary>
/// Selects the <see cref="IPowerBoard"/> implementation ("Simulated" | "Rs422") and, when
/// <see cref="Mode"/> is "Rs422", configures the serial link to the STM32 power board.
/// </summary>
public sealed class HardwareOptions
{
    public const string Section = "Hardware";

    /// <summary>"Simulated" (default, no hardware) or "Rs422" (real STM32 over the serial port below).</summary>
    public string Mode { get; set; } = "Rs422";

    // --- RS422 serial link (used only when Mode=Rs422) ------------------------------------------
    // The OS exposes the UART as a /dev/tty* device regardless of which GPIO/UART it is wired to;
    // set this to the device the kernel created (e.g. /dev/ttyAMA0, /dev/serial0, /dev/ttyS1).
    // Find it with: `ls -l /dev/serial*` or `dmesg | grep -i tty`.

    /// <summary>Serial device path of the RS422 transceiver UART.</summary>
    public string PortName { get; set; } = "/dev/ttyAMA0";

    /// <summary>Link speed — the protocol runs at 115200 8N1 (must match the firmware).</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>How long to wait for a RESPONSE/NACK to a REQUEST before retrying (ms).</summary>
    public int RequestTimeoutMs { get; set; } = 500;

    /// <summary>Extra REQUEST attempts after the first send (so total tries = 1 + this).</summary>
    public int RequestRetries { get; set; } = 2;

    /// <summary>Send a keep-alive NOTIFY whenever nothing has been sent for this long (ms). Keeps the
    /// power board's 2 s comms watchdog fed (the firmware faults the link without ≥1 frame/s).</summary>
    public int KeepAliveMs { get; set; } = 1000;

    /// <summary>Flag the power board offline (comms-loss fault) when no valid frame arrives for this
    /// long (ms). Mirrors the firmware's 2 s watchdog in the other direction.</summary>
    public int LinkTimeoutMs { get; set; } = 2000;

    /// <summary>Delay between reconnect attempts when the serial port can't be opened / drops (ms).</summary>
    public int ReconnectDelayMs { get; set; } = 2000;
}

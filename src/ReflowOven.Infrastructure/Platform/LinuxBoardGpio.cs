using System.Device.Gpio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Domain.Platform;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>
/// Real control-board GPIO on the Pi (selected by <c>System:Mode=Linux</c>): the comms LED (GPIO13 by
/// default — toggled on each frame from the power board), the status LED (GPIO6 — lit while the service
/// runs) and the power-good input (GPIO19). Pin numbers are BCM/logical and configurable via <c>System</c>.
/// GPIO init is best-effort: if the chip is unavailable the indicators degrade to no-ops and the API keeps
/// running (so a dev box mistakenly set to <c>Linux</c> doesn't crash).
/// </summary>
public sealed class LinuxBoardGpio : IBoardGpio, IDisposable
{
    private readonly ILogger<LinuxBoardGpio> _logger;
    private readonly GpioController? _gpio;
    private readonly int _commPin;
    private readonly int _statusPin;
    private readonly int _powerGoodPin;
    private readonly Lock _gate = new();
    private bool _commOn;

    public LinuxBoardGpio(IOptions<SystemOptions> options, ILogger<LinuxBoardGpio> logger)
    {
        _logger = logger;
        var o = options.Value;
        _commPin = o.CommLedPin;
        _statusPin = o.StatusLedPin;
        _powerGoodPin = o.PowerGoodPin;
        try
        {
            _gpio = new GpioController();
            _gpio.OpenPin(_commPin, PinMode.Output, PinValue.Low);
            _gpio.OpenPin(_statusPin, PinMode.Output, PinValue.High); // status on = device powered + service up
            _gpio.OpenPin(_powerGoodPin, PinMode.Input);
            _logger.LogInformation("GPIO pronto: COMM=GPIO{Comm}, STATUS=GPIO{Status} (on), PWR_GOOD=GPIO{Pg}.",
                _commPin, _statusPin, _powerGoodPin);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPIO indisponível — LEDs/power-good desabilitados (no-op).");
            _gpio = null;
        }
    }

    public void ToggleCommLed()
    {
        if (_gpio is null) return;
        lock (_gate)
        {
            _commOn = !_commOn;
            try { _gpio.Write(_commPin, _commOn ? PinValue.High : PinValue.Low); }
            catch (Exception ex) { _logger.LogDebug(ex, "GPIO: falha ao alternar o LED de comunicação."); }
        }
    }

    public void SetStatusLed(bool on)
    {
        if (_gpio is null) return;
        try { _gpio.Write(_statusPin, on ? PinValue.High : PinValue.Low); }
        catch (Exception ex) { _logger.LogDebug(ex, "GPIO: falha ao acionar o LED de status."); }
    }

    public bool ReadPowerGood()
    {
        if (_gpio is null) return true; // can't read → assume good (don't raise false alarms)
        try { return _gpio.Read(_powerGoodPin) == PinValue.High; }
        catch (Exception ex) { _logger.LogDebug(ex, "GPIO: falha ao ler power-good."); return true; }
    }

    public void Dispose()
    {
        try { _gpio?.Dispose(); } catch { /* ignore */ }
    }
}

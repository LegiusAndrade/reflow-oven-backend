using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Drives the control-board STATUS LED (GPIO6) every 100 ms, mirroring the firmware's LED_STATUS task
/// (../reflow-oven-firmware main.cpp UpdateLeds):
/// <list type="bullet">
/// <item>all OK + idle → one long blink per second (500/500 ms);</item>
/// <item>a control-board fault → the prioritised blink code as N short blips (200/200 ms) then 1 s dark,
///   repeating — count the blips to read the fault number;</item>
/// <item>a run active without fault → 5 Hz flicker (no pauses, so it can't be mistaken for a burst).</item>
/// </list>
/// The pattern restarts whenever the code changes so the first burst already counts cleanly. The blink code
/// is the CONTROL board's prioritised fault from <see cref="IControlHealth"/> (power-good, power-board link,
/// DB, front, central server, disk, clock — NOT the power board's own faults, which show on its own LED);
/// "run active" is the control board's run state. No-op under the simulator (the GPIO is a no-op).
/// </summary>
public sealed class StatusLedService(
    IControlHealth health,
    IRunManager runManager,
    IBoardGpio gpio,
    ILogger<StatusLedService> logger) : BackgroundService
{
    // Pattern timing in 100 ms ticks (matches the firmware UpdateLeds constants).
    private const uint BlipOnTicks = 2;     // 200 ms blip ON (~3x the OK blink rate)
    private const uint BlipOffTicks = 2;    // 200 ms gap between blips inside a burst
    private const uint CodePauseTicks = 10; // 1 s dark between bursts so the count restarts unambiguously

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var shownCode = 0;
        uint tick = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var code = health.CurrentFaultBlinkCode;
                if (code != shownCode) { shownCode = code; tick = 0; } // restart so the first burst counts

                bool on;
                if (shownCode > 0) // fault: N short blips, then a long dark pause, repeating
                {
                    var blip = BlipOnTicks + BlipOffTicks;            // 400 ms per blip
                    var burst = (uint)shownCode * blip;
                    var pos = tick % (burst + CodePauseTicks);
                    on = pos < burst && pos % blip < BlipOnTicks;
                }
                else
                {
                    on = runManager.IsRunning ? tick % 2 == 0         // 5 Hz flicker during a run
                                              : tick % 10 < 5;         // 1 Hz heartbeat when idle
                }
                gpio.SetStatusLed(on);
                tick++;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — the stopping token cancels WaitForNextTickAsync. Leave the LED as-is.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no serviço do LED de status.");
        }
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Drives the 1 Hz control tick: advances the active run (the RunManager reads the board and
/// pushes the run trace) or, when idle, publishes a standalone sensor reading for the Diagnóstico
/// screen. The real-vs-simulated board is hidden behind <see cref="IPowerBoard"/>.
/// </summary>
public sealed class RunControlLoopService(
    IRunManager runManager,
    IPowerBoard board,
    ITelemetrySink sink,
    ILogger<RunControlLoopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(DomainConstants.RunTickMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (runManager.GetStatus() is { Status: RunStatus.Running })
                {
                    await runManager.TickAsync(stoppingToken);
                }
                else
                {
                    var reading = await board.ReadAsync(stoppingToken);
                    await sink.PublishReadingAsync(reading);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no loop de controle da execução.");
            }
        }
    }
}

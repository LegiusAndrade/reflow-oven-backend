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
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (runManager.IsRunning)
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
                    throw; // shutdown: bubble to the outer handler so the loop ends cleanly
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falha no loop de controle da execução.");

                    // Don't leave a half-ticked run stuck as "Running" forever (which would make every
                    // future start fail with "Já existe uma execução em andamento"). Abort it cleanly.
                    if (runManager.IsRunning)
                    {
                        try
                        {
                            await runManager.StopAsync(stoppingToken);
                            logger.LogWarning("Execução ativa abortada após falha no loop de controle.");
                        }
                        catch (Exception stopEx)
                        {
                            logger.LogError(stopEx, "Não foi possível abortar a execução após a falha no loop.");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — the stopping token cancels a tick or WaitForNextTickAsync. Swallow it
            // so it isn't surfaced as unhandled (the debugger was flagging this) and the loop stops cleanly.
        }
    }
}

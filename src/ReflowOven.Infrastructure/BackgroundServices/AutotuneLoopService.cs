using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Two RS422 coordination duties the run loop does not own:
///  (1) answers the power board's boot config pull (GET_CONFIGURATION, 0x0E) by pushing the current control
///      config — until it gets one the board stays in a safe state and refuses to heat;
///  (2) polls the active PID auto-tune ~1 Hz (<see cref="IAutotuneManager.TickAsync"/>) and finalizes it.
/// Kept separate from <c>RunControlLoopService</c> so the run path stays untouched.
/// </summary>
public sealed class AutotuneLoopService(
    IAutotuneManager autotune,
    IPowerBoard board,
    IServiceScopeFactory scopeFactory,
    ILogger<AutotuneLoopService> logger) : BackgroundService
{
    private int _configRequested;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        board.ConfigRequested += OnConfigRequested;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(DomainConstants.RunTickMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (Interlocked.Exchange(ref _configRequested, 0) == 1)
                        await PushConfigAsync(stoppingToken);
                    if (autotune.IsActive)
                        await autotune.TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Falha no loop de autotune/config."); }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        finally { board.ConfigRequested -= OnConfigRequested; }
    }

    // Runs on the RS422 RX thread — just flag it; the 1 Hz loop does the DB read + push off that thread.
    private void OnConfigRequested(object? sender, EventArgs e) => Interlocked.Exchange(ref _configRequested, 1);

    private async Task PushConfigAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) { logger.LogWarning("RS422: a placa pediu config (0x0E), mas Settings não está inicializado."); return; }
        await board.ApplyControlConfigAsync(settings, ct);
        logger.LogInformation("RS422: config enviada à placa em resposta ao GET_CONFIGURATION (boot pull).");
    }
}

using Merlin.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Services.Metrics;

public sealed class MetricsBackgroundService(
    ISystemMetricsCollector collector,
    MetricsHistory history,
    IHubContext<MetricsHub> hubContext,
    ILogger<MetricsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Metrics collection started (1s interval)");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        // Initial collection to populate previous-sample state
        try
        {
            await collector.CollectAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Initial metrics collection failed");
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var snapshot = await collector.CollectAsync(stoppingToken);
                history.Add(snapshot);
                await hubContext.Clients.All.SendAsync("SystemMetrics", snapshot, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Metrics collection failed");
            }
        }
    }
}

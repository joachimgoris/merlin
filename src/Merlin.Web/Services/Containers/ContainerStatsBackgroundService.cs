using Merlin.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Services.Containers;

public sealed class ContainerStatsBackgroundService(
    IContainerService containerService,
    IHubContext<MetricsHub> hubContext,
    ILogger<ContainerStatsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Container stats collection started (2s interval)");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var containers = await containerService.ListContainersAsync(stoppingToken);
                var stats = await containerService.GetAllStatsAsync(stoppingToken);

                await hubContext.Clients.All.SendAsync("ContainerList", containers, stoppingToken);
                await hubContext.Clients.All.SendAsync("ContainerStats", stats, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Container stats collection failed");
            }
        }
    }
}

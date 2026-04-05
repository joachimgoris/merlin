using Merlin.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Services.Containers;

public sealed class ContainerStatsBackgroundService(
    IContainerService containerService,
    ContainerMetricsHistory metricsHistory,
    IHubContext<MetricsHub> hubContext,
    ILogger<ContainerStatsBackgroundService> logger) : BackgroundService
{
    private int _tickCount;

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

                foreach (var stat in stats)
                {
                    metricsHistory.Record(stat.ContainerId, stat.CpuPercent, stat.MemoryPercent);
                }

                await hubContext.Clients.All.SendAsync("ContainerList", containers, stoppingToken);
                await hubContext.Clients.All.SendAsync("ContainerStats", stats, stoppingToken);

                var sparklineData = BuildSparklinePayload();
                await hubContext.Clients.All.SendAsync("ContainerSparklines", sparklineData, stoppingToken);

                // Prune stale containers every 30 ticks (~60s)
                if (++_tickCount % 30 == 0)
                {
                    metricsHistory.PruneStale();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Container stats collection failed");
            }
        }
    }

    private Dictionary<string, object> BuildSparklinePayload()
    {
        var allHistory = metricsHistory.GetAllHistory();
        var payload = new Dictionary<string, object>(allHistory.Count);

        foreach (var (containerId, snapshots) in allHistory)
        {
            var cpu = new double[snapshots.Count];
            var mem = new double[snapshots.Count];

            for (var i = 0; i < snapshots.Count; i++)
            {
                cpu[i] = Math.Round(snapshots[i].CpuPercent, 2);
                mem[i] = Math.Round(snapshots[i].MemoryPercent, 2);
            }

            payload[containerId] = new { cpu, mem };
        }

        return payload;
    }
}

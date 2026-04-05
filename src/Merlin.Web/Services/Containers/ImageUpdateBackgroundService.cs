using System.Collections.Concurrent;
using Merlin.Web.Hubs;
using Merlin.Web.Models;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Services.Containers;

public sealed class ImageUpdateBackgroundService(
    IContainerService containerService,
    ImageUpdateChecker updateChecker,
    IHubContext<MetricsHub> hubContext,
    ILogger<ImageUpdateBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, ImageUpdateStatus> _latestResults = new();

    public IReadOnlyDictionary<string, ImageUpdateStatus> LatestResults => _latestResults;

    public async Task ForceCheckAsync(CancellationToken ct)
    {
        updateChecker.ClearCache();
        await RunCheckAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Image update check service started ({Interval} interval)", CheckInterval);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Initial check
        await RunCheckAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCheckAsync(stoppingToken);
        }
    }

    private async Task RunCheckAsync(CancellationToken ct)
    {
        try
        {
            var containers = await containerService.ListContainersAsync(ct);
            var running = containers.Where(c => c.State == "running").ToList();

            if (running.Count == 0)
            {
                _latestResults.Clear();
                await hubContext.Clients.All.SendAsync("ImageUpdates", Array.Empty<ImageUpdateStatus>(), ct);
                return;
            }

            var results = await updateChecker.CheckUpdatesAsync(running, ct);

            _latestResults.Clear();
            foreach (var result in results)
            {
                _latestResults[result.ImageReference] = result;
            }

            await hubContext.Clients.All.SendAsync("ImageUpdates", results, ct);
            logger.LogDebug("Image update check completed: {Count} images checked", results.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Image update check failed");
        }
    }
}

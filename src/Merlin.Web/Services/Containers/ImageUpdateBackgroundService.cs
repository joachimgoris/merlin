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
    private static readonly TimeSpan ContainerChangeCheckInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, ImageUpdateStatus> _latestResults = new();
    private readonly ConcurrentDictionary<string, string> _knownImageIds = new();

    public IReadOnlyDictionary<string, ImageUpdateStatus> LatestResults => _latestResults;

    public async Task ForceCheckAsync(CancellationToken cancellationToken)
    {
        updateChecker.ClearCache();
        await RunCheckAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Image update check service started ({Interval} interval)", CheckInterval);

        try
        {
            await Task.Delay(StartupDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Initial check
        await RunCheckAsync(cancellationToken);

        // Check every 30s if containers changed (image pulled + recreated), full re-check every 15 min
        var lastFullCheck = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(ContainerChangeCheckInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (DateTimeOffset.UtcNow - lastFullCheck >= CheckInterval)
            {
                await RunCheckAsync(cancellationToken);
                lastFullCheck = DateTimeOffset.UtcNow;
            }
            else
            {
                await CheckForContainerChangesAsync(cancellationToken);
            }
        }
    }

    private async Task CheckForContainerChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await containerService.ListContainersAsync(cancellationToken);
            var changed = false;

            foreach (var c in containers.Where(c => c.State == "running"))
            {
                if (_knownImageIds.TryGetValue(c.Image, out var knownId) && knownId != c.ImageId)
                {
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                logger.LogInformation("Container image change detected, re-checking updates");
                updateChecker.ClearCache();
                await RunCheckAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Container change detection failed");
        }
    }

    private async Task RunCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await containerService.ListContainersAsync(cancellationToken);
            var running = containers.Where(c => c.State == "running").ToList();

            if (running.Count == 0)
            {
                _latestResults.Clear();
                await hubContext.Clients.All.SendAsync("ImageUpdates", Array.Empty<ImageUpdateStatus>(), cancellationToken);
                return;
            }

            // Track current image IDs for change detection
            _knownImageIds.Clear();
            foreach (var c in running)
            {
                _knownImageIds[c.Image] = c.ImageId;
            }

            var results = await updateChecker.CheckUpdatesAsync(running, cancellationToken);

            _latestResults.Clear();
            foreach (var result in results)
            {
                _latestResults[result.ImageReference] = result;
            }

            await hubContext.Clients.All.SendAsync("ImageUpdates", results, cancellationToken);
            logger.LogDebug("Image update check completed: {Count} images checked", results.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Image update check failed");
        }
    }
}

using Merlin.Web.Services.Containers;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Hubs;

public sealed class MetricsHub(
    IContainerService containerService,
    ILogger<MetricsHub> logger) : Hub
{
    public async Task StartContainer(string id)
    {
        try
        {
            await containerService.StartAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start container {Id}", id);
            throw new HubException("Failed to start container.");
        }
    }

    public async Task StopContainer(string id)
    {
        try
        {
            await containerService.StopAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop container {Id}", id);
            throw new HubException("Failed to stop container.");
        }
    }

    public async Task RestartContainer(string id)
    {
        try
        {
            await containerService.RestartAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to restart container {Id}", id);
            throw new HubException("Failed to restart container.");
        }
    }

    public async IAsyncEnumerable<string> StreamContainerLogs(
        string containerId,
        int tail = 100,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        tail = Math.Clamp(tail, 1, 5000);

        await foreach (var line in containerService.StreamLogsAsync(containerId, tail, ct))
        {
            yield return line;
        }
    }
}

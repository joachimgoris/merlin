using Merlin.Web.Services.Containers;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Hubs;

public sealed class MetricsHub(IContainerService containerService) : Hub
{
    public async Task StartContainer(string id)
    {
        await containerService.StartAsync(id);
    }

    public async Task StopContainer(string id)
    {
        await containerService.StopAsync(id);
    }

    public async Task RestartContainer(string id)
    {
        await containerService.RestartAsync(id);
    }

    public async IAsyncEnumerable<string> StreamContainerLogs(
        string containerId,
        int tail = 100,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var line in containerService.StreamLogsAsync(containerId, tail, ct))
        {
            yield return line;
        }
    }
}

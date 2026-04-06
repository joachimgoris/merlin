using Merlin.Web.Models;

namespace Merlin.Web.Services.Containers;

public interface IContainerService
{
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContainerStats>> GetAllStatsAsync(CancellationToken ct = default);
    Task StartAsync(string id, CancellationToken ct = default);
    Task StopAsync(string id, CancellationToken ct = default);
    Task RestartAsync(string id, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamLogsAsync(string id, int tail = 100, CancellationToken ct = default);
}

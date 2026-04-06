using Merlin.Web.Models;

namespace Merlin.Web.Services.Containers;

public interface IContainerService
{
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContainerStats>> GetAllStatsAsync(CancellationToken cancellationToken = default);
    Task StartAsync(string id, CancellationToken cancellationToken = default);
    Task StopAsync(string id, CancellationToken cancellationToken = default);
    Task RestartAsync(string id, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamLogsAsync(string id, int tail = 100, CancellationToken cancellationToken = default);
}

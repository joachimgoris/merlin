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
    Task<string> CreateExecAsync(string containerId, string[] command, CancellationToken ct = default);
    Task<Stream> StartExecAsync(string execId, CancellationToken ct = default);
    Task ResizeExecAsync(string execId, int cols, int rows, CancellationToken ct = default);
}

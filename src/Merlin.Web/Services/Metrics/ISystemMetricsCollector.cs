using Merlin.Web.Models;

namespace Merlin.Web.Services.Metrics;

public interface ISystemMetricsCollector
{
    Task<SystemMetrics> CollectAsync(CancellationToken cancellationToken = default);
}

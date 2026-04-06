using Merlin.Web.Services.Metrics;

namespace Merlin.Web.Services.Persistence;

public sealed class MetricsFlushService(
    MetricsHistory history,
    MetricsRepository repository,
    TimeSpan retentionPeriod,
    ILogger<MetricsFlushService> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);
    private const int BatchSize = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Metrics flush service started (interval: {Interval}s, retention: {Retention} days)",
            FlushInterval.TotalSeconds,
            retentionPeriod.TotalDays);

        using var timer = new PeriodicTimer(FlushInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Metrics flush failed");
            }
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var latest = history.GetLatest(BatchSize);
        if (latest.Count == 0) return;

        var latestStored = await repository.GetLatestTimestampAsync(cancellationToken);

        var newEntries = latestStored.HasValue
            ? latest.Where(m => m.Timestamp > latestStored.Value).ToList()
            : [.. latest];

        if (newEntries.Count > 0)
        {
            await repository.InsertBatchAsync(newEntries, cancellationToken);
            logger.LogDebug("Flushed {Count} metrics snapshots to SQLite", newEntries.Count);
        }

        await repository.PruneAsync(retentionPeriod, cancellationToken);
    }
}

using Merlin.Web.Services.Containers;
using Merlin.Web.Services.Metrics;

namespace Merlin.Web.Services.Alerts;

public sealed class AlertBackgroundService(
    AlertEvaluator evaluator,
    DiscordWebhookClient webhookClient,
    MetricsHistory metricsHistory,
    IContainerService containerService,
    SystemInfoCollector systemInfoCollector,
    ILogger<AlertBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Alert evaluation started (10s interval)");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var metrics = metricsHistory.Latest;

                var containers = await containerService.ListContainersAsync(stoppingToken);

                var alerts = evaluator.Evaluate(metrics, containers);

                if (alerts.Count == 0)
                {
                    continue;
                }

                var systemInfo = await systemInfoCollector.GetAsync(stoppingToken);
                var hostname = string.IsNullOrEmpty(systemInfo.Hostname)
                    ? "unknown"
                    : systemInfo.Hostname;

                foreach (var alert in alerts)
                {
                    await webhookClient.SendAlertAsync(alert, hostname, stoppingToken);

                    // Respect Discord rate limits
                    if (alerts.Count > 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Alert evaluation failed");
            }
        }
    }
}

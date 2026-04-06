using System.Text;
using System.Text.Json;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Alerts;

public sealed class DiscordWebhookClient(
    HttpClient httpClient,
    AlertOptions options,
    ILogger<DiscordWebhookClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task SendAlertAsync(Alert alert, string hostname, CancellationToken cancellationToken)
    {
        try
        {
            var color = alert.Severity switch
            {
                AlertSeverity.Critical => 0xFF4444,
                AlertSeverity.Warning => 0xFFAA00,
                AlertSeverity.Info => 0x4488FF,
                _ => 0x4488FF
            };

            var title = alert.Type switch
            {
                AlertType.CpuHigh => "CPU High",
                AlertType.MemHigh => "Memory High",
                AlertType.DiskHigh => "Disk High",
                AlertType.ContainerStopped => "Container Stopped",
                AlertType.ContainerRecovered => "Container Recovered",
                AlertType.ContainerUnhealthy => "Container Unhealthy",
                _ => alert.Type.ToString()
            };

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title,
                        description = alert.Message,
                        color,
                        fields = new[]
                        {
                            new { name = "Host", value = hostname, inline = true }
                        },
                        timestamp = alert.Timestamp.ToString("o"),
                        footer = new { text = "Merlin Monitoring" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(options.WebhookUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Discord webhook returned {StatusCode} for alert {AlertType}:{Subject}",
                    response.StatusCode, alert.Type, alert.Subject);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to send Discord alert {AlertType}:{Subject}",
                alert.Type, alert.Subject);
        }
    }
}

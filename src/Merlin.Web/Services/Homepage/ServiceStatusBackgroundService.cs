using Merlin.Web.Hubs;
using Merlin.Web.Models;
using Merlin.Web.Services.Containers;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Services.Homepage;

public sealed class ServiceStatusBackgroundService(
    ServiceDiscovery serviceDiscovery,
    IContainerService containerService,
    IHttpClientFactory httpClientFactory,
    IHubContext<MetricsHub> hubContext,
    ILogger<ServiceStatusBackgroundService> logger) : BackgroundService
{
    private volatile IReadOnlyList<HomepageService> _currentServices = [];

    public IReadOnlyList<HomepageService> CurrentServices => _currentServices;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Homepage service status monitor started (60s interval)");

        // Run once immediately, then on timer
        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await containerService.ListContainersAsync(cancellationToken);
            var services = serviceDiscovery.DiscoverServices(containers);

            var healthCheckedServices = await HealthCheckAllAsync(services, cancellationToken);

            _currentServices = healthCheckedServices;

            await hubContext.Clients.All.SendAsync("HomepageServices", healthCheckedServices, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Homepage service status check failed");
        }
    }

    private async Task<List<HomepageService>> HealthCheckAllAsync(
        List<HomepageService> services,
        CancellationToken cancellationToken)
    {
        var tasks = services.Select(service => HealthCheckOneAsync(service, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return [.. results];
    }

    private async Task<HomepageService> HealthCheckOneAsync(HomepageService service, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("HomepageHealthCheck");
            using var response = await client.GetAsync(service.Url, cancellationToken);
            var status = response.IsSuccessStatusCode ? "online" : "offline";
            return service with { Status = status };
        }
        catch
        {
            return service with { Status = "offline" };
        }
    }
}

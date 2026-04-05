using Merlin.Web.Hubs;
using Merlin.Web.Services.Containers;
using Merlin.Web.Services.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Configuration
var hostProcPath = builder.Configuration["HOST_PROC_PATH"] ?? "/proc";
var hostSysPath = builder.Configuration["HOST_SYS_PATH"] ?? "/sys";
var hostRootPath = builder.Configuration["HOST_ROOT_PATH"];
var podmanSocketPath = builder.Configuration["PODMAN_SOCKET_PATH"] ?? "/var/run/podman/podman.sock";

// Fall back to real /proc and /sys on Linux dev machines
if (!Directory.Exists(hostProcPath) && Directory.Exists("/proc"))
    hostProcPath = "/proc";
if (!Directory.Exists(hostSysPath) && Directory.Exists("/sys"))
    hostSysPath = "/sys";

builder.Services.AddSingleton(new MetricsCollectorOptions(hostProcPath, hostSysPath, hostRootPath));
builder.Services.AddSingleton<ISystemMetricsCollector, LinuxMetricsCollector>();
builder.Services.AddSingleton<ProcessCollector>();
builder.Services.AddSingleton<MetricsHistory>();
builder.Services.AddHostedService<MetricsBackgroundService>();

builder.Services.AddSingleton(new ContainerServiceOptions(podmanSocketPath));
builder.Services.AddSingleton<IContainerService, PodmanContainerService>();
builder.Services.AddSingleton<ContainerMetricsHistory>();
builder.Services.AddHostedService<ContainerStatsBackgroundService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5050);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<MetricsHub>("/hub/metrics");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapGet("/api/metrics/current", (MetricsHistory history) =>
{
    var latest = history.Latest;
    return latest is not null ? Results.Ok(latest) : Results.NoContent();
});

app.MapGet("/api/metrics/history", (MetricsHistory history, int minutes = 60) =>
{
    var data = history.GetRange(TimeSpan.FromMinutes(minutes));
    return Results.Ok(data);
});

app.MapGet("/api/processes", async (ProcessCollector processCollector, int top = 25, CancellationToken ct = default) =>
{
    var processes = await processCollector.CollectAsync(top, ct);
    return Results.Ok(processes);
});

app.MapGet("/api/containers", async (IContainerService containers, CancellationToken ct) =>
{
    var list = await containers.ListContainersAsync(ct);
    var stats = await containers.GetAllStatsAsync(ct);
    return Results.Ok(new { containers = list, stats });
});

app.MapGet("/api/containers/sparklines", (ContainerMetricsHistory metricsHistory) =>
{
    var allHistory = metricsHistory.GetAllHistory();
    var payload = new Dictionary<string, object>(allHistory.Count);

    foreach (var (containerId, snapshots) in allHistory)
    {
        var cpu = new double[snapshots.Count];
        var mem = new double[snapshots.Count];

        for (var i = 0; i < snapshots.Count; i++)
        {
            cpu[i] = Math.Round(snapshots[i].CpuPercent, 2);
            mem[i] = Math.Round(snapshots[i].MemoryPercent, 2);
        }

        payload[containerId] = new { cpu, mem };
    }

    return Results.Ok(payload);
});

app.MapPost("/api/containers/{id}/start", async (string id, IContainerService containers, CancellationToken ct) =>
{
    await containers.StartAsync(id, ct);
    return Results.Ok();
});

app.MapPost("/api/containers/{id}/stop", async (string id, IContainerService containers, CancellationToken ct) =>
{
    await containers.StopAsync(id, ct);
    return Results.Ok();
});

app.MapPost("/api/containers/{id}/restart", async (string id, IContainerService containers, CancellationToken ct) =>
{
    await containers.RestartAsync(id, ct);
    return Results.Ok();
});

app.Run();

public partial class Program;

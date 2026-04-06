using Merlin.Web.Hubs;
using Merlin.Web.Services.Containers;
using Merlin.Web.Services.Metrics;
using Merlin.Web.Services.Persistence;

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

var dbPath = builder.Configuration["MERLIN_DB_PATH"] ?? "./data/merlin.db";
var retentionDays = int.TryParse(builder.Configuration["MERLIN_RETENTION_DAYS"], out var rd) ? rd : 7;
var retentionPeriod = TimeSpan.FromDays(retentionDays);

// Fall back to real /proc and /sys on Linux dev machines
if (!Directory.Exists(hostProcPath) && Directory.Exists("/proc"))
    hostProcPath = "/proc";
if (!Directory.Exists(hostSysPath) && Directory.Exists("/sys"))
    hostSysPath = "/sys";

builder.Services.AddSingleton(new MetricsCollectorOptions(hostProcPath, hostSysPath, hostRootPath));
builder.Services.AddSingleton<SystemInfoCollector>();
builder.Services.AddSingleton<ISystemMetricsCollector, LinuxMetricsCollector>();
builder.Services.AddSingleton<ProcessCollector>();
builder.Services.AddSingleton<MetricsHistory>();
builder.Services.AddHostedService<MetricsBackgroundService>();

builder.Services.AddSingleton(new ContainerServiceOptions(podmanSocketPath));
builder.Services.AddSingleton<IContainerService, PodmanContainerService>();
builder.Services.AddSingleton<ContainerMetricsHistory>();
builder.Services.AddHostedService<ContainerStatsBackgroundService>();
builder.Services.AddSingleton<ImageUpdateChecker>();
builder.Services.AddSingleton<ImageUpdateBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImageUpdateBackgroundService>());

// Persistence
builder.Services.AddSingleton(new MetricsRepository(dbPath));
builder.Services.AddHostedService(sp => new MetricsFlushService(
    sp.GetRequiredService<MetricsHistory>(),
    sp.GetRequiredService<MetricsRepository>(),
    retentionPeriod,
    sp.GetRequiredService<ILogger<MetricsFlushService>>()));

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5050);
});

var app = builder.Build();

// Load persisted history on startup
try
{
    var repository = app.Services.GetRequiredService<MetricsRepository>();
    var history = app.Services.GetRequiredService<MetricsHistory>();
    var startupLogger = app.Services.GetRequiredService<ILogger<MetricsHistory>>();

    var persisted = await repository.LoadRecentAsync(TimeSpan.FromHours(24));
    foreach (var snapshot in persisted)
    {
        history.Add(snapshot);
    }

    startupLogger.LogInformation("Loaded {Count} persisted metrics snapshots from SQLite", persisted.Count);
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<MetricsHistory>>();
    startupLogger.LogWarning(ex, "Failed to load persisted metrics history — starting with empty buffer");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<MetricsHub>("/hub/metrics");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapGet("/api/system-info", async (SystemInfoCollector collector, CancellationToken ct) =>
{
    var info = await collector.GetAsync(ct);
    return Results.Ok(info);
});

app.MapGet("/api/metrics/current", (MetricsHistory history) =>
{
    var latest = history.Latest;
    return latest is not null ? Results.Ok(latest) : Results.NoContent();
});

app.MapGet("/api/metrics/history", async (
    MetricsHistory history,
    MetricsRepository repository,
    int minutes = 60,
    CancellationToken ct = default) =>
{
    const int maxMinutes = 10_080; // 7 days
    minutes = Math.Clamp(minutes, 1, maxMinutes);

    var lookback = TimeSpan.FromMinutes(minutes);
    var inMemory = history.GetRange(lookback);

    // If requested range fits within the ring buffer, return in-memory data directly
    const int ringBufferSeconds = 86_400; // 24 hours
    if (minutes * 60 <= ringBufferSeconds && history.Count >= minutes * 60)
    {
        return Results.Ok(inMemory);
    }

    // Query SQLite for the full range and merge with in-memory data
    var from = DateTimeOffset.UtcNow - lookback;
    var to = DateTimeOffset.UtcNow;

    IReadOnlyList<Merlin.Web.Models.SystemMetrics> persisted;
    try
    {
        persisted = await repository.GetRangeAsync(from, to, ct);
    }
    catch (Exception)
    {
        // Fall back to in-memory only if SQLite fails
        return Results.Ok(inMemory);
    }

    // Merge and deduplicate by timestamp (in-memory takes precedence as fresher)
    var inMemoryTimestamps = new HashSet<DateTimeOffset>(inMemory.Select(m => m.Timestamp));
    var merged = new List<Merlin.Web.Models.SystemMetrics>(persisted.Count + inMemory.Count);

    foreach (var item in persisted)
    {
        if (!inMemoryTimestamps.Contains(item.Timestamp))
            merged.Add(item);
    }

    merged.AddRange(inMemory);
    merged.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

    return Results.Ok(merged);
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

app.MapGet("/api/containers/image-updates", (ImageUpdateBackgroundService updateService) =>
{
    return Results.Ok(updateService.LatestResults.Values.ToList());
});

app.MapPost("/api/containers/image-updates/check", async (
    ImageUpdateBackgroundService updateService,
    CancellationToken ct) =>
{
    await updateService.ForceCheckAsync(ct);
    return Results.Ok(updateService.LatestResults.Values.ToList());
});

app.MapGet("/api/containers/{id}/health", async (string id, IContainerService containers, CancellationToken ct) =>
{
    if (containers is PodmanContainerService podman)
    {
        var detail = await podman.GetHealthDetailAsync(id, ct);
        return detail is not null ? Results.Ok(detail) : Results.NotFound();
    }
    return Results.NotFound();
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

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Containers;

public sealed class PodmanContainerService : IContainerService, IDisposable
{
    private readonly HttpClient _client;
    private readonly ILogger<PodmanContainerService> _logger;
    private readonly bool _socketAvailable;
    private const string ApiBase = "http://podman/v4.0.0";

    // Track previous CPU stats per container for delta calculation
    private readonly ConcurrentDictionary<string, (long cpuUsage, long systemUsage)> _prevCpuStats = new();

    // Track previous network bytes per container for rate calculation
    private readonly ConcurrentDictionary<string, (long rx, long tx, DateTimeOffset timestamp)> _prevNetStats = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public PodmanContainerService(ContainerServiceOptions options, ILogger<PodmanContainerService> logger)
    {
        _logger = logger;
        // File.Exists returns false for Unix domain sockets; check via filesystem entry instead
        try
        {
            var info = new FileInfo(options.SocketPath);
            _socketAvailable = info.Exists || Directory.GetParent(options.SocketPath)?.GetFiles(Path.GetFileName(options.SocketPath)).Length > 0;
        }
        catch
        {
            _socketAvailable = false;
        }

        // Final fallback: try to actually connect
        if (!_socketAvailable)
        {
            try
            {
                using var testSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                testSocket.Connect(new UnixDomainSocketEndPoint(options.SocketPath));
                _socketAvailable = true;
            }
            catch
            {
                _socketAvailable = false;
            }
        }

        if (!_socketAvailable)
        {
            _logger.LogWarning("Podman socket not found at {Path}. Container features disabled", options.SocketPath);
            _client = new HttpClient();
            return;
        }

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.SocketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };

        _client = new HttpClient(handler) { BaseAddress = new Uri(ApiBase) };
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken ct = default)
    {
        if (!_socketAvailable) return [];

        try
        {
            var response = await _client.GetAsync($"{ApiBase}/containers/json?all=true", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var containers = JsonSerializer.Deserialize<List<PodmanContainer>>(json, JsonOptions) ?? [];

            return containers.Select(c =>
            {
                var name = c.Names?.FirstOrDefault()?.TrimStart('/')
                    ?? (c.Id.Length >= 12 ? c.Id[..12] : c.Id);
                var created = DateTimeOffset.FromUnixTimeSeconds(c.Created);
                var startedAt = c.StartedAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(c.StartedAt)
                    : created;
                var uptime = c.State == "running" ? DateTimeOffset.UtcNow - startedAt : TimeSpan.Zero;
                var health = c.Status ?? "unknown";

                return new ContainerInfo(
                    c.Id, name, c.Image ?? "unknown", c.Status ?? "unknown",
                    c.State ?? "unknown", health, created, uptime);
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list containers");
            return [];
        }
    }

    public async Task<IReadOnlyList<ContainerStats>> GetAllStatsAsync(CancellationToken ct = default)
    {
        if (!_socketAvailable) return [];

        try
        {
            var containers = await ListContainersAsync(ct);
            var running = containers.Where(c => c.State == "running").ToList();

            var tasks = running.Select(c => GetContainerStatsAsync(c.Id, c.Name, ct));
            var results = await Task.WhenAll(tasks);

            return results.Where(r => r is not null).Cast<ContainerStats>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get container stats");
            return [];
        }
    }

    private async Task<ContainerStats?> GetContainerStatsAsync(string id, string name, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetAsync($"{ApiBase}/containers/{id}/stats?stream=false&one-shot=true", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var stats = JsonSerializer.Deserialize<PodmanStats>(json, JsonOptions);
            if (stats is null) return null;

            // Calculate CPU % using our own previous-sample tracking
            var currentCpu = stats.CpuStats?.CpuUsage?.TotalUsage ?? 0;
            var currentSystem = stats.CpuStats?.SystemCpuUsage ?? 0;
            var onlineCpus = stats.CpuStats?.OnlineCpus ?? 1;
            if (onlineCpus == 0) onlineCpus = 1;

            var cpuPercent = 0.0;
            if (_prevCpuStats.TryGetValue(id, out var prev))
            {
                var cpuDelta = currentCpu - prev.cpuUsage;
                var systemDelta = currentSystem - prev.systemUsage;
                if (systemDelta > 0 && cpuDelta >= 0)
                {
                    cpuPercent = (double)cpuDelta / systemDelta * onlineCpus * 100;
                }
            }
            _prevCpuStats[id] = (currentCpu, currentSystem);

            var memUsage = stats.MemoryStats?.Usage ?? 0;
            var memLimit = stats.MemoryStats?.Limit ?? 1;
            var memPercent = memLimit > 0 ? (double)memUsage / memLimit * 100 : 0;

            // Calculate network rates from cumulative byte counters
            long totalRx = 0, totalTx = 0;
            if (stats.Networks is not null)
            {
                foreach (var iface in stats.Networks.Values)
                {
                    totalRx += iface.RxBytes;
                    totalTx += iface.TxBytes;
                }
            }

            var now = DateTimeOffset.UtcNow;
            double netRxRate = 0, netTxRate = 0;
            if (_prevNetStats.TryGetValue(id, out var prevNet))
            {
                var elapsed = (now - prevNet.timestamp).TotalSeconds;
                if (elapsed > 0)
                {
                    netRxRate = Math.Max(0, (totalRx - prevNet.rx) / elapsed);
                    netTxRate = Math.Max(0, (totalTx - prevNet.tx) / elapsed);
                }
            }
            _prevNetStats[id] = (totalRx, totalTx, now);

            return new ContainerStats(id, name, Math.Clamp(cpuPercent, 0, 100 * onlineCpus),
                memUsage, memLimit, Math.Clamp(memPercent, 0, 100), netTxRate, netRxRate);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get stats for container {Id}", id);
            return null;
        }
    }

    public async Task StartAsync(string id, CancellationToken ct = default)
    {
        if (!_socketAvailable) return;
        using var response = await _client.PostAsync($"{ApiBase}/containers/{id}/start", null, ct);
        if ((int)response.StatusCode != 304) // 304 = already started
            response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(string id, CancellationToken ct = default)
    {
        if (!_socketAvailable) return;
        using var response = await _client.PostAsync($"{ApiBase}/containers/{id}/stop", null, ct);
        if ((int)response.StatusCode != 304) // 304 = already stopped
            response.EnsureSuccessStatusCode();
    }

    public async Task RestartAsync(string id, CancellationToken ct = default)
    {
        if (!_socketAvailable) return;
        using var response = await _client.PostAsync($"{ApiBase}/containers/{id}/restart", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string id, int tail = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_socketAvailable) yield break;

        HttpResponseMessage? response = null;
        try
        {
            response = await _client.GetAsync(
                $"{ApiBase}/containers/{id}/logs?follow=true&stdout=true&stderr=true&tail={tail}",
                HttpCompletionOption.ResponseHeadersRead, ct);

            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                // Podman log frames may have an 8-byte header; strip it if present
                if (line.Length > 8 && (line[0] == '\x01' || line[0] == '\x02'))
                {
                    yield return line[8..];
                }
                else
                {
                    yield return line;
                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    // Podman API JSON models (private, for deserialization only)
    private sealed class PodmanContainer
    {
        public string Id { get; set; } = "";
        public List<string>? Names { get; set; }
        public string? Image { get; set; }
        public string? Status { get; set; }
        public string? State { get; set; }
        public long Created { get; set; }

        [JsonPropertyName("started_at")]
        public long StartedAt { get; set; }
    }

    private sealed class PodmanStats
    {
        [JsonPropertyName("cpu_stats")]
        public PodmanCpuStats? CpuStats { get; set; }

        [JsonPropertyName("precpu_stats")]
        public PodmanCpuStats? PrecpuStats { get; set; }

        [JsonPropertyName("memory_stats")]
        public PodmanMemoryStats? MemoryStats { get; set; }

        [JsonPropertyName("networks")]
        public Dictionary<string, PodmanNetworkStats>? Networks { get; set; }
    }

    private sealed class PodmanNetworkStats
    {
        [JsonPropertyName("rx_bytes")]
        public long RxBytes { get; set; }

        [JsonPropertyName("tx_bytes")]
        public long TxBytes { get; set; }
    }

    private sealed class PodmanCpuStats
    {
        [JsonPropertyName("cpu_usage")]
        public PodmanCpuUsage? CpuUsage { get; set; }

        [JsonPropertyName("system_cpu_usage")]
        public long SystemCpuUsage { get; set; }

        [JsonPropertyName("online_cpus")]
        public int OnlineCpus { get; set; }
    }

    private sealed class PodmanCpuUsage
    {
        [JsonPropertyName("total_usage")]
        public long TotalUsage { get; set; }
    }

    private sealed class PodmanMemoryStats
    {
        [JsonPropertyName("usage")]
        public long Usage { get; set; }

        [JsonPropertyName("limit")]
        public long Limit { get; set; }
    }
}

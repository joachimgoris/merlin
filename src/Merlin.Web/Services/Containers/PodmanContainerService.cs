using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Containers;

public sealed class PodmanContainerService : IContainerService, IDisposable
{
    private readonly HttpClient _client;
    private readonly ILogger<PodmanContainerService> _logger;
    private readonly bool _socketAvailable;
    private const string ApiBase = "http://podman/v4.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public PodmanContainerService(ContainerServiceOptions options, ILogger<PodmanContainerService> logger)
    {
        _logger = logger;
        _socketAvailable = File.Exists(options.SocketPath);

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
                var name = c.Names?.FirstOrDefault()?.TrimStart('/') ?? c.Id[..12];
                var created = DateTimeOffset.FromUnixTimeSeconds(c.Created);
                var uptime = c.State == "running" ? DateTimeOffset.UtcNow - created : TimeSpan.Zero;
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

            var cpuDelta = stats.CpuStats?.CpuUsage?.TotalUsage - stats.PrecpuStats?.CpuUsage?.TotalUsage ?? 0;
            var systemDelta = stats.CpuStats?.SystemCpuUsage - stats.PrecpuStats?.SystemCpuUsage ?? 0;
            var cpuPercent = systemDelta > 0
                ? (double)cpuDelta / systemDelta * (stats.CpuStats?.OnlineCpus ?? 1) * 100
                : 0;

            var memUsage = stats.MemoryStats?.Usage ?? 0;
            var memLimit = stats.MemoryStats?.Limit ?? 1;
            var memPercent = memLimit > 0 ? (double)memUsage / memLimit * 100 : 0;

            return new ContainerStats(id, name, Math.Clamp(cpuPercent, 0, 100 * (stats.CpuStats?.OnlineCpus ?? 1)),
                memUsage, memLimit, Math.Clamp(memPercent, 0, 100), 0, 0);
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
        var response = await _client.PostAsync($"{ApiBase}/containers/{id}/start", null, ct);
        if ((int)response.StatusCode != 304) // 304 = already started
            response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(string id, CancellationToken ct = default)
    {
        if (!_socketAvailable) return;
        var response = await _client.PostAsync($"{ApiBase}/containers/{id}/stop", null, ct);
        if ((int)response.StatusCode != 304) // 304 = already stopped
            response.EnsureSuccessStatusCode();
    }

    public async Task RestartAsync(string id, CancellationToken ct = default)
    {
        if (!_socketAvailable) return;
        await _client.PostAsync($"{ApiBase}/containers/{id}/restart", null, ct);
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
    }

    private sealed class PodmanStats
    {
        public PodmanCpuStats? CpuStats { get; set; }
        public PodmanCpuStats? PrecpuStats { get; set; }
        public PodmanMemoryStats? MemoryStats { get; set; }
    }

    private sealed class PodmanCpuStats
    {
        public PodmanCpuUsage? CpuUsage { get; set; }
        public long SystemCpuUsage { get; set; }
        public int OnlineCpus { get; set; }
    }

    private sealed class PodmanCpuUsage
    {
        public long TotalUsage { get; set; }
    }

    private sealed class PodmanMemoryStats
    {
        public long Usage { get; set; }
        public long Limit { get; set; }
    }
}

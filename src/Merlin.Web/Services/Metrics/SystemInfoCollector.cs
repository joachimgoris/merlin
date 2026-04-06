using Merlin.Web.Models;

namespace Merlin.Web.Services.Metrics;

public sealed class SystemInfoCollector(
    MetricsCollectorOptions options,
    ILogger<SystemInfoCollector> logger)
{
    private SystemInfo? _cached;

    public async Task<SystemInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        var hostname = await ReadHostnameAsync(cancellationToken);
        var os = await ReadOsNameAsync(cancellationToken);
        var kernel = await ReadKernelVersionAsync(cancellationToken);
        var (cpuModel, cpuCores) = await ReadCpuInfoAsync(cancellationToken);
        var totalRam = await ReadTotalRamAsync(cancellationToken);

        _cached = new SystemInfo(hostname, os, kernel, cpuModel, cpuCores, totalRam);
        return _cached;
    }

    private async Task<string> ReadHostnameAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(options.ProcPath, "sys", "kernel", "hostname");
            if (File.Exists(path))
                return (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read hostname from proc");
        }

        try
        {
            return System.Net.Dns.GetHostName();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get hostname via DNS");
            return string.Empty;
        }
    }

    private async Task<string> ReadOsNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = options.HostRootPath is not null
                ? Path.Combine(options.HostRootPath, "etc", "os-release")
                : "/etc/os-release";

            if (!File.Exists(path))
                return string.Empty;

            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            foreach (var line in lines)
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    var value = line["PRETTY_NAME=".Length..];
                    return value.Trim('"');
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read OS name");
        }

        return string.Empty;
    }

    private async Task<string> ReadKernelVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(options.ProcPath, "version");
            if (!File.Exists(path))
                return string.Empty;

            var text = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
            // /proc/version typically starts with "Linux version X.Y.Z ..."
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? parts[2] : text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read kernel version");
            return string.Empty;
        }
    }

    private async Task<(string Model, int Cores)> ReadCpuInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(options.ProcPath, "cpuinfo");
            if (!File.Exists(path))
                return (string.Empty, 0);

            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            var model = string.Empty;
            var coreCount = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("model name") && model.Length == 0)
                {
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx >= 0)
                        model = line[(colonIdx + 1)..].Trim();
                }
                else if (line.StartsWith("processor"))
                {
                    coreCount++;
                }
            }

            return (model, coreCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read CPU info");
            return (string.Empty, 0);
        }
    }

    private async Task<long> ReadTotalRamAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(options.ProcPath, "meminfo");
            if (!File.Exists(path))
                return 0;

            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var valuePart = line["MemTotal:".Length..].Trim();
                    var numStr = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    if (long.TryParse(numStr, out var kbValue))
                        return kbValue * 1024;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read total RAM");
        }

        return 0;
    }
}

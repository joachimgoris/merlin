using System.Collections.Concurrent;
using System.Globalization;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Metrics;

public sealed class ProcessCollector(
    MetricsCollectorOptions options,
    ILogger<ProcessCollector> logger)
{
    private readonly ConcurrentDictionary<int, (long cpuTime, DateTimeOffset timestamp)> _previousCpuTimes = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<ProcessInfo>> CollectAsync(int topN = 25, CancellationToken ct = default)
    {
        var totalMemoryBytes = await ReadTotalMemoryAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var activePids = new HashSet<int>();
        var processes = new List<ProcessInfo>();

        string[] pidDirs;
        try
        {
            pidDirs = Directory.GetDirectories(options.ProcPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate {ProcPath}", options.ProcPath);
            return [];
        }

        foreach (var pidDir in pidDirs)
        {
            var dirName = Path.GetFileName(pidDir);
            if (!int.TryParse(dirName, out var pid))
                continue;

            activePids.Add(pid);

            try
            {
                var process = await ReadProcessAsync(pid, now, totalMemoryBytes, ct);
                if (process is not null)
                    processes.Add(process);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Process disappeared between enumeration and reading — expected.
            }
        }

        CleanupStalePids(activePids, now);

        return processes
            .OrderByDescending(p => p.CpuPercent)
            .Take(topN)
            .ToList();
    }

    private async Task<ProcessInfo?> ReadProcessAsync(
        int pid,
        DateTimeOffset now,
        long totalMemoryBytes,
        CancellationToken ct)
    {
        var statPath = Path.Combine(options.ProcPath, pid.ToString(), "stat");
        var statusPath = Path.Combine(options.ProcPath, pid.ToString(), "status");

        var statLine = await File.ReadAllTextAsync(statPath, ct);
        var (name, state, cpuTimeTicks) = ParseStatLine(statLine);

        var (memoryBytes, uid) = await ReadStatusFileAsync(statusPath, ct);
        var user = uid.ToString(CultureInfo.InvariantCulture);

        var cpuPercent = CalculateCpuPercent(pid, cpuTimeTicks, now);
        var memoryPercent = totalMemoryBytes > 0
            ? (double)memoryBytes / totalMemoryBytes * 100.0
            : 0.0;

        return new ProcessInfo(
            pid,
            name,
            user,
            Math.Round(cpuPercent, 1),
            memoryBytes,
            Math.Round(memoryPercent, 1),
            state);
    }

    private static (string name, string state, long cpuTimeTicks) ParseStatLine(string line)
    {
        // Process name is enclosed in parentheses and can contain spaces or ')'.
        // Find the LAST ')' to correctly offset the remaining fields.
        var lastParen = line.LastIndexOf(')');
        if (lastParen < 0)
            return ("unknown", "?", 0);

        var firstParen = line.IndexOf('(');
        var name = firstParen >= 0 && lastParen > firstParen
            ? line[(firstParen + 1)..lastParen]
            : "unknown";

        // Fields after the closing ')' are space-separated.
        // Field index (1-based from man proc): state=3, utime=14, stime=15
        // After splitting the remainder, index 0 = state, index 11 = utime, index 12 = stime
        var remainder = line[(lastParen + 2)..]; // skip ') '
        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var state = parts.Length > 0 ? MapState(parts[0]) : "?";

        long utime = 0, stime = 0;
        if (parts.Length > 12)
        {
            long.TryParse(parts[11], out utime);
            long.TryParse(parts[12], out stime);
        }

        return (name, state, utime + stime);
    }

    private static string MapState(string code) => code switch
    {
        "R" => "Running",
        "S" => "Sleeping",
        "D" => "Disk Sleep",
        "Z" => "Zombie",
        "T" => "Stopped",
        "t" => "Tracing",
        "X" or "x" => "Dead",
        _ => code,
    };

    private async Task<(long memoryBytes, int uid)> ReadStatusFileAsync(string path, CancellationToken ct)
    {
        long memoryBytes = 0;
        var uid = -1;

        var lines = await File.ReadAllLinesAsync(path, ct);
        foreach (var line in lines)
        {
            if (line.StartsWith("VmRSS:"))
            {
                var valuePart = line[6..].Trim();
                var numStr = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (long.TryParse(numStr, out var kb))
                    memoryBytes = kb * 1024;
            }
            else if (line.StartsWith("Uid:"))
            {
                var valuePart = line[4..].Trim();
                var numStr = valuePart.Split('\t', StringSplitOptions.RemoveEmptyEntries)[0];
                int.TryParse(numStr, out uid);
            }
        }

        return (memoryBytes, uid);
    }

    private double CalculateCpuPercent(int pid, long currentCpuTime, DateTimeOffset now)
    {
        if (!_previousCpuTimes.TryGetValue(pid, out var prev))
        {
            _previousCpuTimes[pid] = (currentCpuTime, now);
            return 0.0;
        }

        var elapsed = (now - prev.timestamp).TotalSeconds;
        if (elapsed <= 0)
        {
            _previousCpuTimes[pid] = (currentCpuTime, now);
            return 0.0;
        }

        var tickDelta = currentCpuTime - prev.cpuTime;
        // Clock ticks per second is typically 100 on Linux (USER_HZ).
        var cpuSeconds = (double)tickDelta / 100.0;
        var percent = cpuSeconds / elapsed * 100.0;

        _previousCpuTimes[pid] = (currentCpuTime, now);
        return Math.Clamp(percent, 0, 100 * Environment.ProcessorCount);
    }

    private async Task<long> ReadTotalMemoryAsync(CancellationToken ct)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "meminfo"), ct);
            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:"))
                {
                    var valuePart = line[9..].Trim();
                    var numStr = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    if (long.TryParse(numStr, out var kb))
                        return kb * 1024;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read total memory from meminfo");
        }

        return 0;
    }

    private void CleanupStalePids(HashSet<int> activePids, DateTimeOffset now)
    {
        if (now - _lastCleanup < CleanupInterval)
            return;

        _lastCleanup = now;
        var stalePids = _previousCpuTimes.Keys.Where(pid => !activePids.Contains(pid)).ToList();
        foreach (var pid in stalePids)
        {
            _previousCpuTimes.TryRemove(pid, out _);
        }
    }
}

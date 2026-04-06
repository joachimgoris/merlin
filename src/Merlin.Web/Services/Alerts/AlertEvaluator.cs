using Merlin.Web.Models;

namespace Merlin.Web.Services.Alerts;

public sealed class AlertEvaluator(AlertOptions options)
{
    private const int CpuSampleCount = 60;

    private readonly AlertOptions _options = options;
    private readonly Queue<double> _cpuSamples = new();
    private readonly Dictionary<string, string> _previousContainerStates = [];
    private readonly Dictionary<string, string> _previousContainerHealth = [];
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = [];

    public List<Alert> Evaluate(SystemMetrics? metrics, IReadOnlyList<ContainerInfo> containers)
    {
        var alerts = new List<Alert>();
        var now = DateTimeOffset.UtcNow;

        if (metrics is not null)
        {
            EvaluateCpu(metrics, alerts, now);
            EvaluateMemory(metrics, alerts, now);
            EvaluateDisk(metrics, alerts, now);
        }

        EvaluateContainers(containers, alerts, now);

        return alerts;
    }

    private void EvaluateCpu(SystemMetrics metrics, List<Alert> alerts, DateTimeOffset now)
    {
        _cpuSamples.Enqueue(metrics.Cpu.TotalUsagePercent);

        while (_cpuSamples.Count > CpuSampleCount)
        {
            _cpuSamples.Dequeue();
        }

        if (_cpuSamples.Count < CpuSampleCount)
        {
            return;
        }

        var allAbove = true;
        foreach (var sample in _cpuSamples)
        {
            if (sample <= _options.CpuThreshold)
            {
                allAbove = false;
                break;
            }
        }

        if (!allAbove)
        {
            return;
        }

        var key = $"{AlertType.CpuHigh}:cpu";
        if (IsOnCooldown(key, now))
        {
            return;
        }

        _cooldowns[key] = now;
        alerts.Add(new Alert(
            AlertType.CpuHigh,
            "cpu",
            $"CPU usage has exceeded {_options.CpuThreshold}% for the last 60 samples (current: {metrics.Cpu.TotalUsagePercent:F1}%)",
            AlertSeverity.Critical,
            now));
    }

    private void EvaluateMemory(SystemMetrics metrics, List<Alert> alerts, DateTimeOffset now)
    {
        if (metrics.Memory.TotalBytes == 0)
        {
            return;
        }

        var usedPercent = (double)metrics.Memory.UsedBytes / metrics.Memory.TotalBytes * 100;

        if (usedPercent <= _options.MemThreshold)
        {
            return;
        }

        var key = $"{AlertType.MemHigh}:memory";
        if (IsOnCooldown(key, now))
        {
            return;
        }

        _cooldowns[key] = now;
        alerts.Add(new Alert(
            AlertType.MemHigh,
            "memory",
            $"Memory usage is at {usedPercent:F1}% ({FormatBytes(metrics.Memory.UsedBytes)} / {FormatBytes(metrics.Memory.TotalBytes)})",
            AlertSeverity.Critical,
            now));
    }

    private void EvaluateDisk(SystemMetrics metrics, List<Alert> alerts, DateTimeOffset now)
    {
        foreach (var mount in metrics.Disk.Mounts)
        {
            if (mount.TotalBytes == 0)
            {
                continue;
            }

            var usedPercent = (double)mount.UsedBytes / mount.TotalBytes * 100;

            if (usedPercent <= _options.DiskThreshold)
            {
                continue;
            }

            var key = $"{AlertType.DiskHigh}:{mount.MountPoint}";
            if (IsOnCooldown(key, now))
            {
                continue;
            }

            _cooldowns[key] = now;
            alerts.Add(new Alert(
                AlertType.DiskHigh,
                mount.MountPoint,
                $"Disk usage on {mount.MountPoint} is at {usedPercent:F1}% ({FormatBytes(mount.UsedBytes)} / {FormatBytes(mount.TotalBytes)})",
                AlertSeverity.Warning,
                now));
        }
    }

    private void EvaluateContainers(IReadOnlyList<ContainerInfo> containers, List<Alert> alerts, DateTimeOffset now)
    {
        var currentIds = new HashSet<string>();

        foreach (var container in containers)
        {
            currentIds.Add(container.Id);
            var name = string.IsNullOrEmpty(container.Name) ? container.Id : container.Name;

            // State transitions
            if (_previousContainerStates.TryGetValue(container.Id, out var prevState))
            {
                if (prevState == "running" && container.State != "running")
                {
                    var key = $"{AlertType.ContainerStopped}:{container.Id}";
                    if (!IsOnCooldown(key, now))
                    {
                        _cooldowns[key] = now;
                        alerts.Add(new Alert(
                            AlertType.ContainerStopped,
                            name,
                            $"Container '{name}' stopped (was running, now {container.State})",
                            AlertSeverity.Warning,
                            now));
                    }
                }
                else if (prevState != "running" && container.State == "running")
                {
                    var key = $"{AlertType.ContainerRecovered}:{container.Id}";
                    if (!IsOnCooldown(key, now))
                    {
                        _cooldowns[key] = now;
                        alerts.Add(new Alert(
                            AlertType.ContainerRecovered,
                            name,
                            $"Container '{name}' recovered (was {prevState}, now running)",
                            AlertSeverity.Info,
                            now));
                    }
                }
            }

            _previousContainerStates[container.Id] = container.State;

            // Health transitions
            if (!string.IsNullOrEmpty(container.Health) &&
                container.Health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase))
            {
                var hadPreviousHealth = _previousContainerHealth.TryGetValue(container.Id, out var prevHealth);
                if (!hadPreviousHealth || !prevHealth!.Equals("unhealthy", StringComparison.OrdinalIgnoreCase))
                {
                    var key = $"{AlertType.ContainerUnhealthy}:{container.Id}";
                    if (!IsOnCooldown(key, now))
                    {
                        _cooldowns[key] = now;
                        alerts.Add(new Alert(
                            AlertType.ContainerUnhealthy,
                            name,
                            $"Container '{name}' is unhealthy",
                            AlertSeverity.Warning,
                            now));
                    }
                }
            }

            _previousContainerHealth[container.Id] = container.Health;
        }

        // Clean up stale entries
        var staleIds = new List<string>();
        foreach (var id in _previousContainerStates.Keys)
        {
            if (!currentIds.Contains(id))
            {
                staleIds.Add(id);
            }
        }

        foreach (var id in staleIds)
        {
            _previousContainerStates.Remove(id);
            _previousContainerHealth.Remove(id);
        }
    }

    private bool IsOnCooldown(string key, DateTimeOffset now)
    {
        if (!_cooldowns.TryGetValue(key, out var lastFired))
        {
            return false;
        }

        return lastFired + TimeSpan.FromMinutes(_options.CooldownMinutes) > now;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }
}

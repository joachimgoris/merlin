using Merlin.Web.Models;

namespace Merlin.Web.Services.Metrics;

public sealed class LinuxMetricsCollector(
    MetricsCollectorOptions options,
    ILogger<LinuxMetricsCollector> logger) : ISystemMetricsCollector
{
    private long[]? _prevCpuTimes;
    private long[][]? _prevPerCoreTimes;
    private Dictionary<string, (long rx, long tx)>? _prevNetBytes;
    private Dictionary<string, (long read, long write)>? _prevDiskSectors;
    private DateTimeOffset _prevTimestamp = DateTimeOffset.UtcNow;

    public async Task<SystemMetrics> CollectAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _prevTimestamp).TotalSeconds;
        if (elapsed <= 0) elapsed = 1;

        var cpu = await CollectCpuAsync(elapsed, ct);
        var memory = await CollectMemoryAsync(ct);
        var disk = await CollectDiskAsync(elapsed, ct);
        var network = await CollectNetworkAsync(elapsed, ct);
        var temperature = CollectTemperature();

        _prevTimestamp = now;

        return new SystemMetrics(cpu, memory, disk, network, temperature, now);
    }

    private async Task<CpuMetrics> CollectCpuAsync(double elapsed, CancellationToken ct)
    {
        try
        {
            var statLines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "stat"), ct);
            var loadAvgLine = await File.ReadAllTextAsync(Path.Combine(options.ProcPath, "loadavg"), ct);

            var totalUsage = 0.0;
            var perCore = new List<double>();
            long[]? currentTotal = null;
            var currentPerCore = new List<long[]>();

            foreach (var line in statLines)
            {
                if (line.StartsWith("cpu "))
                {
                    currentTotal = ParseCpuLine(line);
                }
                else if (line.StartsWith("cpu") && char.IsDigit(line[3]))
                {
                    currentPerCore.Add(ParseCpuLine(line));
                }
            }

            if (currentTotal is not null && _prevCpuTimes is not null)
            {
                totalUsage = CalculateCpuUsage(_prevCpuTimes, currentTotal);
            }
            _prevCpuTimes = currentTotal;

            if (_prevPerCoreTimes is not null)
            {
                for (var i = 0; i < currentPerCore.Count && i < _prevPerCoreTimes.Length; i++)
                {
                    perCore.Add(CalculateCpuUsage(_prevPerCoreTimes[i], currentPerCore[i]));
                }
            }
            _prevPerCoreTimes = currentPerCore.ToArray();

            var loadParts = loadAvgLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var load1 = loadParts.Length > 0 ? ParseDouble(loadParts[0]) : 0;
            var load5 = loadParts.Length > 1 ? ParseDouble(loadParts[1]) : 0;
            var load15 = loadParts.Length > 2 ? ParseDouble(loadParts[2]) : 0;

            var freqMhz = await ReadCpuFrequencyAsync(ct);

            return new CpuMetrics(totalUsage, perCore, load1, load5, load15, freqMhz);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect CPU metrics");
            return new CpuMetrics(0, [], 0, 0, 0, 0);
        }
    }

    private async Task<double> ReadCpuFrequencyAsync(CancellationToken ct)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "cpuinfo"), ct);
            var maxFreq = 0.0;
            foreach (var line in lines)
            {
                if (line.StartsWith("cpu MHz"))
                {
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx >= 0)
                    {
                        var freq = ParseDouble(line[(colonIdx + 1)..].Trim());
                        if (freq > maxFreq) maxFreq = freq;
                    }
                }
            }
            return maxFreq;
        }
        catch
        {
            return 0;
        }
    }

    private static long[] ParseCpuLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Skip label (cpu, cpu0, etc.), parse user, nice, system, idle, iowait, irq, softirq, steal
        var times = new long[Math.Min(parts.Length - 1, 8)];
        for (var i = 0; i < times.Length; i++)
        {
            long.TryParse(parts[i + 1], out times[i]);
        }
        return times;
    }

    private static double CalculateCpuUsage(long[] prev, long[] current)
    {
        if (prev.Length == 0 || current.Length == 0) return 0;

        long prevTotal = 0, currTotal = 0;
        for (var i = 0; i < Math.Min(prev.Length, current.Length); i++)
        {
            prevTotal += prev[i];
            currTotal += current[i];
        }

        var prevIdle = prev.Length > 3 ? prev[3] + (prev.Length > 4 ? prev[4] : 0) : 0;
        var currIdle = current.Length > 3 ? current[3] + (current.Length > 4 ? current[4] : 0) : 0;

        var totalDelta = currTotal - prevTotal;
        var idleDelta = currIdle - prevIdle;

        if (totalDelta <= 0) return 0;
        return Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100, 0, 100);
    }

    private async Task<MemoryMetrics> CollectMemoryAsync(CancellationToken ct)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "meminfo"), ct);
            var values = new Dictionary<string, long>();

            foreach (var line in lines)
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx < 0) continue;

                var key = line[..colonIdx].Trim();
                var valuePart = line[(colonIdx + 1)..].Trim();
                var numStr = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (long.TryParse(numStr, out var kbValue))
                {
                    values[key] = kbValue * 1024; // Convert kB to bytes
                }
            }

            var total = values.GetValueOrDefault("MemTotal");
            var available = values.GetValueOrDefault("MemAvailable");
            var used = total - available;
            var swapTotal = values.GetValueOrDefault("SwapTotal");
            var swapFree = values.GetValueOrDefault("SwapFree");

            return new MemoryMetrics(total, used, available, swapTotal, swapTotal - swapFree);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect memory metrics");
            return new MemoryMetrics(0, 0, 0, 0, 0);
        }
    }

    private async Task<DiskMetrics> CollectDiskAsync(double elapsed, CancellationToken ct)
    {
        try
        {
            var mounts = new List<DiskMountMetrics>();
            var mountLines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "mounts"), ct);

            // Read diskstats for I/O rates
            var diskStatsLines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "diskstats"), ct);
            var currentDiskSectors = ParseDiskStats(diskStatsLines);

            var pseudoFs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "tmpfs", "proc", "sysfs", "devtmpfs", "cgroup", "cgroup2", "overlay", "devpts", "mqueue", "hugetlbfs", "debugfs", "securityfs", "pstore", "bpf", "tracefs", "fusectl", "configfs", "efivarfs" };

            foreach (var line in mountLines)
            {
                var parts = line.Split(' ');
                if (parts.Length < 3) continue;

                var device = parts[0];
                var mountPoint = parts[1];
                var fsType = parts[2];

                if (pseudoFs.Contains(fsType)) continue;
                if (mountPoint.StartsWith("/host/proc") || mountPoint.StartsWith("/host/sys")) continue;
                if (!mountPoint.StartsWith('/')) continue;

                try
                {
                    var driveInfo = new DriveInfo(mountPoint);
                    if (!driveInfo.IsReady || driveInfo.TotalSize == 0) continue;

                    var deviceName = Path.GetFileName(device);
                    var readRate = 0.0;
                    var writeRate = 0.0;

                    if (_prevDiskSectors is not null &&
                        currentDiskSectors.TryGetValue(deviceName, out var curr) &&
                        _prevDiskSectors.TryGetValue(deviceName, out var prev))
                    {
                        readRate = (curr.read - prev.read) * 512.0 / elapsed;
                        writeRate = (curr.write - prev.write) * 512.0 / elapsed;
                    }

                    mounts.Add(new DiskMountMetrics(
                        mountPoint, device,
                        driveInfo.TotalSize,
                        driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
                        driveInfo.AvailableFreeSpace,
                        Math.Max(0, readRate), Math.Max(0, writeRate)));
                }
                catch
                {
                    // Skip mounts we can't stat
                }
            }

            _prevDiskSectors = currentDiskSectors;
            return new DiskMetrics(mounts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect disk metrics");
            return new DiskMetrics([]);
        }
    }

    private static Dictionary<string, (long read, long write)> ParseDiskStats(string[] lines)
    {
        var result = new Dictionary<string, (long, long)>();
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 14) continue;
            var name = parts[2];
            if (long.TryParse(parts[5], out var readSectors) && long.TryParse(parts[9], out var writeSectors))
            {
                result[name] = (readSectors, writeSectors);
            }
        }
        return result;
    }

    private async Task<NetworkMetrics> CollectNetworkAsync(double elapsed, CancellationToken ct)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(options.ProcPath, "net", "dev"), ct);
            var interfaces = new List<NetworkInterfaceMetrics>();
            var currentBytes = new Dictionary<string, (long rx, long tx)>();

            foreach (var line in lines.Skip(2)) // Skip header lines
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx < 0) continue;

                var name = line[..colonIdx].Trim();
                if (name == "lo") continue;

                var parts = line[(colonIdx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10) continue;

                var rxBytes = long.TryParse(parts[0], out var rx) ? rx : 0;
                var txBytes = long.TryParse(parts[8], out var tx) ? tx : 0;
                currentBytes[name] = (rxBytes, txBytes);

                var rxRate = 0.0;
                var txRate = 0.0;
                if (_prevNetBytes is not null && _prevNetBytes.TryGetValue(name, out var prev))
                {
                    rxRate = Math.Max(0, (rxBytes - prev.rx) / elapsed);
                    txRate = Math.Max(0, (txBytes - prev.tx) / elapsed);
                }

                interfaces.Add(new NetworkInterfaceMetrics(name, txRate, rxRate, txBytes, rxBytes));
            }

            _prevNetBytes = currentBytes;
            return new NetworkMetrics(interfaces);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect network metrics");
            return new NetworkMetrics([]);
        }
    }

    private TemperatureMetrics CollectTemperature()
    {
        var sensors = new List<TemperatureSensor>();

        try
        {
            // Try /sys/class/thermal/thermal_zone*
            var thermalPath = Path.Combine(options.SysPath, "class", "thermal");
            if (Directory.Exists(thermalPath))
            {
                foreach (var zone in Directory.GetDirectories(thermalPath, "thermal_zone*"))
                {
                    var tempFile = Path.Combine(zone, "temp");
                    var typeFile = Path.Combine(zone, "type");

                    if (!File.Exists(tempFile)) continue;

                    var tempStr = File.ReadAllText(tempFile).Trim();
                    var label = File.Exists(typeFile) ? File.ReadAllText(typeFile).Trim() : Path.GetFileName(zone);

                    if (long.TryParse(tempStr, out var milliDegrees))
                    {
                        sensors.Add(new TemperatureSensor(label, milliDegrees / 1000.0, null));
                    }
                }
            }

            // Try /sys/class/hwmon/hwmon* for more detailed sensors
            var hwmonPath = Path.Combine(options.SysPath, "class", "hwmon");
            if (Directory.Exists(hwmonPath))
            {
                foreach (var hwmon in Directory.GetDirectories(hwmonPath))
                {
                    var tempFiles = Directory.GetFiles(hwmon, "temp*_input");
                    foreach (var tempFile in tempFiles)
                    {
                        var prefix = Path.GetFileNameWithoutExtension(tempFile).Replace("_input", "");
                        var labelFile = Path.Combine(hwmon, $"{prefix}_label");
                        var critFile = Path.Combine(hwmon, $"{prefix}_crit");

                        var tempStr = File.ReadAllText(tempFile).Trim();
                        var label = File.Exists(labelFile) ? File.ReadAllText(labelFile).Trim() : prefix;

                        double? critical = null;
                        if (File.Exists(critFile) && long.TryParse(File.ReadAllText(critFile).Trim(), out var critMilliDeg))
                        {
                            critical = critMilliDeg / 1000.0;
                        }

                        if (long.TryParse(tempStr, out var milliDegrees))
                        {
                            sensors.Add(new TemperatureSensor(label, milliDegrees / 1000.0, critical));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect temperature metrics");
        }

        return new TemperatureMetrics(sensors);
    }

    private static double ParseDouble(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}

using FluentAssertions;
using Merlin.Web.Services.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Merlin.Web.Tests;

public sealed class LinuxMetricsCollectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _procDir;
    private readonly string _sysDir;

    public LinuxMetricsCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "merlin-test-" + Guid.NewGuid().ToString("N")[..8]);
        _procDir = Path.Combine(_tempDir, "proc");
        _sysDir = Path.Combine(_tempDir, "sys");
        Directory.CreateDirectory(_procDir);
        Directory.CreateDirectory(_sysDir);
        Directory.CreateDirectory(Path.Combine(_procDir, "net"));
    }

    [Fact]
    public async Task CollectAsync_WithProcStatAndMeminfo_ReturnsCpuAndMemory()
    {
        WriteProcStat(idle: 900, user: 100);
        WriteProcMeminfo(totalKb: 16_000_000, availableKb: 8_000_000);
        WriteProcLoadAvg("1.50 2.00 1.75 1/200 1234");
        WriteProcCpuInfo(3600.0);
        WriteProcNetDev([]);
        WriteProcDiskStats([]);
        WriteProcMounts([]);

        var collector = CreateCollector();

        // First call establishes baseline
        await collector.CollectAsync();

        // Update with new values for delta
        WriteProcStat(idle: 950, user: 150);
        var metrics = await collector.CollectAsync();

        metrics.Cpu.Should().NotBeNull();
        metrics.Cpu.LoadAvg1.Should().BeApproximately(1.5, 0.01);
        metrics.Cpu.LoadAvg5.Should().BeApproximately(2.0, 0.01);
        metrics.Memory.TotalBytes.Should().Be(16_000_000L * 1024);
        metrics.Memory.AvailableBytes.Should().Be(8_000_000L * 1024);
    }

    [Fact]
    public async Task CollectAsync_MissingFiles_ReturnsZeroMetrics()
    {
        // Completely empty proc directory
        var collector = CreateCollector();

        var metrics = await collector.CollectAsync();

        metrics.Cpu.TotalUsagePercent.Should().Be(0);
        metrics.Memory.TotalBytes.Should().Be(0);
        metrics.Disk.Mounts.Should().BeEmpty();
        metrics.Network.Interfaces.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_NetworkInterfaces_ParsesCorrectly()
    {
        WriteProcStat(idle: 900, user: 100);
        WriteProcMeminfo(totalKb: 8_000_000, availableKb: 4_000_000);
        WriteProcLoadAvg("0.50 0.50 0.50 1/100 1000");
        WriteProcCpuInfo(2400.0);
        WriteProcDiskStats([]);
        WriteProcMounts([]);
        WriteProcNetDev([("eth0", rxBytes: 1000, txBytes: 2000)]);

        var collector = CreateCollector();
        await collector.CollectAsync();

        // Update with new byte counts
        WriteProcNetDev([("eth0", rxBytes: 2000, txBytes: 4000)]);
        var metrics = await collector.CollectAsync();

        metrics.Network.Interfaces.Should().ContainSingle();
        metrics.Network.Interfaces[0].Name.Should().Be("eth0");
        // Rates should be positive (delta / elapsed)
        metrics.Network.Interfaces[0].RxBytesPerSec.Should().BeGreaterThanOrEqualTo(0);
        metrics.Network.Interfaces[0].TxBytesPerSec.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CollectAsync_TemperatureSensors_ParsesCorrectly()
    {
        WriteProcStat(idle: 900, user: 100);
        WriteProcMeminfo(totalKb: 8_000_000, availableKb: 4_000_000);
        WriteProcLoadAvg("0.50 0.50 0.50 1/100 1000");
        WriteProcCpuInfo(2400.0);
        WriteProcNetDev([]);
        WriteProcDiskStats([]);
        WriteProcMounts([]);
        WriteThermalZone("thermal_zone0", "x86_pkg_temp", 55000);

        var collector = CreateCollector();
        var metrics = await collector.CollectAsync();

        metrics.Temperature.Sensors.Should().ContainSingle();
        metrics.Temperature.Sensors[0].Label.Should().Be("x86_pkg_temp");
        metrics.Temperature.Sensors[0].CelsiusCurrent.Should().BeApproximately(55.0, 0.1);
    }

    private LinuxMetricsCollector CreateCollector() =>
        new(new MetricsCollectorOptions(_procDir, _sysDir),
            NullLogger<LinuxMetricsCollector>.Instance);

    private void WriteProcStat(long idle, long user)
    {
        File.WriteAllText(Path.Combine(_procDir, "stat"),
            $"cpu  {user} 10 50 {idle} 20 5 5 0 0 0\ncpu0 {user} 10 50 {idle} 20 5 5 0 0 0\n");
    }

    private void WriteProcMeminfo(long totalKb, long availableKb)
    {
        File.WriteAllText(Path.Combine(_procDir, "meminfo"),
            $"MemTotal:       {totalKb} kB\nMemFree:        {availableKb / 2} kB\nMemAvailable:   {availableKb} kB\nSwapTotal:      4000000 kB\nSwapFree:       3000000 kB\n");
    }

    private void WriteProcLoadAvg(string content)
    {
        File.WriteAllText(Path.Combine(_procDir, "loadavg"), content);
    }

    private void WriteProcCpuInfo(double mhz)
    {
        File.WriteAllText(Path.Combine(_procDir, "cpuinfo"),
            $"processor\t: 0\ncpu MHz\t\t: {mhz}\n");
    }

    private void WriteProcNetDev(IEnumerable<(string name, long rxBytes, long txBytes)> interfaces)
    {
        var lines = new List<string>
        {
            "Inter-|   Receive                                                |  Transmit",
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed",
        };
        foreach (var (name, rx, tx) in interfaces)
        {
            lines.Add($"  {name}: {rx} 100 0 0 0 0 0 0 {tx} 100 0 0 0 0 0 0");
        }
        File.WriteAllText(Path.Combine(_procDir, "net", "dev"), string.Join('\n', lines));
    }

    private void WriteProcDiskStats(IEnumerable<(string name, long readSectors, long writeSectors)> disks)
    {
        var lines = disks.Select(d => $"   8       0 {d.name} 100 0 {d.readSectors} 0 50 0 {d.writeSectors} 0 0 0 0 0 0 0 0");
        File.WriteAllText(Path.Combine(_procDir, "diskstats"), string.Join('\n', lines));
    }

    private void WriteProcMounts(IEnumerable<(string device, string mount, string fsType)> mounts)
    {
        var lines = mounts.Select(m => $"{m.device} {m.mount} {m.fsType} rw 0 0");
        File.WriteAllText(Path.Combine(_procDir, "mounts"), string.Join('\n', lines));
    }

    private void WriteThermalZone(string zone, string type, long milliDegrees)
    {
        var zonePath = Path.Combine(_sysDir, "class", "thermal", zone);
        Directory.CreateDirectory(zonePath);
        File.WriteAllText(Path.Combine(zonePath, "temp"), milliDegrees.ToString());
        File.WriteAllText(Path.Combine(zonePath, "type"), type);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* cleanup best-effort */ }
    }
}

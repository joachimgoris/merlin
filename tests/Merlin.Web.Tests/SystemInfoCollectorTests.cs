using FluentAssertions;
using Merlin.Web.Services.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Merlin.Web.Tests;

public sealed class SystemInfoCollectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _procDir;

    public SystemInfoCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "merlin-test-" + Guid.NewGuid().ToString("N")[..8]);
        _procDir = Path.Combine(_tempDir, "proc");
        Directory.CreateDirectory(_procDir);
    }

    [Fact]
    public async Task GetAsync_WithValidProcFiles_ReturnsSystemInfo()
    {
        WriteHostname("test-server");
        WriteCpuInfo("Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz", 4);
        WriteMeminfo(totalKb: 16_000_000);
        WriteVersion("Linux version 5.15.0-91-generic (buildd@lcy02-amd64-045) (gcc 11.4.0)");

        var collector = CreateCollector();
        var info = await collector.GetAsync();

        info.Hostname.Should().Be("test-server");
        info.CpuModel.Should().Be("Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz");
        info.CpuCores.Should().Be(4);
        info.TotalRamBytes.Should().Be(16_000_000L * 1024);
        info.Kernel.Should().Be("5.15.0-91-generic");
    }

    [Fact]
    public async Task GetAsync_MissingFiles_ReturnsEmptyDefaults()
    {
        var collector = CreateCollector();

        var info = await collector.GetAsync();

        info.CpuModel.Should().BeEmpty();
        info.CpuCores.Should().Be(0);
        info.TotalRamBytes.Should().Be(0);
        info.Kernel.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_CachesResult()
    {
        WriteHostname("cached-host");
        WriteCpuInfo("AMD EPYC", 2);
        WriteMeminfo(totalKb: 8_000_000);
        WriteVersion("Linux version 6.1.0 (builder@host)");

        var collector = CreateCollector();
        var first = await collector.GetAsync();
        var second = await collector.GetAsync();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_ParsesCpuInfo_MultipleProcessors()
    {
        WriteCpuInfo("Intel Xeon", 8);
        WriteMeminfo(totalKb: 32_000_000);
        WriteVersion("Linux version 6.1.0 (builder@host)");

        var collector = CreateCollector();
        var info = await collector.GetAsync();

        info.CpuCores.Should().Be(8);
    }

    [Fact]
    public async Task GetAsync_ParsesKernelVersion()
    {
        WriteCpuInfo("cpu", 1);
        WriteMeminfo(totalKb: 4_000_000);
        WriteVersion("Linux version 6.1.0-25-amd64 (debian-kernel@lists.debian.org) (gcc-12 (Debian 12.2.0-14) 12.2.0)");

        var collector = CreateCollector();
        var info = await collector.GetAsync();

        info.Kernel.Should().Be("6.1.0-25-amd64");
    }

    private SystemInfoCollector CreateCollector() =>
        new(new MetricsCollectorOptions(_procDir, Path.Combine(_tempDir, "sys")),
            NullLogger<SystemInfoCollector>.Instance);

    private void WriteHostname(string hostname)
    {
        var dir = Path.Combine(_procDir, "sys", "kernel");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "hostname"), hostname);
    }

    private void WriteCpuInfo(string model, int cores)
    {
        var lines = new List<string>();
        for (var i = 0; i < cores; i++)
        {
            lines.Add($"processor\t: {i}");
            lines.Add($"model name\t: {model}");
            lines.Add($"cpu MHz\t\t: 2600.000");
            lines.Add("");
        }

        File.WriteAllText(Path.Combine(_procDir, "cpuinfo"), string.Join('\n', lines));
    }

    private void WriteMeminfo(long totalKb)
    {
        File.WriteAllText(Path.Combine(_procDir, "meminfo"),
            $"MemTotal:       {totalKb} kB\nMemFree:        {totalKb / 2} kB\nMemAvailable:   {totalKb / 2} kB\n");
    }

    private void WriteVersion(string content)
    {
        File.WriteAllText(Path.Combine(_procDir, "version"), content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* cleanup best-effort */ }
    }
}

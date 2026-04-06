using FluentAssertions;
using Merlin.Web.Models;
using Merlin.Web.Services.Metrics;

namespace Merlin.Web.Tests;

public sealed class MetricsHistoryGetLatestTests
{
    private static SystemMetrics CreateSnapshot(DateTimeOffset? timestamp = null) => new(
        Cpu: new CpuMetrics(50, [50], 1.0, 1.0, 1.0, 3000),
        Memory: new MemoryMetrics(16_000_000_000, 8_000_000_000, 8_000_000_000, 0, 0),
        Disk: new DiskMetrics([]),
        Network: new NetworkMetrics([]),
        Temperature: new TemperatureMetrics([]),
        Timestamp: timestamp ?? DateTimeOffset.UtcNow);

    [Fact]
    public void GetLatest_ReturnsNMostRecent()
    {
        var history = new MetricsHistory();
        var baseTime = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
        {
            history.Add(CreateSnapshot(baseTime.AddSeconds(i)));
        }

        var result = history.GetLatest(3);

        result.Should().HaveCount(3);
        // Should be the last 3 in chronological order
        result[0].Timestamp.Should().BeCloseTo(baseTime.AddSeconds(7), TimeSpan.FromMilliseconds(10));
        result[1].Timestamp.Should().BeCloseTo(baseTime.AddSeconds(8), TimeSpan.FromMilliseconds(10));
        result[2].Timestamp.Should().BeCloseTo(baseTime.AddSeconds(9), TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void GetLatest_RequestMoreThanAvailable_ReturnsAll()
    {
        var history = new MetricsHistory();

        for (var i = 0; i < 3; i++)
        {
            history.Add(CreateSnapshot());
        }

        var result = history.GetLatest(10);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void GetLatest_EmptyBuffer_ReturnsEmpty()
    {
        var history = new MetricsHistory();

        var result = history.GetLatest(5);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetLatest_ZeroCount_ReturnsEmpty()
    {
        var history = new MetricsHistory();
        history.Add(CreateSnapshot());

        var result = history.GetLatest(0);

        result.Should().BeEmpty();
    }
}

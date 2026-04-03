using FluentAssertions;
using Merlin.Web.Models;
using Merlin.Web.Services.Metrics;

namespace Merlin.Web.Tests;

public sealed class MetricsHistoryTests
{
    private static SystemMetrics CreateSnapshot(DateTimeOffset? timestamp = null) => new(
        Cpu: new CpuMetrics(50, [50], 1.0, 1.0, 1.0, 3000),
        Memory: new MemoryMetrics(16_000_000_000, 8_000_000_000, 8_000_000_000, 0, 0),
        Disk: new DiskMetrics([]),
        Network: new NetworkMetrics([]),
        Temperature: new TemperatureMetrics([]),
        Timestamp: timestamp ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Latest_EmptyBuffer_ReturnsNull()
    {
        var history = new MetricsHistory();

        history.Latest.Should().BeNull();
    }

    [Fact]
    public void Latest_AfterAdd_ReturnsMostRecent()
    {
        var history = new MetricsHistory();
        var snapshot = CreateSnapshot();

        history.Add(snapshot);

        history.Latest.Should().BeSameAs(snapshot);
    }

    [Fact]
    public void Count_TracksAdds()
    {
        var history = new MetricsHistory();

        history.Add(CreateSnapshot());
        history.Add(CreateSnapshot());
        history.Add(CreateSnapshot());

        history.Count.Should().Be(3);
    }

    [Fact]
    public void GetRange_ReturnsItemsWithinLookback()
    {
        var history = new MetricsHistory();
        var old = CreateSnapshot(DateTimeOffset.UtcNow.AddMinutes(-10));
        var recent = CreateSnapshot(DateTimeOffset.UtcNow.AddSeconds(-30));
        var newest = CreateSnapshot(DateTimeOffset.UtcNow);

        history.Add(old);
        history.Add(recent);
        history.Add(newest);

        var result = history.GetRange(TimeSpan.FromMinutes(5));

        result.Should().HaveCount(2);
        result[0].Should().BeSameAs(recent);
        result[1].Should().BeSameAs(newest);
    }

    [Fact]
    public void GetRange_EmptyBuffer_ReturnsEmpty()
    {
        var history = new MetricsHistory();

        var result = history.GetRange(TimeSpan.FromMinutes(5));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Add_ConcurrentAccess_DoesNotThrow()
    {
        var history = new MetricsHistory();

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                history.Add(CreateSnapshot());
                var latest = history.Latest;
                var range = history.GetRange(TimeSpan.FromMinutes(1));
            }
        })).ToArray();

        var act = () => Task.WaitAll(tasks);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetRange_ReturnsChronologicalOrder()
    {
        var history = new MetricsHistory();
        for (var i = 0; i < 10; i++)
        {
            history.Add(CreateSnapshot(DateTimeOffset.UtcNow.AddSeconds(i)));
        }

        var result = history.GetRange(TimeSpan.FromMinutes(5));

        for (var i = 1; i < result.Count; i++)
        {
            result[i].Timestamp.Should().BeOnOrAfter(result[i - 1].Timestamp);
        }
    }
}

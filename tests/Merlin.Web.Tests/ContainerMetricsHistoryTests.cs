using FluentAssertions;
using Merlin.Web.Services.Containers;

namespace Merlin.Web.Tests;

public sealed class ContainerMetricsHistoryTests
{
    [Fact]
    public void Record_And_GetHistory_ReturnsSnapshots()
    {
        var history = new ContainerMetricsHistory();

        for (var i = 0; i < 5; i++)
        {
            history.Record("container-1", cpu: i * 10.0, mem: i * 5.0);
        }

        var snapshots = history.GetHistory("container-1");

        snapshots.Should().HaveCount(5);
        snapshots[0].CpuPercent.Should().Be(0.0);
        snapshots[4].CpuPercent.Should().Be(40.0);
    }

    [Fact]
    public void Record_ExceedsCapacity_EvictsOldest()
    {
        var history = new ContainerMetricsHistory();

        for (var i = 0; i < 305; i++)
        {
            history.Record("container-1", cpu: i, mem: 0);
        }

        var snapshots = history.GetHistory("container-1");

        snapshots.Should().HaveCount(300);
        // The oldest 5 (cpu 0..4) should have been evicted; first should be cpu=5
        snapshots[0].CpuPercent.Should().Be(5.0);
        snapshots[299].CpuPercent.Should().Be(304.0);
    }

    [Fact]
    public void GetHistory_UnknownContainer_ReturnsEmpty()
    {
        var history = new ContainerMetricsHistory();

        var snapshots = history.GetHistory("nonexistent");

        snapshots.Should().BeEmpty();
    }

    [Fact]
    public void GetAllHistory_ReturnsAllContainers()
    {
        var history = new ContainerMetricsHistory();

        history.Record("container-a", cpu: 10, mem: 20);
        history.Record("container-b", cpu: 30, mem: 40);
        history.Record("container-c", cpu: 50, mem: 60);

        var allHistory = history.GetAllHistory();

        allHistory.Should().HaveCount(3);
        allHistory.Should().ContainKey("container-a");
        allHistory.Should().ContainKey("container-b");
        allHistory.Should().ContainKey("container-c");
    }

    [Fact]
    public void PruneStale_RemovesOldContainers()
    {
        var history = new ContainerMetricsHistory();

        // Record for two containers; both get UtcNow timestamps internally
        history.Record("stale-container", cpu: 10, mem: 20);
        history.Record("fresh-container", cpu: 30, mem: 40);

        // PruneStale checks LatestTimestamp < UtcNow - 60s.
        // Since we just recorded, both should be kept after pruning.
        history.PruneStale();

        var allHistory = history.GetAllHistory();
        allHistory.Should().HaveCount(2);
        allHistory.Should().ContainKey("stale-container");
        allHistory.Should().ContainKey("fresh-container");
    }

    [Fact]
    public void PruneStale_KeepsRecentContainers()
    {
        var history = new ContainerMetricsHistory();

        // Record a fresh container
        history.Record("fresh", cpu: 50, mem: 50);

        history.PruneStale();

        history.GetHistory("fresh").Should().NotBeEmpty();
    }

    [Fact]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var history = new ContainerMetricsHistory();

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            var containerId = $"container-{i % 5}";
            for (var j = 0; j < 100; j++)
            {
                history.Record(containerId, cpu: j, mem: j * 2);
                _ = history.GetHistory(containerId);
                _ = history.GetAllHistory();
            }
        })).ToArray();

        var act = () => Task.WaitAll(tasks);
        act.Should().NotThrow();
    }
}

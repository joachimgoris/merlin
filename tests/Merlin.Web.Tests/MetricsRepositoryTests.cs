using FluentAssertions;
using Merlin.Web.Models;
using Merlin.Web.Services.Persistence;

namespace Merlin.Web.Tests;

public sealed class MetricsRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MetricsRepository _repository;

    public MetricsRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "merlin-test-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        _repository = new MetricsRepository(_dbPath);
    }

    private static SystemMetrics CreateSnapshot(DateTimeOffset? timestamp = null) => new(
        Cpu: new CpuMetrics(50, [50], 1.0, 1.0, 1.0, 3000),
        Memory: new MemoryMetrics(16_000_000_000, 8_000_000_000, 8_000_000_000, 0, 0),
        Disk: new DiskMetrics([]),
        Network: new NetworkMetrics([]),
        Temperature: new TemperatureMetrics([]),
        Timestamp: timestamp ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task InsertBatchAsync_And_GetRangeAsync_RoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            CreateSnapshot(now.AddSeconds(-2)),
            CreateSnapshot(now.AddSeconds(-1)),
            CreateSnapshot(now),
        };

        await _repository.InsertBatchAsync(snapshots);

        var result = await _repository.GetRangeAsync(now.AddSeconds(-3), now.AddSeconds(1));

        result.Should().HaveCount(3);
        result[0].Cpu.TotalUsagePercent.Should().Be(50);
        result[0].Memory.TotalBytes.Should().Be(16_000_000_000);
    }

    [Fact]
    public async Task GetRangeAsync_EmptyDatabase_ReturnsEmpty()
    {
        var result = await _repository.GetRangeAsync(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PruneAsync_RemovesOldRecords()
    {
        var now = DateTimeOffset.UtcNow;
        var oldSnapshot = CreateSnapshot(now.AddHours(-5));
        var newSnapshot = CreateSnapshot(now);

        await _repository.InsertBatchAsync([oldSnapshot, newSnapshot]);

        // Prune records older than 1 hour
        await _repository.PruneAsync(TimeSpan.FromHours(1));

        var result = await _repository.GetRangeAsync(now.AddHours(-10), now.AddHours(1));

        result.Should().HaveCount(1);
        result[0].Timestamp.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetLatestTimestampAsync_ReturnsNewest()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            CreateSnapshot(now.AddMinutes(-10)),
            CreateSnapshot(now.AddMinutes(-5)),
            CreateSnapshot(now),
        };

        await _repository.InsertBatchAsync(snapshots);

        var latest = await _repository.GetLatestTimestampAsync();

        latest.Should().NotBeNull();
        latest!.Value.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetLatestTimestampAsync_EmptyDatabase_ReturnsNull()
    {
        var latest = await _repository.GetLatestTimestampAsync();

        latest.Should().BeNull();
    }

    [Fact]
    public async Task LoadRecentAsync_ReturnsWithinLookback()
    {
        var now = DateTimeOffset.UtcNow;
        var oldSnapshot = CreateSnapshot(now.AddHours(-2));
        var recentSnapshot = CreateSnapshot(now.AddMinutes(-10));
        var newestSnapshot = CreateSnapshot(now);

        await _repository.InsertBatchAsync([oldSnapshot, recentSnapshot, newestSnapshot]);

        var result = await _repository.LoadRecentAsync(TimeSpan.FromHours(1));

        result.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _repository.Dispose();
        try { File.Delete(_dbPath); } catch { /* cleanup best-effort */ }
    }
}

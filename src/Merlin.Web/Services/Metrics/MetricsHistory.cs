using Merlin.Web.Models;

namespace Merlin.Web.Services.Metrics;

public sealed class MetricsHistory
{
    private const int Capacity = 86_400; // 1 per second for 24 hours
    private readonly SystemMetrics?[] _buffer = new SystemMetrics?[Capacity];
    private int _index = -1;
    private int _count;
    private readonly Lock _lock = new();

    public SystemMetrics? Latest
    {
        get
        {
            lock (_lock)
            {
                return _count == 0 ? null : _buffer[_index];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public void Add(SystemMetrics snapshot)
    {
        lock (_lock)
        {
            _index = (_index + 1) % Capacity;
            _buffer[_index] = snapshot;
            if (_count < Capacity) _count++;
        }
    }

    public IReadOnlyList<SystemMetrics> GetLatest(int count)
    {
        lock (_lock)
        {
            if (_count == 0 || count <= 0) return [];

            var take = Math.Min(count, _count);
            var result = new List<SystemMetrics>(take);

            for (var i = take - 1; i >= 0; i--)
            {
                var idx = (_index - i + Capacity) % Capacity;
                var item = _buffer[idx];
                if (item is not null) result.Add(item);
            }

            return result;
        }
    }

    public IReadOnlyList<SystemMetrics> GetRange(TimeSpan lookback)
    {
        lock (_lock)
        {
            if (_count == 0) return [];

            var cutoff = DateTimeOffset.UtcNow - lookback;
            var result = new List<SystemMetrics>();

            // Walk backwards from newest to oldest
            for (var i = 0; i < _count; i++)
            {
                var idx = (_index - i + Capacity) % Capacity;
                var item = _buffer[idx];
                if (item is null) break;
                if (item.Timestamp < cutoff) break;
                result.Add(item);
            }

            result.Reverse();
            return result;
        }
    }
}

using System.Collections.Concurrent;

namespace Merlin.Web.Services.Containers;

public sealed record ContainerMetricSnapshot(
    double CpuPercent,
    double MemoryPercent,
    DateTimeOffset Timestamp);

public sealed class ContainerMetricsHistory
{
    private const int MaxSamples = 300;
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, ContainerRingBuffer> _buffers = new();

    public void Record(string containerId, double cpu, double mem)
    {
        var buffer = _buffers.GetOrAdd(containerId, _ => new ContainerRingBuffer(MaxSamples));
        buffer.Add(new ContainerMetricSnapshot(cpu, mem, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<ContainerMetricSnapshot> GetHistory(string containerId)
    {
        return _buffers.TryGetValue(containerId, out var buffer)
            ? buffer.GetAll()
            : [];
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ContainerMetricSnapshot>> GetAllHistory()
    {
        var result = new Dictionary<string, IReadOnlyList<ContainerMetricSnapshot>>();

        foreach (var (id, buffer) in _buffers)
        {
            var snapshots = buffer.GetAll();
            if (snapshots.Count > 0)
            {
                result[id] = snapshots;
            }
        }

        return result;
    }

    public void PruneStale()
    {
        var cutoff = DateTimeOffset.UtcNow - StaleThreshold;

        foreach (var (id, buffer) in _buffers)
        {
            if (buffer.LatestTimestamp < cutoff)
            {
                _buffers.TryRemove(id, out _);
            }
        }
    }

    private sealed class ContainerRingBuffer(int capacity)
    {
        private readonly ContainerMetricSnapshot[] _buffer = new ContainerMetricSnapshot[capacity];
        private readonly Lock _lock = new();
        private int _index = -1;
        private int _count;

        public DateTimeOffset LatestTimestamp
        {
            get
            {
                lock (_lock)
                {
                    return _count == 0
                        ? DateTimeOffset.MinValue
                        : _buffer[_index].Timestamp;
                }
            }
        }

        public void Add(ContainerMetricSnapshot snapshot)
        {
            lock (_lock)
            {
                _index = (_index + 1) % capacity;
                _buffer[_index] = snapshot;
                if (_count < capacity) _count++;
            }
        }

        public IReadOnlyList<ContainerMetricSnapshot> GetAll()
        {
            lock (_lock)
            {
                if (_count == 0) return [];

                var result = new ContainerMetricSnapshot[_count];
                for (var i = 0; i < _count; i++)
                {
                    var idx = (_index - _count + 1 + i + capacity) % capacity;
                    result[i] = _buffer[idx];
                }

                return result;
            }
        }
    }
}

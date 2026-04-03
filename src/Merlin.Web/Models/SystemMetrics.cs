namespace Merlin.Web.Models;

public sealed record SystemMetrics(
    CpuMetrics Cpu,
    MemoryMetrics Memory,
    DiskMetrics Disk,
    NetworkMetrics Network,
    TemperatureMetrics Temperature,
    DateTimeOffset Timestamp);

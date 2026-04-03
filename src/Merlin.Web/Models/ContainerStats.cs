namespace Merlin.Web.Models;

public sealed record ContainerStats(
    string ContainerId,
    string Name,
    double CpuPercent,
    long MemoryUsageBytes,
    long MemoryLimitBytes,
    double MemoryPercent,
    double NetTxBytesPerSec,
    double NetRxBytesPerSec);

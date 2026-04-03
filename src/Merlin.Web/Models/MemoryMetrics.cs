namespace Merlin.Web.Models;

public sealed record MemoryMetrics(
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    long SwapTotalBytes,
    long SwapUsedBytes);

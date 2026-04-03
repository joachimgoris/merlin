namespace Merlin.Web.Models;

public sealed record CpuMetrics(
    double TotalUsagePercent,
    IReadOnlyList<double> PerCoreUsagePercent,
    double LoadAvg1,
    double LoadAvg5,
    double LoadAvg15,
    double FrequencyMhz);

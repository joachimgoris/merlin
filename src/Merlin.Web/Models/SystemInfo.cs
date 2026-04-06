namespace Merlin.Web.Models;

public sealed record SystemInfo(
    string Hostname,
    string Os,
    string Kernel,
    string CpuModel,
    int CpuCores,
    long TotalRamBytes);

namespace Merlin.Web.Models;

public sealed record NetworkMetrics(IReadOnlyList<NetworkInterfaceMetrics> Interfaces);

public sealed record NetworkInterfaceMetrics(
    string Name,
    double TxBytesPerSec,
    double RxBytesPerSec,
    long TotalTxBytes,
    long TotalRxBytes);

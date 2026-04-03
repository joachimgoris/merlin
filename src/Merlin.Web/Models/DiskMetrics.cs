namespace Merlin.Web.Models;

public sealed record DiskMetrics(IReadOnlyList<DiskMountMetrics> Mounts);

public sealed record DiskMountMetrics(
    string MountPoint,
    string Device,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    double ReadBytesPerSec,
    double WriteBytesPerSec);

namespace Merlin.Web.Models;

public enum AlertType
{
    CpuHigh,
    MemHigh,
    DiskHigh,
    ContainerStopped,
    ContainerRecovered,
    ContainerUnhealthy
}

public enum AlertSeverity
{
    Critical,
    Warning,
    Info
}

public sealed record Alert(
    AlertType Type,
    string Subject,
    string Message,
    AlertSeverity Severity,
    DateTimeOffset Timestamp);

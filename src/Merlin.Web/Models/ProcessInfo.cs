namespace Merlin.Web.Models;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    string User,
    double CpuPercent,
    long MemoryBytes,
    double MemoryPercent,
    string State);

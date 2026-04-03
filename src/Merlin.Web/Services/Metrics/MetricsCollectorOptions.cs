namespace Merlin.Web.Services.Metrics;

public sealed record MetricsCollectorOptions(string ProcPath, string SysPath, string? HostRootPath = null);

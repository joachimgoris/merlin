namespace Merlin.Web.Services.Alerts;

public sealed record AlertOptions(
    string WebhookUrl,
    int CpuThreshold = 90,
    int MemThreshold = 90,
    int DiskThreshold = 95,
    int CooldownMinutes = 15);

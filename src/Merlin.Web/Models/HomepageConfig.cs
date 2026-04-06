namespace Merlin.Web.Models;

public sealed record HomepageConfigEntry(
    string Name,
    string Url,
    string? HealthUrl,
    string Icon,
    string Group,
    string Description);

public sealed record HomepageConfig(List<HomepageConfigEntry> Services);

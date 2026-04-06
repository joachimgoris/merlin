namespace Merlin.Web.Models;

public sealed record HomepageService(
    string Id,
    string Name,
    string Url,
    string? HealthUrl,
    string Icon,
    string Group,
    string Description,
    string Status,
    string? ContainerId,
    string? ContainerState)
{
    /// <summary>URL used for health checks. Falls back to Url if not set.</summary>
    public string EffectiveHealthUrl => !string.IsNullOrWhiteSpace(HealthUrl) ? HealthUrl : Url;
}

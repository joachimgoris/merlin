namespace Merlin.Web.Models;

public sealed record HomepageService(
    string Id,
    string Name,
    string Url,
    string Icon,
    string Group,
    string Description,
    string Status,
    string? ContainerId,
    string? ContainerState);

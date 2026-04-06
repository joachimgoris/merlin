namespace Merlin.Web.Models;

public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string ImageId,
    string Version,
    string Status,
    string State,
    string Health,
    DateTimeOffset Created,
    TimeSpan Uptime);

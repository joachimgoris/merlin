namespace Merlin.Web.Models;

public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Status,
    string State,
    string Health,
    DateTimeOffset Created,
    TimeSpan Uptime);

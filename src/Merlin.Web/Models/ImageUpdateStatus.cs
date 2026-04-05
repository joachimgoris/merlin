namespace Merlin.Web.Models;

public sealed record ImageUpdateStatus(
    string ImageReference,
    string LocalDigest,
    string? RemoteDigest,
    bool UpdateAvailable,
    DateTimeOffset CheckedAt,
    string? Error);

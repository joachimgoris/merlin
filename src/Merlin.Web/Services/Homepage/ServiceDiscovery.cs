using System.Security.Cryptography;
using System.Text;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Homepage;

public sealed class ServiceDiscovery(HomepageConfigLoader configLoader)
{
    private const string LabelPrefix = "merlin.homepage.";
    private const string LabelName = "merlin.homepage.name";
    private const string LabelUrl = "merlin.homepage.url";
    private const string LabelIcon = "merlin.homepage.icon";
    private const string LabelGroup = "merlin.homepage.group";
    private const string LabelDescription = "merlin.homepage.description";
    private const string LabelHealthUrl = "merlin.homepage.healthUrl";

    public List<HomepageService> DiscoverServices(IReadOnlyList<ContainerInfo> containers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<HomepageService>();

        // Container-discovered services take precedence
        foreach (var container in containers)
        {
            if (!container.Labels.TryGetValue(LabelName, out var name) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!container.Labels.TryGetValue(LabelUrl, out var url) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            container.Labels.TryGetValue(LabelIcon, out var icon);
            container.Labels.TryGetValue(LabelGroup, out var group);
            container.Labels.TryGetValue(LabelDescription, out var description);
            container.Labels.TryGetValue(LabelHealthUrl, out var healthUrl);

            var dedupeKey = $"{name}\n{url}".ToLowerInvariant();
            seen.Add(dedupeKey);

            result.Add(new HomepageService(
                Id: GenerateDeterministicId(name),
                Name: name,
                Url: url,
                HealthUrl: healthUrl,
                Icon: icon ?? "",
                Group: group ?? "Services",
                Description: description ?? "",
                Status: "unknown",
                ContainerId: container.Id,
                ContainerState: container.State));
        }

        // Static config entries (deduplicated against container services)
        var configEntries = configLoader.Load();

        foreach (var entry in configEntries)
        {
            var dedupeKey = $"{entry.Name}\n{entry.Url}".ToLowerInvariant();

            if (seen.Contains(dedupeKey))
            {
                continue;
            }

            seen.Add(dedupeKey);

            result.Add(new HomepageService(
                Id: GenerateDeterministicId(entry.Name),
                Name: entry.Name,
                Url: entry.Url,
                HealthUrl: entry.HealthUrl,
                Icon: entry.Icon,
                Group: string.IsNullOrWhiteSpace(entry.Group) ? "Services" : entry.Group,
                Description: entry.Description,
                Status: "unknown",
                ContainerId: null,
                ContainerState: null));
        }

        return result;
    }

    internal static string GenerateDeterministicId(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return Convert.ToHexStringLower(hash)[..12];
    }
}

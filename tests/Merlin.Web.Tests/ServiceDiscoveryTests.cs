using FluentAssertions;
using Merlin.Web.Models;
using Merlin.Web.Services.Homepage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Merlin.Web.Tests;

public sealed class ServiceDiscoveryTests
{
    private static HomepageConfigLoader CreateLoader(string? path = null) =>
        new(path ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json"),
            NullLogger<HomepageConfigLoader>.Instance);

    [Fact]
    public void ContainerWithHomepageLabels_ProducesService()
    {
        var loader = CreateLoader();
        var discovery = new ServiceDiscovery(loader);

        var containers = new List<ContainerInfo>
        {
            CreateContainer("c1", "Grafana", new Dictionary<string, string>
            {
                ["merlin.homepage.name"] = "Grafana",
                ["merlin.homepage.url"] = "http://grafana:3000",
                ["merlin.homepage.icon"] = "📊",
                ["merlin.homepage.group"] = "Monitoring",
                ["merlin.homepage.description"] = "Dashboards",
            }),
        };

        var services = discovery.DiscoverServices(containers);

        services.Should().HaveCount(1);
        var svc = services[0];
        svc.Name.Should().Be("Grafana");
        svc.Url.Should().Be("http://grafana:3000");
        svc.Icon.Should().Be("📊");
        svc.Group.Should().Be("Monitoring");
        svc.Description.Should().Be("Dashboards");
        svc.ContainerId.Should().Be("c1");
        svc.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ContainerWithoutNameLabel_IsExcluded()
    {
        var loader = CreateLoader();
        var discovery = new ServiceDiscovery(loader);

        var containers = new List<ContainerInfo>
        {
            CreateContainer("c1", "NoName", new Dictionary<string, string>
            {
                ["merlin.homepage.url"] = "http://example.com",
            }),
        };

        var services = discovery.DiscoverServices(containers);

        services.Should().BeEmpty();
    }

    [Fact]
    public void StaticConfigEntries_AreIncluded()
    {
        var configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        File.WriteAllText(configPath, """
        {
          "services": [
            {
              "name": "External API",
              "url": "https://api.example.com",
              "icon": "🌐",
              "group": "External",
              "description": "Third-party API"
            }
          ]
        }
        """);

        try
        {
            var loader = CreateLoader(configPath);
            var discovery = new ServiceDiscovery(loader);

            var services = discovery.DiscoverServices([]);

            services.Should().HaveCount(1);
            services[0].Name.Should().Be("External API");
            services[0].Group.Should().Be("External");
            services[0].ContainerId.Should().BeNull();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void ContainerSourceWins_OnDuplicate()
    {
        var configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        File.WriteAllText(configPath, """
        {
          "services": [
            {
              "name": "Grafana",
              "url": "http://grafana:3000",
              "icon": "📊",
              "group": "Config",
              "description": "From config"
            }
          ]
        }
        """);

        try
        {
            var loader = CreateLoader(configPath);
            var discovery = new ServiceDiscovery(loader);

            var containers = new List<ContainerInfo>
            {
                CreateContainer("c1", "Grafana", new Dictionary<string, string>
                {
                    ["merlin.homepage.name"] = "Grafana",
                    ["merlin.homepage.url"] = "http://grafana:3000",
                    ["merlin.homepage.icon"] = "📈",
                    ["merlin.homepage.group"] = "Container",
                    ["merlin.homepage.description"] = "From container",
                }),
            };

            var services = discovery.DiscoverServices(containers);

            services.Should().HaveCount(1);
            services[0].Group.Should().Be("Container");
            services[0].Description.Should().Be("From container");
            services[0].ContainerId.Should().Be("c1");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void EmptyInputs_ProducesEmptyResult()
    {
        var loader = CreateLoader();
        var discovery = new ServiceDiscovery(loader);

        var services = discovery.DiscoverServices([]);

        services.Should().BeEmpty();
    }

    private static ContainerInfo CreateContainer(
        string id,
        string name,
        IReadOnlyDictionary<string, string> labels) =>
        new(
            Id: id,
            Name: name,
            Image: "test:latest",
            ImageId: "sha256:abc",
            Version: "1.0",
            Status: "Up 5 minutes",
            State: "running",
            Health: "healthy",
            Created: DateTimeOffset.UtcNow.AddHours(-1),
            Uptime: TimeSpan.FromMinutes(5),
            HealthLog: null,
            LastHealthCheck: null,
            ComposeProject: "test",
            Labels: labels);
}

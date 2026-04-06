using FluentAssertions;
using Merlin.Web.Services.Homepage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Merlin.Web.Tests;

public sealed class HomepageConfigLoaderTests
{
    [Fact]
    public void ValidJsonFile_LoadsCorrectly()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        File.WriteAllText(path, """
        {
          "services": [
            {
              "name": "Prometheus",
              "url": "http://prometheus:9090",
              "icon": "🔥",
              "group": "Monitoring",
              "description": "Metrics collection"
            },
            {
              "name": "Grafana",
              "url": "http://grafana:3000",
              "icon": "📊",
              "group": "Monitoring",
              "description": "Dashboards"
            }
          ]
        }
        """);

        try
        {
            var loader = new HomepageConfigLoader(path, NullLogger<HomepageConfigLoader>.Instance);
            var entries = loader.Load();

            entries.Should().HaveCount(2);
            entries[0].Name.Should().Be("Prometheus");
            entries[0].Url.Should().Be("http://prometheus:9090");
            entries[1].Name.Should().Be("Grafana");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        var loader = new HomepageConfigLoader(path, NullLogger<HomepageConfigLoader>.Instance);

        var entries = loader.Load();

        entries.Should().BeEmpty();
    }

    [Fact]
    public void InvalidJson_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        File.WriteAllText(path, "{ this is not valid json }}}");

        try
        {
            var loader = new HomepageConfigLoader(path, NullLogger<HomepageConfigLoader>.Instance);
            var entries = loader.Load();

            entries.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }
}

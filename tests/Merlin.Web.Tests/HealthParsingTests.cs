using System.Reflection;
using FluentAssertions;
using Merlin.Web.Services.Containers;

namespace Merlin.Web.Tests;

public sealed class HealthParsingTests
{
    private static readonly MethodInfo ParseHealthMethod =
        typeof(PodmanContainerService).GetMethod(
            "ParseHealthFromStatus",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExtractVersionMethod =
        typeof(PodmanContainerService).GetMethod(
            "ExtractVersion",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string InvokeParseHealth(string? status) =>
        (string)ParseHealthMethod.Invoke(null, [status])!;

    private static string InvokeExtractVersion(Dictionary<string, string>? labels, string? image) =>
        (string)ExtractVersionMethod.Invoke(null, [labels, image])!;

    [Theory]
    [InlineData("Up 2 hours (healthy)", "healthy")]
    [InlineData("Up 5 minutes (unhealthy)", "unhealthy")]
    [InlineData("Up 1 hour (starting)", "starting")]
    public void ParseHealthFromStatus_WithHealthTag_ReturnsCorrectStatus(string status, string expected)
    {
        var result = InvokeParseHealth(status);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Up 3 days", "none")]
    [InlineData("Exited (0) 2 hours ago", "none")]
    public void ParseHealthFromStatus_WithoutHealthTag_ReturnsNone(string status, string expected)
    {
        var result = InvokeParseHealth(status);

        result.Should().Be(expected);
    }

    [Fact]
    public void ParseHealthFromStatus_Null_ReturnsNone()
    {
        var result = InvokeParseHealth(null);

        result.Should().Be("none");
    }

    [Fact]
    public void ParseHealthFromStatus_Empty_ReturnsNone()
    {
        var result = InvokeParseHealth("");

        result.Should().Be("none");
    }

    [Fact]
    public void ExtractVersion_OciVersionLabel_ReturnsVersion()
    {
        var labels = new Dictionary<string, string>
        {
            ["org.opencontainers.image.version"] = "1.27.4"
        };

        var result = InvokeExtractVersion(labels, "nginx:1.25");

        result.Should().Be("1.27.4");
    }

    [Fact]
    public void ExtractVersion_VersionLabel_ReturnsVersion()
    {
        var labels = new Dictionary<string, string>
        {
            ["version"] = "3.0.1"
        };

        var result = InvokeExtractVersion(labels, "myapp:latest");

        result.Should().Be("3.0.1");
    }

    [Fact]
    public void ExtractVersion_NoLabels_ImageWithTag_ReturnsTag()
    {
        var result = InvokeExtractVersion(null, "nginx:1.25");

        result.Should().Be("1.25");
    }

    [Fact]
    public void ExtractVersion_NoLabels_ImageWithLatestTag_ReturnsEmpty()
    {
        var result = InvokeExtractVersion(null, "nginx:latest");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractVersion_NoLabels_ImageWithNoTag_ReturnsEmpty()
    {
        var result = InvokeExtractVersion(null, "nginx");

        result.Should().BeEmpty();
    }
}

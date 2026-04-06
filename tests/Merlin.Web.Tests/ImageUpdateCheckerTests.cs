using FluentAssertions;
using Merlin.Web.Services.Containers;

namespace Merlin.Web.Tests;

public sealed class ImageUpdateCheckerTests
{
    [Fact]
    public void ParseImageReference_SimpleImage_DefaultsToDockerHub()
    {
        var result = ImageUpdateChecker.ParseImageReference("nginx");

        result.Registry.Should().Be("registry-1.docker.io");
        result.Repository.Should().Be("library/nginx");
        result.Tag.Should().Be("latest");
    }

    [Fact]
    public void ParseImageReference_ImageWithTag()
    {
        var result = ImageUpdateChecker.ParseImageReference("nginx:1.25");

        result.Registry.Should().Be("registry-1.docker.io");
        result.Repository.Should().Be("library/nginx");
        result.Tag.Should().Be("1.25");
    }

    [Fact]
    public void ParseImageReference_DockerHubUserRepo()
    {
        var result = ImageUpdateChecker.ParseImageReference("myuser/myapp:v2");

        result.Registry.Should().Be("registry-1.docker.io");
        result.Repository.Should().Be("myuser/myapp");
        result.Tag.Should().Be("v2");
    }

    [Fact]
    public void ParseImageReference_GhcrImage()
    {
        var result = ImageUpdateChecker.ParseImageReference("ghcr.io/user/repo:latest");

        result.Registry.Should().Be("ghcr.io");
        result.Repository.Should().Be("user/repo");
        result.Tag.Should().Be("latest");
    }

    [Fact]
    public void ParseImageReference_DockerIoExplicit()
    {
        var result = ImageUpdateChecker.ParseImageReference("docker.io/library/redis:7");

        result.Registry.Should().Be("registry-1.docker.io");
        result.Repository.Should().Be("library/redis");
        result.Tag.Should().Be("7");
    }

    [Fact]
    public void ParseImageReference_RegistryWithPort()
    {
        var result = ImageUpdateChecker.ParseImageReference("registry:5000/myapp:v1");

        result.Registry.Should().Be("registry:5000");
        result.Repository.Should().Be("myapp");
        result.Tag.Should().Be("v1");
    }

    [Fact]
    public void ParseImageReference_NoTag_DefaultsToLatest()
    {
        var result = ImageUpdateChecker.ParseImageReference("ghcr.io/user/repo");

        result.Registry.Should().Be("ghcr.io");
        result.Repository.Should().Be("user/repo");
        result.Tag.Should().Be("latest");
    }
}

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Containers;

public sealed class ImageUpdateChecker : IDisposable
{
    private readonly HttpClient _podmanClient;
    private readonly HttpClient _registryClient;
    private readonly ILogger<ImageUpdateChecker> _logger;
    private readonly bool _socketAvailable;
    private readonly ConcurrentDictionary<string, (ImageUpdateStatus Status, DateTimeOffset CachedAt)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private const string PodmanApiBase = "http://podman/v4.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ImageUpdateChecker(
        ContainerServiceOptions options,
        ILogger<ImageUpdateChecker> logger)
    {
        _logger = logger;

        // Set up Podman client (Unix socket) using the same pattern as PodmanContainerService
        try
        {
            var info = new FileInfo(options.SocketPath);
            _socketAvailable = info.Exists
                || Directory.GetParent(options.SocketPath)?.GetFiles(Path.GetFileName(options.SocketPath)).Length > 0;
        }
        catch
        {
            _socketAvailable = false;
        }

        if (!_socketAvailable)
        {
            try
            {
                using var testSocket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.Unix,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Unspecified);
                testSocket.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(options.SocketPath));
                _socketAvailable = true;
            }
            catch
            {
                _socketAvailable = false;
            }
        }

        if (_socketAvailable)
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, ct) =>
                {
                    var socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.Unix,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Unspecified);
                    await socket.ConnectAsync(
                        new System.Net.Sockets.UnixDomainSocketEndPoint(options.SocketPath), ct);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                },
            };
            _podmanClient = new HttpClient(handler) { BaseAddress = new Uri(PodmanApiBase) };
        }
        else
        {
            _podmanClient = new HttpClient();
        }

        // Separate HttpClient for registry API calls over the internet
        _registryClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task<IReadOnlyList<ImageUpdateStatus>> CheckUpdatesAsync(
        IReadOnlyList<ContainerInfo> containers,
        CancellationToken ct)
    {
        if (!_socketAvailable) return [];

        var uniqueImages = containers
            .Select(c => c.Image)
            .Where(img => !string.IsNullOrWhiteSpace(img) && img != "unknown")
            .Distinct()
            .ToList();

        var tasks = uniqueImages.Select(image => CheckSingleImageAsync(image, ct));
        var results = await Task.WhenAll(tasks);

        return results.ToList();
    }

    private async Task<ImageUpdateStatus> CheckSingleImageAsync(string imageReference, CancellationToken cancellationToken)
    {
        // Check cache first
        if (_cache.TryGetValue(imageReference, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < CacheDuration)
        {
            return cached.Status;
        }

        try
        {
            var localDigest = await GetLocalDigestAsync(imageReference, cancellationToken);
            if (string.IsNullOrEmpty(localDigest))
            {
                var noDigestStatus = new ImageUpdateStatus(
                    imageReference, "", null, false, DateTimeOffset.UtcNow,
                    "Could not determine local image digest");
                _cache[imageReference] = (noDigestStatus, DateTimeOffset.UtcNow);
                return noDigestStatus;
            }

            var parsed = ParseImageReference(imageReference);
            var remoteDigest = await GetRemoteDigestAsync(parsed, cancellationToken);

            var updateAvailable = !string.IsNullOrEmpty(remoteDigest)
                && !string.IsNullOrEmpty(localDigest)
                && !string.Equals(localDigest, remoteDigest, StringComparison.OrdinalIgnoreCase);

            var status = new ImageUpdateStatus(
                imageReference, localDigest, remoteDigest, updateAvailable,
                DateTimeOffset.UtcNow, null);
            _cache[imageReference] = (status, DateTimeOffset.UtcNow);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check image update for {Image}", imageReference);
            var errorStatus = new ImageUpdateStatus(
                imageReference, "", null, false, DateTimeOffset.UtcNow, ex.Message);
            _cache[imageReference] = (errorStatus, DateTimeOffset.UtcNow);
            return errorStatus;
        }
    }

    private async Task<string> GetLocalDigestAsync(string imageReference, CancellationToken cancellationToken)
    {
        try
        {
            var encodedImage = Uri.EscapeDataString(imageReference);
            var response = await _podmanClient.GetAsync(
                $"{PodmanApiBase}/images/{encodedImage}/json", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var imageInfo = JsonSerializer.Deserialize<PodmanImageInspect>(json, JsonOptions);

            // Try RepoDigests first — these contain the registry digest
            if (imageInfo?.RepoDigests is { Count: > 0 })
            {
                foreach (var repoDigest in imageInfo.RepoDigests)
                {
                    var atIndex = repoDigest.IndexOf('@');
                    if (atIndex >= 0)
                    {
                        return repoDigest[(atIndex + 1)..];
                    }
                }
            }

            // Fall back to the image Digest field
            if (!string.IsNullOrEmpty(imageInfo?.Digest))
            {
                return imageInfo.Digest;
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get local digest for {Image}", imageReference);
            return "";
        }
    }

    private async Task<string?> GetRemoteDigestAsync(ParsedImageReference parsed, CancellationToken cancellationToken)
    {
        try
        {
            // Get auth token for Docker Hub
            string? authToken = null;
            if (parsed.Registry is "registry-1.docker.io")
            {
                authToken = await GetDockerHubTokenAsync(parsed.Repository, cancellationToken);
            }

            var manifestUrl = $"https://{parsed.Registry}/v2/{parsed.Repository}/manifests/{parsed.Tag}";

            using var request = new HttpRequestMessage(HttpMethod.Head, manifestUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "application/vnd.docker.distribution.manifest.v2+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "application/vnd.oci.image.manifest.v1+json"));

            if (authToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }

            using var response = await _registryClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Headers.TryGetValues("Docker-Content-Digest", out var digestValues))
            {
                return digestValues.FirstOrDefault();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get remote digest for {Registry}/{Repo}:{Tag}",
                parsed.Registry, parsed.Repository, parsed.Tag);
            return null;
        }
    }

    private async Task<string?> GetDockerHubTokenAsync(string repository, CancellationToken ct)
    {
        try
        {
            var tokenUrl = $"https://auth.docker.io/token?service=registry.docker.io&scope=repository:{repository}:pull";
            var response = await _registryClient.GetAsync(tokenUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var tokenResponse = JsonSerializer.Deserialize<DockerAuthTokenResponse>(json, JsonOptions);
            return tokenResponse?.Token;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Docker Hub auth token for {Repository}", repository);
            return null;
        }
    }

    internal static ParsedImageReference ParseImageReference(string imageReference)
    {
        var reference = imageReference;
        var tag = "latest";

        // Extract tag if present (but not from digest references)
        var colonIndex = reference.LastIndexOf(':');
        if (colonIndex > 0 && !reference[colonIndex..].Contains('/'))
        {
            tag = reference[(colonIndex + 1)..];
            reference = reference[..colonIndex];
        }

        // Determine if the reference includes a registry
        string registry;
        string repository;

        var firstSlash = reference.IndexOf('/');
        if (firstSlash < 0)
        {
            // Simple name like "nginx" -> docker.io/library/nginx
            registry = "registry-1.docker.io";
            repository = $"library/{reference}";
        }
        else
        {
            var possibleRegistry = reference[..firstSlash];
            if (possibleRegistry.Contains('.') || possibleRegistry.Contains(':'))
            {
                // Fully qualified like "ghcr.io/user/repo" or "registry:5000/repo"
                registry = possibleRegistry;
                repository = reference[(firstSlash + 1)..];

                // Docker Hub uses docker.io as the reference but registry-1.docker.io for API calls
                if (registry is "docker.io")
                {
                    registry = "registry-1.docker.io";
                    // docker.io/nginx -> library/nginx
                    if (!repository.Contains('/'))
                    {
                        repository = $"library/{repository}";
                    }
                }
            }
            else
            {
                // Looks like a Docker Hub user/repo, e.g. "myuser/myapp"
                registry = "registry-1.docker.io";
                repository = reference;
            }
        }

        return new ParsedImageReference(registry, repository, tag);
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    public void Dispose()
    {
        _podmanClient.Dispose();
        _registryClient.Dispose();
    }

    internal sealed record ParsedImageReference(string Registry, string Repository, string Tag);

    private sealed class PodmanImageInspect
    {
        public string? Digest { get; set; }

        public List<string>? RepoDigests { get; set; }
    }

    private sealed class DockerAuthTokenResponse
    {
        public string? Token { get; set; }
    }

}
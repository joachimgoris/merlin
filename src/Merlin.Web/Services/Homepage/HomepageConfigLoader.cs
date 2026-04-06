using System.Text.Json;
using Merlin.Web.Models;

namespace Merlin.Web.Services.Homepage;

public sealed class HomepageConfigLoader(string configFilePath, ILogger<HomepageConfigLoader> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Lock _lock = new();
    private DateTimeOffset _lastModified = DateTimeOffset.MinValue;
    private List<HomepageConfigEntry> _cached = [];

    public List<HomepageConfigEntry> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(configFilePath))
                {
                    _cached = [];
                    _lastModified = DateTimeOffset.MinValue;
                    return _cached;
                }

                var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(configFilePath), TimeSpan.Zero);

                if (mtime == _lastModified && _cached.Count > 0)
                {
                    return _cached;
                }

                var json = File.ReadAllText(configFilePath);
                var config = JsonSerializer.Deserialize<HomepageConfig>(json, JsonOptions);

                _cached = config?.Services ?? [];
                _lastModified = mtime;

                logger.LogInformation("Loaded {Count} homepage config entries from {Path}", _cached.Count, configFilePath);

                return _cached;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Invalid JSON in homepage config file {Path}", configFilePath);
                _cached = [];
                _lastModified = DateTimeOffset.MinValue;
                return _cached;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read homepage config file {Path}", configFilePath);
                return _cached;
            }
        }
    }
}

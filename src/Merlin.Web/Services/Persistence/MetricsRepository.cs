using System.Text.Json;
using Merlin.Web.Models;
using Microsoft.Data.Sqlite;

namespace Merlin.Web.Services.Persistence;

public sealed class MetricsRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public MetricsRepository(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        Initialize();
    }

    private void Initialize()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragmaCmd.ExecuteNonQuery();

        using var createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS metrics_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                cpu_percent REAL NOT NULL,
                mem_used_bytes INTEGER NOT NULL,
                mem_total_bytes INTEGER NOT NULL,
                payload_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_metrics_timestamp ON metrics_snapshots(timestamp);
            """;
        createCmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public async Task InsertBatchAsync(
        IEnumerable<SystemMetrics> snapshots,
        CancellationToken cancellationToken = default)
    {
        var items = snapshots.ToList();
        if (items.Count == 0) return;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO metrics_snapshots (timestamp, cpu_percent, mem_used_bytes, mem_total_bytes, payload_json)
                VALUES (@timestamp, @cpuPercent, @memUsed, @memTotal, @payload)
                """;

            var timestampParam = cmd.Parameters.Add("@timestamp", SqliteType.Text);
            var cpuParam = cmd.Parameters.Add("@cpuPercent", SqliteType.Real);
            var memUsedParam = cmd.Parameters.Add("@memUsed", SqliteType.Integer);
            var memTotalParam = cmd.Parameters.Add("@memTotal", SqliteType.Integer);
            var payloadParam = cmd.Parameters.Add("@payload", SqliteType.Text);

            foreach (var snapshot in items)
            {
                timestampParam.Value = snapshot.Timestamp.ToString("O");
                cpuParam.Value = snapshot.Cpu.TotalUsagePercent;
                memUsedParam.Value = snapshot.Memory.UsedBytes;
                memTotalParam.Value = snapshot.Memory.TotalBytes;
                payloadParam.Value = JsonSerializer.Serialize(snapshot, _jsonOptions);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<SystemMetrics>> GetRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT payload_json FROM metrics_snapshots
            WHERE timestamp >= @from AND timestamp <= @to
            ORDER BY timestamp ASC
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("O"));
        cmd.Parameters.AddWithValue("@to", to.ToString("O"));

        var results = new List<SystemMetrics>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var json = reader.GetString(0);
            var metrics = JsonSerializer.Deserialize<SystemMetrics>(json, _jsonOptions);
            if (metrics is not null)
                results.Add(metrics);
        }

        return results;
    }

    public async Task PruneAsync(
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM metrics_snapshots WHERE timestamp < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DateTimeOffset?> GetLatestTimestampAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(timestamp) FROM metrics_snapshots";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);

        if (result is null or DBNull)
            return null;

        return DateTimeOffset.Parse((string)result);
    }

    public async Task<IReadOnlyList<SystemMetrics>> LoadRecentAsync(
        TimeSpan lookback,
        CancellationToken cancellationToken = default)
    {
        var from = DateTimeOffset.UtcNow - lookback;
        var to = DateTimeOffset.UtcNow;
        return await GetRangeAsync(from, to, cancellationToken);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}

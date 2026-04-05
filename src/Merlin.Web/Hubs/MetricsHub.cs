using System.Collections.Concurrent;
using System.Text;
using Merlin.Web.Services.Containers;
using Microsoft.AspNetCore.SignalR;

namespace Merlin.Web.Hubs;

public sealed class MetricsHub(
    IContainerService containerService,
    ILogger<MetricsHub> logger) : Hub
{
    private static readonly ConcurrentDictionary<string, TerminalSession> ActiveSessions = new();

    public async Task StartContainer(string id)
    {
        try
        {
            await containerService.StartAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start container {Id}", id);
            throw new HubException("Failed to start container.");
        }
    }

    public async Task StopContainer(string id)
    {
        try
        {
            await containerService.StopAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop container {Id}", id);
            throw new HubException("Failed to stop container.");
        }
    }

    public async Task RestartContainer(string id)
    {
        try
        {
            await containerService.RestartAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to restart container {Id}", id);
            throw new HubException("Failed to restart container.");
        }
    }

    public async IAsyncEnumerable<string> StreamContainerLogs(
        string containerId,
        int tail = 100,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        tail = Math.Clamp(tail, 1, 5000);

        await foreach (var line in containerService.StreamLogsAsync(containerId, tail, ct))
        {
            yield return line;
        }
    }

    public async Task StartTerminal(string containerId)
    {
        var connectionId = Context.ConnectionId;

        try
        {
            // Clean up any existing session for this connection
            await CleanupSessionAsync(connectionId);

            var execId = await containerService.CreateExecAsync(containerId, ["/bin/sh"]);
            var stream = await containerService.StartExecAsync(execId);
            var cts = new CancellationTokenSource();

            var session = new TerminalSession(execId, stream, cts);
            ActiveSessions[connectionId] = session;

            // Start background reader
            _ = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, cts.Token);
                        if (bytesRead == 0) break;

                        var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        await Clients.Client(connectionId).SendAsync("TerminalOutput", data, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on cancellation
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Terminal stream ended for connection {ConnectionId}", connectionId);
                }
                finally
                {
                    await CleanupSessionAsync(connectionId);
                    try
                    {
                        await Clients.Client(connectionId).SendAsync("TerminalOutput", "\r\n--- session ended ---\r\n");
                    }
                    catch
                    {
                        // Connection may already be gone
                    }
                }
            }, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start terminal for container {ContainerId}", containerId);
            throw new HubException("Failed to start terminal session.");
        }
    }

    public async Task TerminalInput(string data)
    {
        if (!ActiveSessions.TryGetValue(Context.ConnectionId, out var session)) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await session.Stream.WriteAsync(bytes, session.Cts.Token);
            await session.Stream.FlushAsync(session.Cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to write terminal input for connection {ConnectionId}", Context.ConnectionId);
        }
    }

    public async Task TerminalResize(int cols, int rows)
    {
        if (!ActiveSessions.TryGetValue(Context.ConnectionId, out var session)) return;

        try
        {
            await containerService.ResizeExecAsync(session.ExecId, cols, rows);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resize terminal for connection {ConnectionId}", Context.ConnectionId);
        }
    }

    public async Task StopTerminal()
    {
        await CleanupSessionAsync(Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await CleanupSessionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static async Task CleanupSessionAsync(string connectionId)
    {
        if (!ActiveSessions.TryRemove(connectionId, out var session)) return;

        await session.Cts.CancelAsync();
        session.Cts.Dispose();

        try
        {
            session.Stream.Dispose();
        }
        catch
        {
            // Stream may already be closed
        }
    }

    private sealed class TerminalSession(string execId, Stream stream, CancellationTokenSource cts)
    {
        public string ExecId { get; } = execId;
        public Stream Stream { get; } = stream;
        public CancellationTokenSource Cts { get; } = cts;
    }
}

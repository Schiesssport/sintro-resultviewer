using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Sintro.ResultViewer.Live;

/// <summary>
/// Fans lane state out to connected viewers. Each client owns a single slot (newest payload wins) and its own pump;
/// Broadcast only fills slots and never awaits a socket, so one stalled display cannot hold up the others.
/// </summary>
public sealed class LiveHub(ILogger<LiveHub> logger)
{
    // Two updates a second is plenty for a wall display, and it keeps a burst from becoming a burst of syscalls.
    private static readonly TimeSpan MinimumSendInterval = TimeSpan.FromMilliseconds(500);

    // A send that takes this long means the far end is gone, whatever TCP thinks.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    public int ClientCount => _clients.Count;

    private sealed record Client(WebSocket Socket, Channel<ReadOnlyMemory<byte>> Pending);

    private static Channel<ReadOnlyMemory<byte>> NewSlot() =>
        Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    /// <summary>Registers the socket, sends it <paramref name="initialPayload"/>, and returns when the client goes away.</summary>
    public async Task AcceptAsync<T>(WebSocket socket, T initialPayload, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var client = new Client(socket, NewSlot());
        _clients[id] = client;
        logger.LogInformation("Live client {Id} connected ({Count} total)", id, _clients.Count);

        client.Pending.Writer.TryWrite(Serialize(initialPayload));
        var pump = PumpAsync(id, client, token);

        try
        {
            await WaitUntilClosedAsync(socket, token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down: a display that never answers a close handshake would hold the process open until Kestrel's timeout.
            Abort(client);
        }
        catch (WebSocketException)
        {
            // Client vanished.
        }
        finally
        {
            Remove(id);
            await pump;
        }
    }

    // Nothing is expected from the client; this parks until it goes away.
    private static async Task WaitUntilClosedAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[256];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return;
        }
    }

    public void Broadcast<T>(T payload)
    {
        var json = Serialize(payload);

        foreach (var (id, client) in _clients)
        {
            if (client.Socket.State != WebSocketState.Open)
            {
                Remove(id);
                continue;
            }

            client.Pending.Writer.TryWrite(json);
        }
    }

    // One writer per socket, which is also what keeps SendAsync calls from overlapping.
    private async Task PumpAsync(Guid id, Client client, CancellationToken token)
    {
        var sinceLastSend = Stopwatch.StartNew();
        var hasSent = false;

        try
        {
            await foreach (var frame in client.Pending.Reader.ReadAllAsync(token))
            {
                if (hasSent && sinceLastSend.Elapsed < MinimumSendInterval)
                    await Task.Delay(MinimumSendInterval - sinceLastSend.Elapsed, token);

                await SendWithTimeoutAsync(client.Socket, frame, token);
                sinceLastSend.Restart();
                hasSent = true;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            logger.LogInformation("Live client {Id} stopped reading; dropping it", id);
            Abort(client);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            Abort(client);
        }
        finally
        {
            Remove(id);
        }
    }

    private static async Task SendWithTimeoutAsync(WebSocket socket, ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(SendTimeout);
        await socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
    }

    private void Remove(Guid id)
    {
        if (!_clients.TryRemove(id, out var client)) return;

        client.Pending.Writer.TryComplete();
        logger.LogInformation("Live client {Id} disconnected ({Count} remaining)", id, _clients.Count);
    }

    // Abort, not CloseAsync: a client that is not draining will not complete a close handshake either.
    private static void Abort(Client client)
    {
        try { client.Socket.Abort(); } catch (ObjectDisposedException) { }
    }

    private static ReadOnlyMemory<byte> Serialize<T>(T payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, SintroJson.Options);
}

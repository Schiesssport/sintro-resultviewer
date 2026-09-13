using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Sintro.ResultViewer.Live;

/// <summary>
/// Tracks connected viewers and pushes state to them.
///
/// Every client owns a **single slot** — not a queue — and its own pump. Broadcasting only fills
/// the slot, so one wall display on a half-dead TCP connection cannot hold up the others or stall
/// the lane watcher while the OS works its way to a timeout.
///
/// A slot rather than a queue because these payloads are complete snapshots of current state: a
/// backlog would only show a display the past more slowly, and after a stall it would have to
/// replay frames that were already obsolete when they were queued. Whatever arrives while a client
/// is busy simply replaces what was waiting, so it always resumes on the newest state.
/// </summary>
public sealed class LiveHub(ILogger<LiveHub> logger)
{
    /// <summary>
    /// Floor between two sends to one client. Nothing here is worth more than two updates a
    /// second on a wall display, and it keeps a burst from becoming a burst of syscalls.
    /// </summary>
    private static readonly TimeSpan MinimumSendInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>A send that takes longer than this means the far end is gone, whatever TCP thinks.</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    public int ClientCount => _clients.Count;

    private sealed record Client(WebSocket Socket, Channel<ReadOnlyMemory<byte>> Pending);

    /// <summary>Capacity one, newest wins: the slot described above.</summary>
    private static Channel<ReadOnlyMemory<byte>> NewSlot() =>
        Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    /// <summary>
    /// Registers the socket, hands <paramref name="initialPayload"/> to it alone, and stays until
    /// the client goes away.
    /// </summary>
    public async Task AcceptAsync<T>(WebSocket socket, T initialPayload, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var client = new Client(socket, NewSlot());

        _clients[id] = client;
        logger.LogInformation("Live client {Id} connected ({Count} total)", id, _clients.Count);

        // The joining client needs the current state immediately; a broadcast would not reach it
        // any sooner (it is not registered yet) and would disturb everyone else.
        client.Pending.Writer.TryWrite(Serialize(initialPayload));

        var pump = PumpAsync(id, client, token);

        try
        {
            // Nothing is expected from the client; this just parks until it goes away.
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (WebSocketException)
        {
            // Client vanished; nothing to recover.
        }
        finally
        {
            Remove(id);
            await pump;
        }
    }

    /// <summary>Offers a payload to every client. Never waits on a socket.</summary>
    public Task BroadcastAsync<T>(T payload, CancellationToken token)
    {
        if (_clients.IsEmpty) return Task.CompletedTask;

        var json = Serialize(payload);

        foreach (var (id, client) in _clients)
        {
            if (client.Socket.State != WebSocketState.Open)
            {
                Remove(id);
                continue;
            }

            // Replaces whatever was waiting; cannot block however far behind the client is.
            client.Pending.Writer.TryWrite(json);
        }

        return Task.CompletedTask;
    }

    /// <summary>One writer per socket, which is also what keeps SendAsync calls from overlapping.</summary>
    private async Task PumpAsync(Guid id, Client client, CancellationToken token)
    {
        var sinceLastSend = Stopwatch.StartNew();
        var hasSent = false;

        try
        {
            await foreach (var frame in client.Pending.Reader.ReadAllAsync(token))
            {
                // Hold the floor between sends. Anything newer that arrives while waiting
                // replaces the slot, so the client resumes on the latest state, not a backlog.
                if (hasSent && sinceLastSend.Elapsed < MinimumSendInterval)
                    await Task.Delay(MinimumSendInterval - sinceLastSend.Elapsed, token);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(SendTimeout);

                await client.Socket.SendAsync(
                    frame, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);

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

    private void Remove(Guid id)
    {
        if (!_clients.TryRemove(id, out var client)) return;

        client.Pending.Writer.TryComplete();
        logger.LogInformation("Live client {Id} disconnected ({Count} remaining)", id, _clients.Count);
    }

    private static void Abort(Client client)
    {
        // Abort, not CloseAsync: a client that is not draining will not complete a handshake
        // either, and waiting for one is the stall this whole design exists to avoid.
        try { client.Socket.Abort(); } catch (ObjectDisposedException) { }
    }

    private static ReadOnlyMemory<byte> Serialize<T>(T payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

    // Must serialize identically to the HTTP endpoints (Program.cs), or the same program
    // would carry "active" over REST and "Active" over the socket.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

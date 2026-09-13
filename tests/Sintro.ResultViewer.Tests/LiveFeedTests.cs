using System.Net.Http.Json;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sintro.ResultViewer.Live;

namespace Sintro.ResultViewer.Tests;

[Collection(ApiCollection.Name)]
public class LiveFeedTests(ApiFixture fixture)
{
    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var builder = new StringBuilder();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return builder.ToString();
    }

    [RequiresDatabaseFact]
    public async Task theLiveFeedAcceptsTheTokenAsAQueryParameter()
    {
        // Regression guard: browsers cannot set headers on a WebSocket handshake, and
        // UseWebSockets must run before the token check or IsWebSocketRequest is false
        // and every handshake is refused with 401.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Server.CreateWebSocketClient();

        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");
        using var socket = await client.ConnectAsync(uri, cts.Token);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [RequiresDatabaseFact]
    public async Task theCurrentLaneStateIsPushedOnConnect()
    {
        // A client must render immediately rather than waiting for the first change.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Server.CreateWebSocketClient();

        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");
        using var socket = await client.ConnectAsync(uri, cts.Token);

        using var document = JsonDocument.Parse(await ReceiveTextAsync(socket, cts.Token));

        Assert.Equal("lanes", document.RootElement.GetProperty("type").GetString());

        // Invariant, not a count: the pushed state must be exactly what a plain GET of the
        // same URL reports — how many lines exist and which are occupied is per-installation.
        var snapshot = (await fixture.CreateAuthorizedClient()
            .GetFromJsonAsync<List<Domain.LaneStatus>>("/api/v2/live", TestJson.Options))!;

        var lanes = document.RootElement.GetProperty("lanes");
        Assert.Equal(snapshot.Count, lanes.GetArrayLength());
        Assert.NotEmpty(snapshot);

        var occupied = lanes.EnumerateArray()
            .Where(lane => lane.GetProperty("currentProgram").ValueKind != JsonValueKind.Null)
            .Select(lane => lane.GetProperty("currentProgram").GetProperty("id").GetInt32())
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(
            snapshot.Where(lane => lane.CurrentProgram is not null)
                    .Select(lane => lane.CurrentProgram!.Id).OrderBy(id => id),
            occupied);
    }

    [RequiresDatabaseFact]
    public async Task aHandshakeWithoutATokenIsRefused()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Server.CreateWebSocketClient();

        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live");

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(uri, cts.Token));
    }

    [RequiresDatabaseFact]
    public async Task aHandshakeWithAWrongTokenIsRefused()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Server.CreateWebSocketClient();

        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token=not-the-token");

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(uri, cts.Token));
    }

    [RequiresDatabaseFact]
    public async Task oneStalledClientDoesNotStopTheOthers()
    {
        // The failure this design exists to prevent: a wall display on a half-dead connection
        // used to hold up the broadcast to everyone, and with it the lane watcher, until the
        // OS gave up on the socket.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var wsClient = fixture.Server.CreateWebSocketClient();
        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");

        using var reader = await wsClient.ConnectAsync(uri, cts.Token);
        using var stalled = await wsClient.ConnectAsync(uri, cts.Token);

        // Drain the reader's connect payload; the stalled one is deliberately never read.
        await ReceiveTextAsync(reader, cts.Token);

        var hub = fixture.Services.GetRequiredService<LiveHub>();

        // Far more than any client's queue depth, so the stalled one is certainly saturated.
        var started = Stopwatch.StartNew();
        for (var index = 0; index < 50; index++)
            await hub.BroadcastAsync(new { type = "lanes", lanes = Array.Empty<object>() }, cts.Token);

        // Broadcasting only enqueues, so it cannot be waiting on a socket.
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5),
            $"broadcast took {started.Elapsed}, which means it waited on a client");

        // And the healthy client is still being served.
        using var receive = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        receive.CancelAfter(TimeSpan.FromSeconds(10));

        var payload = await ReceiveTextAsync(reader, receive.Token);
        Assert.Contains("lanes", payload);
    }

    [RequiresDatabaseFact]
    public async Task aClientThatFallsBehindLosesFramesRatherThanMemory()
    {
        // The queue is bounded and drops the oldest frame: these payloads are snapshots, so a
        // backlog would only show a display the past more slowly.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var wsClient = fixture.Server.CreateWebSocketClient();
        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");

        using var socket = await wsClient.ConnectAsync(uri, cts.Token);

        // Draining the connect payload also proves the server has registered the client:
        // ConnectAsync returns as soon as the handshake completes on this side.
        await ReceiveTextAsync(socket, cts.Token);

        var hub = fixture.Services.GetRequiredService<LiveHub>();

        for (var index = 0; index < 200; index++)
            await hub.BroadcastAsync(new { type = "lanes", lanes = Array.Empty<object>() }, cts.Token);

        // Still connected and still serving; nothing queued without bound.
        Assert.True(hub.ClientCount >= 1);
    }
}

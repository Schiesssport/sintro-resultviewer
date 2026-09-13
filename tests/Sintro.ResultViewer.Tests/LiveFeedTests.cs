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
        // Guards the UseWebSockets-before-UseTokenAuth order: reversed, every handshake is 401.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Server.CreateWebSocketClient();

        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");
        using var socket = await client.ConnectAsync(uri, cts.Token);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Theory]
    [InlineData("/api/v2/live")]
    [InlineData("/api/v2/programs")]
    public async Task aPlainGetDoesNotAcceptTheTokenInTheQueryString(string path)
    {
        // Query-string tokens end up in logs; the exception exists for the WebSocket handshake alone.
        var response = await fixture.CreateClient().GetAsync($"{path}?token={ApiFixture.Token}");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task theCurrentLaneStateIsPushedOnConnect()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Server.CreateWebSocketClient();

        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");
        using var socket = await client.ConnectAsync(uri, cts.Token);

        using var document = JsonDocument.Parse(await ReceiveTextAsync(socket, cts.Token));

        Assert.Equal("lanes", document.RootElement.GetProperty("type").GetString());

        // Invariant, not a count: the pushed state must equal a plain GET of the same URL.
        var snapshot = (await fixture.CreateAuthorizedClient()
            .GetFromJsonAsync<List<Domain.LaneStatus>>("/api/v2/live", SintroJson.Options))!;

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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var wsClient = fixture.Server.CreateWebSocketClient();
        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");

        using var reader = await wsClient.ConnectAsync(uri, cts.Token);
        using var stalled = await wsClient.ConnectAsync(uri, cts.Token);

        // The stalled client is deliberately never read.
        await ReceiveTextAsync(reader, cts.Token);

        var hub = fixture.Services.GetRequiredService<LiveHub>();

        var started = Stopwatch.StartNew();
        for (var index = 0; index < 50; index++)
            hub.Broadcast(new { type = "lanes", lanes = Array.Empty<object>() });

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5),
            $"broadcast took {started.Elapsed}, which means it waited on a client");

        using var receive = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        receive.CancelAfter(TimeSpan.FromSeconds(10));

        var payload = await ReceiveTextAsync(reader, receive.Token);
        Assert.Contains("lanes", payload);
    }

    [RequiresDatabaseFact]
    public async Task aClientThatFallsBehindLosesFramesRatherThanMemory()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var wsClient = fixture.Server.CreateWebSocketClient();
        var uri = new Uri($"{fixture.Server.BaseAddress}api/v2/live?token={ApiFixture.Token}");

        using var socket = await wsClient.ConnectAsync(uri, cts.Token);

        // Draining the connect payload proves the server has registered the client; ConnectAsync alone does not.
        await ReceiveTextAsync(socket, cts.Token);

        var hub = fixture.Services.GetRequiredService<LiveHub>();

        for (var index = 0; index < 200; index++)
            hub.Broadcast(new { type = "lanes", lanes = Array.Empty<object>() });

        Assert.True(hub.ClientCount >= 1);
    }
}

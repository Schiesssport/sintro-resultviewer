using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Live;

/// <summary>Polls the lane state and broadcasts it when it changes. Polling because the range PC's SQL Express has no Service Broker or CDC.</summary>
public sealed class LaneWatcher(
    SintroRepository repository,
    LiveHub hub,
    IOptions<SintroOptions> options,
    ILogger<LaneWatcher> logger) : BackgroundService
{
    private string? _lastFingerprint;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(250, options.Value.Live.PollMilliseconds));
        using var timer = new PeriodicTimer(interval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            if (hub.ClientCount == 0)
            {
                _lastFingerprint = null;
                continue;
            }

            try
            {
                await PushIfChangedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Includes a cancelled read: SqlClient reports a connection lost mid-query as OperationCanceledException too.
                logger.LogWarning(ex, "Lane watcher poll failed");
                _lastFingerprint = null;
            }
        }
    }

    private async Task PushIfChangedAsync(CancellationToken token)
    {
        var fingerprint = await repository.ReadLiveFingerprintAsync(token);
        if (fingerprint == _lastFingerprint) return;

        _lastFingerprint = fingerprint;
        hub.Broadcast(new { type = "lanes", lanes = await repository.ListLanesAsync(token) });
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

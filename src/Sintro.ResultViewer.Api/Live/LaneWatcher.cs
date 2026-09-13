using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Live;

/// <summary>
/// Polls the lane assignments and the highest shot id, and pushes the lane view whenever either
/// changes. Polling rather than change-tracking on purpose: the range PC's SQL Express has no
/// Service Broker or CDC configured, and six lanes at one second is negligible load.
/// </summary>
public sealed class LaneWatcher(
    SintroRepository repository,
    LiveHub hub,
    IOptions<SintroOptions> options,
    ILogger<LaneWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(250, options.Value.Live.PollMilliseconds));
        string? lastFingerprint = null;

        using var timer = new PeriodicTimer(interval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            if (hub.ClientCount == 0)
            {
                // Nobody is watching; don't touch the device database at all.
                lastFingerprint = null;
                continue;
            }

            try
            {
                var fingerprint = await repository.ReadLiveFingerprintAsync(stoppingToken);
                if (fingerprint == lastFingerprint) continue;

                lastFingerprint = fingerprint;
                var lanes = await repository.ListLanesAsync(stoppingToken);

                await hub.BroadcastAsync(new { type = "lanes", lanes }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient DB blip must not kill the watcher; retry on the next tick.
                logger.LogWarning(ex, "Lane watcher poll failed");
                lastFingerprint = null;
            }
        }
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

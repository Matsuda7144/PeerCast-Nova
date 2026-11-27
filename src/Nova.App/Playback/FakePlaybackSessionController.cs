namespace PeerCast.Nova.App;

/// <summary>
/// Inert GUI-development controller. It performs no network, decoder, output,
/// readiness, diagnostic, or physical playback operation.
/// </summary>
public sealed class FakePlaybackSessionController : IPlaybackSessionController
{
    private readonly object _sync = new();
    private PlaybackSessionSnapshot _snapshot = PlaybackSessionSnapshot.Stopped;

    public PlaybackSessionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
        string source,
        CancellationToken cancellationToken = default) =>
        PlayAsync(new PlaybackSessionRequest(source), cancellationToken);

    public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
        PlaybackSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Cancelled,
                Snapshot));
        }

        var normalized = request.Source?.Trim() ?? string.Empty;
        lock (_sync)
        {
            if (normalized.Length == 0)
            {
                _snapshot = _snapshot with
                {
                    ErrorMessage = "A channel or source is required.",
                };
                return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                    PlaybackSessionCommandResult.Rejected,
                    _snapshot));
            }

            if (request.KeepExistingSessions && _snapshot.State == PlaybackSessionState.Playing)
            {
                var additionalSnapshot = new PlaybackSessionSnapshot(
                    PlaybackSessionState.Playing,
                    normalized,
                    null)
                {
                    ChannelName = string.IsNullOrWhiteSpace(request.ChannelName)
                        ? normalized
                        : request.ChannelName.Trim(),
                    ChannelId = request.ChannelId?.Trim(),
                };
                return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                    PlaybackSessionCommandResult.Completed,
                    additionalSnapshot));
            }

            if (_snapshot.State is PlaybackSessionState.Starting or
                PlaybackSessionState.Playing or
                PlaybackSessionState.Stopping)
            {
                return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                    PlaybackSessionCommandResult.Rejected,
                    _snapshot));
            }

            _snapshot = new PlaybackSessionSnapshot(
                PlaybackSessionState.Playing,
                normalized,
                null)
            {
                ChannelName = string.IsNullOrWhiteSpace(request.ChannelName)
                    ? normalized
                    : request.ChannelName.Trim(),
                ChannelId = request.ChannelId?.Trim(),
            };
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                _snapshot));
        }
    }

    public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Cancelled,
                Snapshot));
        }

        lock (_sync)
        {
            _snapshot = PlaybackSessionSnapshot.Stopped;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                _snapshot));
        }
    }
}

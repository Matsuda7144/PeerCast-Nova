namespace PeerCast.Nova.App;

public enum PlaybackSessionState
{
    Stopped,
    Starting,
    Playing,
    Stopping,
    Faulted,
}

public enum PlaybackSessionCommandResult
{
    Completed,
    Rejected,
    Cancelled,
    Faulted,
}

public sealed record PlaybackSessionSnapshot(
    PlaybackSessionState State,
    string? Source,
    string? ErrorMessage)
{
    public string? ChannelName { get; init; }

    public string? ChannelId { get; init; }

    public static PlaybackSessionSnapshot Stopped { get; } =
        new(PlaybackSessionState.Stopped, null, null);
}

public sealed record PlaybackSessionCommandOutcome(
    PlaybackSessionCommandResult Result,
    PlaybackSessionSnapshot Snapshot);

public sealed record PlaybackSessionRequest(
    string Source,
    string? ChannelName = null,
    string? ChannelId = null)
{
    public GenesisChannelItem? CatalogChannel { get; init; }
}

internal interface IPlaybackSessionSnapshotNotifications
{
    event EventHandler? SnapshotChanged;
}

/// <summary>
/// Narrow application-facing playback boundary used by the Genesis GUI.
/// The GUI owns no playback implementation detail. Production composition
/// adapts the existing daily external-player controller; tests can inject an
/// inert fake.
/// </summary>
public interface IPlaybackSessionController
{
    PlaybackSessionSnapshot Snapshot { get; }

    ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
        PlaybackSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PlayAsync(request.Source, cancellationToken);
    }

    ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
        string source,
        CancellationToken cancellationToken = default);

    ValueTask<PlaybackSessionCommandOutcome> StopAsync(
        CancellationToken cancellationToken = default);
}

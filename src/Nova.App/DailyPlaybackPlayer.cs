using PeerCast.Nova.Playback;

namespace PeerCast.Nova.App;

internal sealed record DailyPlaybackPlayerSource(
    string StreamUrl,
    string PlaylistUrl,
    string? ChannelName,
    string? ContactUrl,
    PlaybackMediaContainer Container = PlaybackMediaContainer.FlashVideo);

internal interface IDailyPlaybackPlayerLauncher
{
    void ValidateConfiguration();

    ValueTask<IDailyPlaybackPlayerLifetime> LaunchAsync(
        DailyPlaybackPlayerSource source,
        CancellationToken cancellationToken);
}

internal interface IDailyPlaybackPlayerLifetime : IAsyncDisposable
{
    Task Completion { get; }
}

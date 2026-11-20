namespace PeerCast.Nova.App;

internal sealed record DailyPlaybackPlayerSource(
    string StreamUrl,
    string PlaylistUrl,
    string? ChannelName,
    string? ContactUrl);

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

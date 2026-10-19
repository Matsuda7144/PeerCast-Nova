namespace PeerCast.Nova.App;

public enum GenesisPlaybackWindowMode
{
    Normal,
    VideoOnly,
    Fullscreen,
}

public sealed record GenesisPlaybackWindowState(
    bool IsOpen,
    GenesisPlaybackWindowMode Mode,
    string? Source)
{
    public static GenesisPlaybackWindowState Closed { get; } =
        new(false, GenesisPlaybackWindowMode.Normal, null);

    public static GenesisPlaybackWindowState Open(string source) =>
        new(true, GenesisPlaybackWindowMode.Normal, source);
}

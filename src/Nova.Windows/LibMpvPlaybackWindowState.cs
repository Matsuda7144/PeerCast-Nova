namespace PeerCast.Nova.Windows;

internal sealed record LibMpvPlaybackWindowState(
    int? Left = null,
    int? Top = null,
    int VideoWidth = 960,
    int VideoHeight = 540,
    int Volume = LibMpvPlaybackAudioState.MaxVolume,
    bool Muted = false,
    bool AlwaysOnTop = false,
    bool Borderless = false,
    bool BbsUseSage = true)
{
    public const int MinVideoWidth = 320;
    public const int MinVideoHeight = 180;
    public const int MaxVideoWidth = 7680;
    public const int MaxVideoHeight = 4320;
    public const int DefaultSnapDistance = 12;

    public static LibMpvPlaybackWindowState Default { get; } = new();

    public LibMpvPlaybackAudioState AudioState => new LibMpvPlaybackAudioState(Volume, Muted).Normalized();

    public LibMpvPlaybackWindowState Normalized() => this with
    {
        VideoWidth = Math.Clamp(VideoWidth, MinVideoWidth, MaxVideoWidth),
        VideoHeight = Math.Clamp(VideoHeight, MinVideoHeight, MaxVideoHeight),
        Volume = LibMpvPlaybackAudioState.ClampVolume(Volume),
    };

    public static (int Left, int Top) SnapToWorkArea(
        int left,
        int top,
        int width,
        int height,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        int snapDistance = DefaultSnapDistance)
    {
        var snappedLeft = left;
        var snappedTop = top;

        if (Math.Abs(left - workLeft) <= snapDistance)
        {
            snappedLeft = workLeft;
        }
        else if (Math.Abs((left + width) - workRight) <= snapDistance)
        {
            snappedLeft = workRight - width;
        }

        if (Math.Abs(top - workTop) <= snapDistance)
        {
            snappedTop = workTop;
        }
        else if (Math.Abs((top + height) - workBottom) <= snapDistance)
        {
            snappedTop = workBottom - height;
        }

        return (snappedLeft, snappedTop);
    }
}

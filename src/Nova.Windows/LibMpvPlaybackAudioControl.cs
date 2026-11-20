using System.Globalization;

namespace PeerCast.Nova.Windows;

internal readonly record struct LibMpvPlaybackAudioState(int Volume, bool Muted)
{
    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    public const int DefaultVolume = 100;
    public const int VolumeStep = 5;
    public const int WheelDeltaPerNotch = 120;

    public static LibMpvPlaybackAudioState Default { get; } = new(DefaultVolume, Muted: false);

    public static int ClampVolume(int volume) => Math.Clamp(volume, MinVolume, MaxVolume);

    public LibMpvPlaybackAudioState Normalized() => new(ClampVolume(Volume), Muted);

    public string FormatVolumeLabel() =>
        Muted
            ? "Muted"
            : string.Create(CultureInfo.InvariantCulture, $"{ClampVolume(Volume)}%");

    public string FormatMuteButtonText() => Muted ? "Unmute" : "Mute";
}

internal sealed class LibMpvPlaybackAudioController
{
    private readonly object _sync = new();
    private LibMpvPlaybackAudioState _state;
    private int _wheelDeltaRemainder;

    public LibMpvPlaybackAudioController(LibMpvPlaybackAudioState? initialState = null)
    {
        _state = (initialState ?? LibMpvPlaybackAudioState.Default).Normalized();
    }

    public event Action<LibMpvPlaybackAudioState>? StateChanged;

    public LibMpvPlaybackAudioState Current
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public bool SetVolume(int volume)
    {
        var clamped = LibMpvPlaybackAudioState.ClampVolume(volume);
        LibMpvPlaybackAudioState updated;
        lock (_sync)
        {
            var nextMuted = clamped > 0 && _state.Muted ? false : _state.Muted;
            updated = new(clamped, nextMuted);
            if (updated == _state)
            {
                return false;
            }

            _state = updated;
        }

        StateChanged?.Invoke(updated);
        return true;
    }

    public bool AdjustVolume(int delta)
    {
        if (delta == 0)
        {
            return false;
        }

        LibMpvPlaybackAudioState updated;
        lock (_sync)
        {
            var nextVolume = LibMpvPlaybackAudioState.ClampVolume(_state.Volume + delta);
            var nextMuted = delta > 0 && nextVolume > 0 ? false : _state.Muted;
            updated = new(nextVolume, nextMuted);
            if (updated == _state)
            {
                return false;
            }

            _state = updated;
        }

        StateChanged?.Invoke(updated);
        return true;
    }

    public bool ApplyWheelDelta(int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return false;
        }

        int notches;
        lock (_sync)
        {
            if ((_wheelDeltaRemainder > 0 && wheelDelta < 0) ||
                (_wheelDeltaRemainder < 0 && wheelDelta > 0))
            {
                _wheelDeltaRemainder = 0;
            }

            var accumulated = (long)_wheelDeltaRemainder + wheelDelta;
            notches = (int)(accumulated / LibMpvPlaybackAudioState.WheelDeltaPerNotch);
            _wheelDeltaRemainder = (int)(accumulated % LibMpvPlaybackAudioState.WheelDeltaPerNotch);
        }

        return notches != 0 && AdjustVolume(notches * LibMpvPlaybackAudioState.VolumeStep);
    }

    public bool SetMuted(bool muted)
    {
        LibMpvPlaybackAudioState updated;
        lock (_sync)
        {
            updated = _state with { Muted = muted };
            if (updated == _state)
            {
                return false;
            }

            _state = updated;
        }

        StateChanged?.Invoke(updated);
        return true;
    }

    public bool ToggleMute()
    {
        LibMpvPlaybackAudioState updated;
        lock (_sync)
        {
            updated = _state with { Muted = !_state.Muted };
            _state = updated;
        }

        StateChanged?.Invoke(updated);
        return true;
    }
}

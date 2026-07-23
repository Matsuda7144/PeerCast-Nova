namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsAudioRenderWriteResult
{
    None,
    Processed,
    WouldBlock,
    FormatUnsupported,
    ResizeRequired,
    DeviceLost,
    Failure,
}

public readonly record struct PlaybackWindowsAudioRenderWriteObservation(
    int RequestedFrameCount,
    int RequestedByteCount,
    bool HasCapacityFrameCount,
    int CapacityFrameCount,
    bool HasPaddingFrameCount,
    int PaddingFrameCount,
    bool HasAvailableFrameCount,
    int AvailableFrameCount,
    bool StartRequested,
    bool GetBufferAcknowledged,
    bool ReleaseBufferAcknowledged,
    bool StartAcknowledged,
    PlaybackWindowsAudioRenderWriteResult Result);

public readonly record struct PlaybackWindowsAudioRenderPreStopObservation(
    int QueryCount,
    bool Available,
    int PaddingFrameCount,
    bool Overflowed);

public readonly record struct PlaybackWindowsAudioRenderTraceSnapshot(
    int Count,
    bool Overflowed,
    PlaybackWindowsAudioRenderWriteObservation First,
    PlaybackWindowsAudioRenderWriteObservation Second,
    PlaybackWindowsAudioRenderWriteObservation Third,
    PlaybackWindowsAudioRenderWriteObservation Fourth,
    PlaybackWindowsAudioRenderPreStopObservation PreStop)
{
    public const int MaximumObservationCount = 4;

    public PlaybackWindowsAudioRenderWriteObservation GetObservation(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index switch
        {
            0 => First,
            1 => Second,
            2 => Third,
            3 => Fourth,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }
}

internal interface IPlaybackWindowsAudioRenderTraceSource
{
    PlaybackWindowsAudioRenderTraceSnapshot AudioRenderTrace { get; }
}

internal sealed class PlaybackWindowsAudioRenderTrace(bool capturePreStopPadding) :
    IPlaybackWindowsAudioRenderTraceSource
{
    private readonly object _sync = new();
    private readonly PlaybackWindowsAudioRenderWriteObservation[] _observations =
        new PlaybackWindowsAudioRenderWriteObservation[
            PlaybackWindowsAudioRenderTraceSnapshot.MaximumObservationCount];
    private readonly bool _capturePreStopPadding = capturePreStopPadding;
    private int _count;
    private bool _overflowed;
    private int _preStopQueryCount;
    private bool _preStopAvailable;
    private int _preStopPaddingFrameCount;
    private bool _preStopOverflowed;

    internal bool CapturePreStopPadding => _capturePreStopPadding;

    public PlaybackWindowsAudioRenderTraceSnapshot AudioRenderTrace
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _count,
                    _overflowed,
                    _count > 0 ? _observations[0] : default,
                    _count > 1 ? _observations[1] : default,
                    _count > 2 ? _observations[2] : default,
                    _count > 3 ? _observations[3] : default,
                    new(
                        _preStopQueryCount,
                        _preStopAvailable,
                        _preStopPaddingFrameCount,
                        _preStopOverflowed));
            }
        }
    }

    internal void RecordWrite(PlaybackWindowsAudioRenderWriteObservation observation)
    {
        if (!IsValid(observation))
        {
            lock (_sync)
            {
                _overflowed = true;
            }
            return;
        }

        lock (_sync)
        {
            if (_count >= _observations.Length)
            {
                _overflowed = true;
                return;
            }

            _observations[_count++] = observation;
        }
    }

    internal void RecordPreStopPadding(bool available, int paddingFrameCount)
    {
        lock (_sync)
        {
            if (_preStopQueryCount == int.MaxValue)
            {
                _preStopOverflowed = true;
                return;
            }

            _preStopQueryCount++;
            if (_preStopQueryCount > 1)
            {
                _preStopOverflowed = true;
                return;
            }

            if (!available || paddingFrameCount < 0)
            {
                _preStopAvailable = false;
                _preStopPaddingFrameCount = 0;
                return;
            }

            _preStopAvailable = true;
            _preStopPaddingFrameCount = paddingFrameCount;
        }
    }

    private static bool IsValid(PlaybackWindowsAudioRenderWriteObservation observation) =>
        observation.RequestedFrameCount > 0 &&
        observation.RequestedByteCount > 0 &&
        IsOptionalNonNegative(observation.HasCapacityFrameCount, observation.CapacityFrameCount) &&
        IsOptionalNonNegative(observation.HasPaddingFrameCount, observation.PaddingFrameCount) &&
        IsOptionalNonNegative(observation.HasAvailableFrameCount, observation.AvailableFrameCount) &&
        (!observation.HasAvailableFrameCount ||
            observation.HasCapacityFrameCount && observation.HasPaddingFrameCount &&
            observation.PaddingFrameCount <= observation.CapacityFrameCount &&
            observation.AvailableFrameCount ==
                observation.CapacityFrameCount - observation.PaddingFrameCount) &&
        Enum.IsDefined(observation.Result) &&
        observation.Result != PlaybackWindowsAudioRenderWriteResult.None &&
        (!observation.StartAcknowledged || observation.StartRequested) &&
        (!observation.ReleaseBufferAcknowledged || observation.GetBufferAcknowledged);

    private static bool IsOptionalNonNegative(bool available, int value) =>
        available ? value >= 0 : value == 0;
}

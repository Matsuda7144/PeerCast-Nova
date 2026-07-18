using System.Diagnostics;

namespace PeerCast.Nova.Playback;

public interface IPlaybackMonotonicClock
{
    TimeSpan GetTimestamp();
}

public sealed class StopwatchPlaybackMonotonicClock : IPlaybackMonotonicClock
{
    public TimeSpan GetTimestamp() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
}

public enum PlaybackPolicyMediaKind
{
    Audio,
    Video,
}

public enum PlaybackPolicyClockMaster
{
    Audio,
    Video,
}

public enum PlaybackPolicyDecisionKind
{
    DeliverNow,
    Wait,
    Drop,
    ResetClock,
    Reject,
}

public enum PlaybackPolicyDecisionReason
{
    None,
    UnknownTiming,
    Early,
    Late,
    DecoderReset,
    InvalidInput,
    SequenceRegression,
    TimestampRegression,
    ClockRegression,
    Overflow,
}

public readonly record struct PlaybackPolicyTime
{
    private PlaybackPolicyTime(bool isKnown, TimeSpan value)
    {
        IsKnown = isKnown;
        Value = value;
    }

    public bool IsKnown { get; }

    public TimeSpan Value { get; }

    public static PlaybackPolicyTime Unknown => default;

    public static PlaybackPolicyTime Known(TimeSpan value) => new(true, value);
}

public readonly record struct PlaybackPolicyFrameInput(
    PlaybackPolicyMediaKind MediaKind,
    PlaybackPolicyTime Timestamp,
    PlaybackPolicyTime Duration,
    long Sequence,
    PlaybackDecodedFrameDiscontinuityKind Discontinuity);

public readonly record struct PlaybackPolicyDecision
{
    private PlaybackPolicyDecision(
        PlaybackPolicyDecisionKind kind,
        PlaybackPolicyDecisionReason reason,
        TimeSpan waitDuration)
    {
        Kind = kind;
        Reason = reason;
        WaitDuration = waitDuration;
    }

    public PlaybackPolicyDecisionKind Kind { get; }

    public PlaybackPolicyDecisionReason Reason { get; }

    public TimeSpan WaitDuration { get; }

    internal static PlaybackPolicyDecision Create(
        PlaybackPolicyDecisionKind kind,
        PlaybackPolicyDecisionReason reason,
        TimeSpan waitDuration = default) => new(kind, reason, waitDuration);
}

public sealed record PlaybackPolicyOptions
{
    public static readonly TimeSpan MaximumWaitLimit = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumLatenessLimit = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumLeadLimit = TimeSpan.FromSeconds(10);

    public PlaybackPolicyClockMaster ClockMaster { get; init; }

    public TimeSpan MaximumWait { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaximumLateness { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MaximumLead { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed class PlaybackPolicyClockBoundary
{
    private readonly object _sync = new();
    private readonly IPlaybackMonotonicClock _clock;
    private readonly PlaybackPolicyOptions _options;
    private bool _hasArrival;
    private TimeSpan _lastArrival;
    private bool _hasSequence;
    private long _lastSequence;
    private bool _hasAudioTimestamp;
    private TimeSpan _lastAudioTimestamp;
    private bool _hasVideoTimestamp;
    private TimeSpan _lastVideoTimestamp;
    private bool _hasClockAnchor;
    private TimeSpan _anchorTimestamp;
    private TimeSpan _anchorArrival;

    public PlaybackPolicyClockBoundary(
        IPlaybackMonotonicClock clock,
        PlaybackPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        _clock = clock;
        _options = options;
    }

    public PlaybackPolicyDecision Evaluate(PlaybackPolicyFrameInput frame) =>
        Evaluate(frame, _clock.GetTimestamp());

    public PlaybackPolicyDecision Evaluate(
        PlaybackPolicyFrameInput frame,
        TimeSpan arrivalTimestamp)
    {
        lock (_sync)
        {
            if (!IsValid(frame, arrivalTimestamp))
            {
                return Reject(PlaybackPolicyDecisionReason.InvalidInput);
            }

            if (_hasArrival && arrivalTimestamp < _lastArrival)
            {
                return Reject(PlaybackPolicyDecisionReason.ClockRegression);
            }

            if (frame.Discontinuity == PlaybackDecodedFrameDiscontinuityKind.DecoderReset)
            {
                ResetState(frame, arrivalTimestamp);
                return PlaybackPolicyDecision.Create(
                    PlaybackPolicyDecisionKind.ResetClock,
                    PlaybackPolicyDecisionReason.DecoderReset);
            }

            if (_hasSequence && frame.Sequence <= _lastSequence)
            {
                return Reject(PlaybackPolicyDecisionReason.SequenceRegression);
            }

            if (HasTimestampRegression(frame))
            {
                return Reject(PlaybackPolicyDecisionReason.TimestampRegression);
            }

            if (!frame.Timestamp.IsKnown)
            {
                UpdateObserved(frame, arrivalTimestamp);
                return PlaybackPolicyDecision.Create(
                    PlaybackPolicyDecisionKind.DeliverNow,
                    PlaybackPolicyDecisionReason.UnknownTiming);
            }

            if (!_hasClockAnchor)
            {
                UpdateObserved(frame, arrivalTimestamp);
                if (IsMaster(frame.MediaKind))
                {
                    SetAnchor(frame.Timestamp.Value, arrivalTimestamp);
                }

                return PlaybackPolicyDecision.Create(
                    PlaybackPolicyDecisionKind.DeliverNow,
                    PlaybackPolicyDecisionReason.None);
            }

            long targetTicks;
            long deltaTicks;
            try
            {
                var offsetTicks = checked(frame.Timestamp.Value.Ticks - _anchorTimestamp.Ticks);
                targetTicks = checked(_anchorArrival.Ticks + offsetTicks);
                deltaTicks = checked(targetTicks - arrivalTimestamp.Ticks);
            }
            catch (OverflowException)
            {
                return Reject(PlaybackPolicyDecisionReason.Overflow);
            }

            if (deltaTicks > _options.MaximumLead.Ticks)
            {
                return Reject(PlaybackPolicyDecisionReason.InvalidInput);
            }

            UpdateObserved(frame, arrivalTimestamp);

            if (deltaTicks > 0)
            {
                var wait = TimeSpan.FromTicks(Math.Min(deltaTicks, _options.MaximumWait.Ticks));
                return PlaybackPolicyDecision.Create(
                    PlaybackPolicyDecisionKind.Wait,
                    PlaybackPolicyDecisionReason.Early,
                    wait);
            }

            if (deltaTicks < -_options.MaximumLateness.Ticks)
            {
                return PlaybackPolicyDecision.Create(
                    PlaybackPolicyDecisionKind.Drop,
                    PlaybackPolicyDecisionReason.Late);
            }

            return PlaybackPolicyDecision.Create(
                PlaybackPolicyDecisionKind.DeliverNow,
                PlaybackPolicyDecisionReason.None);
        }
    }

    private static void ValidateOptions(PlaybackPolicyOptions options)
    {
        if (!Enum.IsDefined(options.ClockMaster))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        ValidateBound(options.MaximumWait, PlaybackPolicyOptions.MaximumWaitLimit, nameof(options));
        ValidateBound(
            options.MaximumLateness,
            PlaybackPolicyOptions.MaximumLatenessLimit,
            nameof(options));
        ValidateBound(options.MaximumLead, PlaybackPolicyOptions.MaximumLeadLimit, nameof(options));
    }

    private static void ValidateBound(TimeSpan value, TimeSpan limit, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > limit)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static bool IsValid(PlaybackPolicyFrameInput frame, TimeSpan arrivalTimestamp)
    {
        if (!Enum.IsDefined(frame.MediaKind) ||
            !Enum.IsDefined(frame.Discontinuity) ||
            frame.Sequence < 0 ||
            arrivalTimestamp < TimeSpan.Zero ||
            frame.Timestamp.IsKnown && frame.Timestamp.Value < TimeSpan.Zero)
        {
            return false;
        }

        return !frame.Duration.IsKnown ||
            frame.Duration.Value > TimeSpan.Zero &&
            frame.Duration.Value <= PlaybackDecodedVideoFrame.MaximumDuration;
    }

    private bool HasTimestampRegression(PlaybackPolicyFrameInput frame)
    {
        if (!frame.Timestamp.IsKnown)
        {
            return false;
        }

        return frame.MediaKind switch
        {
            PlaybackPolicyMediaKind.Audio =>
                _hasAudioTimestamp && frame.Timestamp.Value < _lastAudioTimestamp,
            PlaybackPolicyMediaKind.Video =>
                _hasVideoTimestamp && frame.Timestamp.Value < _lastVideoTimestamp,
            _ => true,
        };
    }

    private void ResetState(PlaybackPolicyFrameInput frame, TimeSpan arrivalTimestamp)
    {
        _hasArrival = true;
        _lastArrival = arrivalTimestamp;
        _hasSequence = true;
        _lastSequence = frame.Sequence;
        _hasAudioTimestamp = false;
        _hasVideoTimestamp = false;
        _hasClockAnchor = false;
        UpdateMediaTimestamp(frame);
        if (frame.Timestamp.IsKnown && IsMaster(frame.MediaKind))
        {
            SetAnchor(frame.Timestamp.Value, arrivalTimestamp);
        }
    }

    private void UpdateObserved(PlaybackPolicyFrameInput frame, TimeSpan arrivalTimestamp)
    {
        _hasArrival = true;
        _lastArrival = arrivalTimestamp;
        _hasSequence = true;
        _lastSequence = frame.Sequence;
        UpdateMediaTimestamp(frame);
    }

    private void UpdateMediaTimestamp(PlaybackPolicyFrameInput frame)
    {
        if (!frame.Timestamp.IsKnown)
        {
            return;
        }

        if (frame.MediaKind == PlaybackPolicyMediaKind.Audio)
        {
            _hasAudioTimestamp = true;
            _lastAudioTimestamp = frame.Timestamp.Value;
        }
        else
        {
            _hasVideoTimestamp = true;
            _lastVideoTimestamp = frame.Timestamp.Value;
        }
    }

    private bool IsMaster(PlaybackPolicyMediaKind mediaKind) =>
        _options.ClockMaster switch
        {
            PlaybackPolicyClockMaster.Audio => mediaKind == PlaybackPolicyMediaKind.Audio,
            PlaybackPolicyClockMaster.Video => mediaKind == PlaybackPolicyMediaKind.Video,
            _ => false,
        };

    private void SetAnchor(TimeSpan timestamp, TimeSpan arrivalTimestamp)
    {
        _hasClockAnchor = true;
        _anchorTimestamp = timestamp;
        _anchorArrival = arrivalTimestamp;
    }

    private static PlaybackPolicyDecision Reject(PlaybackPolicyDecisionReason reason) =>
        PlaybackPolicyDecision.Create(PlaybackPolicyDecisionKind.Reject, reason);
}

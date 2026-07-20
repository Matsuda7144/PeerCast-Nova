using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackBoundedDemuxResult
{
    StreamDescriptionAvailable,
    PacketsAvailable,
    EndOfStream,
    NeedMoreInput,
    UnsupportedContainer,
    UnsupportedCodec,
    InvalidStreamDescription,
    MalformedData,
    TruncatedData,
    TimestampConversionFailed,
    LimitExceeded,
    ActiveCompletion,
    Cancelled,
    Disposed,
    Faulted,
}

public enum PlaybackBoundedDemuxContainerKind
{
    Flv,
    Mp4,
    FragmentedMp4,
}

public enum PlaybackBoundedDemuxMediaKind
{
    Video,
    Audio,
}

public enum PlaybackBoundedDemuxCodec
{
    H264,
    Aac,
}

public enum PlaybackBoundedDemuxEventKind
{
    Packet,
    EndOfStream,
}

public enum PlaybackBoundedDemuxKeyframeState
{
    NotApplicable,
    Unknown,
    NotKeyframe,
    Keyframe,
}

public enum PlaybackBoundedDemuxTimestampConversionResult
{
    Converted,
    InvalidTimeBase,
    InvalidLimit,
    MagnitudeExceeded,
}

public readonly record struct PlaybackBoundedDemuxTime
{
    private PlaybackBoundedDemuxTime(bool isKnown, TimeSpan value)
    {
        IsKnown = isKnown;
        Value = value;
    }

    public bool IsKnown { get; }

    public TimeSpan Value { get; }

    public static PlaybackBoundedDemuxTime Unknown => default;

    public static PlaybackBoundedDemuxTime Known(TimeSpan value) => new(true, value);
}

public readonly record struct PlaybackBoundedDemuxTimeBase(
    int Numerator,
    int Denominator);

public sealed record PlaybackBoundedDemuxOptions
{
    public const long DefaultMaximumInputBytesPerRead = 64L * 1024 * 1024;
    public const long MaximumInputBytesPerReadHardLimit = 1024L * 1024 * 1024;
    public const int DefaultMaximumPacketPayloadBytes = 4 * 1024 * 1024;
    public const int MaximumPacketPayloadBytesHardLimit = 4 * 1024 * 1024;
    public const int DefaultMaximumCodecExtradataBytes = 1024 * 1024;
    public const int MaximumCodecExtradataBytesHardLimit = 1024 * 1024;
    public const int DefaultMaximumPacketsPerRead = 64;
    public const int MaximumPacketsPerReadHardLimit = 1_024;
    public const long DefaultMaximumOwnedPacketBytesPerRead = 16L * 1024 * 1024;
    public const long MaximumOwnedPacketBytesPerReadHardLimit = 256L * 1024 * 1024;
    public const int DefaultMaximumSkippedRecordsPerRead = 4_096;
    public const int MaximumSkippedRecordsPerReadHardLimit = 1_000_000;
    public const int MaximumTimeBaseComponent = 1_000_000_000;

    public static readonly TimeSpan DefaultMaximumTimestampMagnitude = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaximumTimestampMagnitudeHardLimit = TimeSpan.FromDays(3650);

    public long MaximumInputBytesPerRead { get; init; } = DefaultMaximumInputBytesPerRead;

    public int MaximumPacketPayloadBytes { get; init; } = DefaultMaximumPacketPayloadBytes;

    public int MaximumCodecExtradataBytes { get; init; } = DefaultMaximumCodecExtradataBytes;

    public int MaximumPacketsPerRead { get; init; } = DefaultMaximumPacketsPerRead;

    public long MaximumOwnedPacketBytesPerRead { get; init; } =
        DefaultMaximumOwnedPacketBytesPerRead;

    public int MaximumSkippedRecordsPerRead { get; init; } =
        DefaultMaximumSkippedRecordsPerRead;

    public TimeSpan MaximumTimestampMagnitude { get; init; } =
        DefaultMaximumTimestampMagnitude;
}

public readonly record struct PlaybackBoundedDemuxStreamDescriptionInput(
    int StreamId,
    PlaybackBoundedDemuxMediaKind MediaKind,
    PlaybackBoundedDemuxCodec Codec,
    int Width,
    int Height,
    int SampleRate,
    int ChannelCount,
    PlaybackBoundedDemuxTimeBase TimeBase,
    ReadOnlyMemory<byte> CodecExtradata);

public sealed record PlaybackBoundedDemuxSessionDescriptionInput(
    PlaybackBoundedDemuxContainerKind ContainerKind,
    PlaybackBoundedDemuxStreamDescriptionInput Video,
    PlaybackBoundedDemuxStreamDescriptionInput Audio);

public readonly record struct PlaybackBoundedDemuxEventInput(
    PlaybackBoundedDemuxEventKind Kind,
    int StreamId,
    PlaybackBoundedDemuxMediaKind MediaKind,
    long Sequence,
    ReadOnlyMemory<byte> Payload,
    PlaybackBoundedDemuxTime PresentationTimestamp,
    PlaybackBoundedDemuxTime DecodeTimestamp,
    PlaybackBoundedDemuxTime Duration,
    PlaybackBoundedDemuxKeyframeState KeyframeState,
    PlaybackDecodedFrameDiscontinuityKind Discontinuity);

public sealed class PlaybackBoundedDemuxStreamDescription
{
    private byte[]? _codecExtradata;

    internal PlaybackBoundedDemuxStreamDescription(
        PlaybackBoundedDemuxStreamDescriptionInput input,
        byte[] codecExtradata)
    {
        StreamId = input.StreamId;
        MediaKind = input.MediaKind;
        Codec = input.Codec;
        Width = input.Width;
        Height = input.Height;
        SampleRate = input.SampleRate;
        ChannelCount = input.ChannelCount;
        TimeBase = input.TimeBase;
        _codecExtradata = codecExtradata;
    }

    public int StreamId { get; }

    public PlaybackBoundedDemuxMediaKind MediaKind { get; }

    public PlaybackBoundedDemuxCodec Codec { get; }

    public int Width { get; }

    public int Height { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public PlaybackBoundedDemuxTimeBase TimeBase { get; }

    public ReadOnlyMemory<byte> CodecExtradata => _codecExtradata ?? ReadOnlyMemory<byte>.Empty;

    internal void Clear()
    {
        var codecExtradata = Interlocked.Exchange(ref _codecExtradata, null);
        if (codecExtradata is not null)
        {
            Array.Clear(codecExtradata);
        }
    }
}

public sealed class PlaybackBoundedDemuxEvent
{
    private byte[]? _payload;

    internal PlaybackBoundedDemuxEvent(
        PlaybackBoundedDemuxEventInput input,
        byte[]? payload)
    {
        Kind = input.Kind;
        StreamId = input.StreamId;
        MediaKind = input.MediaKind;
        Sequence = input.Sequence;
        PresentationTimestamp = input.PresentationTimestamp;
        DecodeTimestamp = input.DecodeTimestamp;
        Duration = input.Duration;
        KeyframeState = input.KeyframeState;
        Discontinuity = input.Discontinuity;
        _payload = payload;
    }

    public PlaybackBoundedDemuxEventKind Kind { get; }

    public int StreamId { get; }

    public PlaybackBoundedDemuxMediaKind MediaKind { get; }

    public long Sequence { get; }

    public ReadOnlyMemory<byte> Payload => _payload ?? ReadOnlyMemory<byte>.Empty;

    public PlaybackBoundedDemuxTime PresentationTimestamp { get; }

    public PlaybackBoundedDemuxTime DecodeTimestamp { get; }

    public PlaybackBoundedDemuxTime Duration { get; }

    public PlaybackBoundedDemuxKeyframeState KeyframeState { get; }

    public PlaybackDecodedFrameDiscontinuityKind Discontinuity { get; }

    internal void Clear()
    {
        var payload = Interlocked.Exchange(ref _payload, null);
        if (payload is not null)
        {
            Array.Clear(payload);
        }
    }
}

public sealed class PlaybackBoundedDemuxReadCompletion : IDisposable
{
    private readonly PlaybackBoundedDemuxSession? _owner;
    private readonly PlaybackBoundedDemuxEvent[] _ownedEvents;
    private readonly IReadOnlyList<PlaybackBoundedDemuxEvent> _events;
    private int _isDisposed;

    internal PlaybackBoundedDemuxReadCompletion(
        PlaybackBoundedDemuxResult result,
        PlaybackBoundedDemuxEvent[] events,
        long inputBytesObserved,
        int skippedRecordCount,
        PlaybackBoundedDemuxSession? owner = null)
    {
        Result = result;
        _ownedEvents = events;
        _events = Array.AsReadOnly(events);
        InputBytesObserved = inputBytesObserved;
        SkippedRecordCount = skippedRecordCount;
        _owner = owner;
    }

    public PlaybackBoundedDemuxResult Result { get; }

    public IReadOnlyList<PlaybackBoundedDemuxEvent> Events => _events;

    public long InputBytesObserved { get; }

    public int SkippedRecordCount { get; }

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        ClearEvents(_ownedEvents);
        _owner?.ReleaseCompletion(this);
        GC.SuppressFinalize(this);
    }

    private static void ClearEvents(IEnumerable<PlaybackBoundedDemuxEvent> events)
    {
        foreach (var item in events)
        {
            item?.Clear();
        }
    }
}

public static class PlaybackBoundedDemuxTimestampConverter
{
    public static PlaybackBoundedDemuxTimestampConversionResult TryConvert(
        long timestamp,
        PlaybackBoundedDemuxTimeBase timeBase,
        TimeSpan maximumMagnitude,
        out PlaybackBoundedDemuxTime result)
    {
        result = PlaybackBoundedDemuxTime.Unknown;
        if (!PlaybackBoundedDemuxSession.IsValidTimeBase(timeBase))
        {
            return PlaybackBoundedDemuxTimestampConversionResult.InvalidTimeBase;
        }

        if (maximumMagnitude <= TimeSpan.Zero ||
            maximumMagnitude > PlaybackBoundedDemuxOptions.MaximumTimestampMagnitudeHardLimit)
        {
            return PlaybackBoundedDemuxTimestampConversionResult.InvalidLimit;
        }

        var scaledTicks = (BigInteger)timestamp * timeBase.Numerator * TimeSpan.TicksPerSecond;
        var convertedTicks = scaledTicks / timeBase.Denominator;
        if (convertedTicks < long.MinValue || convertedTicks > long.MaxValue)
        {
            return PlaybackBoundedDemuxTimestampConversionResult.MagnitudeExceeded;
        }

        var ticks = (long)convertedTicks;
        if (ticks < -maximumMagnitude.Ticks || ticks > maximumMagnitude.Ticks)
        {
            return PlaybackBoundedDemuxTimestampConversionResult.MagnitudeExceeded;
        }

        result = PlaybackBoundedDemuxTime.Known(TimeSpan.FromTicks(ticks));
        return PlaybackBoundedDemuxTimestampConversionResult.Converted;
    }
}

public sealed class PlaybackBoundedDemuxSession : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackBoundedDemuxOptions _options;
    private PlaybackBoundedDemuxReadCompletion? _activeCompletion;
    private bool _hasVideoSequence;
    private long _lastVideoSequence;
    private bool _hasAudioSequence;
    private long _lastAudioSequence;
    private bool _videoEnded;
    private bool _audioEnded;
    private int _isDisposed;

    private PlaybackBoundedDemuxSession(
        PlaybackBoundedDemuxOptions options,
        PlaybackBoundedDemuxContainerKind containerKind,
        PlaybackBoundedDemuxStreamDescription video,
        PlaybackBoundedDemuxStreamDescription audio)
    {
        _options = options;
        ContainerKind = containerKind;
        Video = video;
        Audio = audio;
    }

    public PlaybackBoundedDemuxContainerKind ContainerKind { get; }

    public PlaybackBoundedDemuxStreamDescription Video { get; }

    public PlaybackBoundedDemuxStreamDescription Audio { get; }

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public static PlaybackBoundedDemuxResult TryCreate(
        PlaybackBoundedDemuxOptions? options,
        PlaybackBoundedDemuxSessionDescriptionInput? input,
        out PlaybackBoundedDemuxSession? session)
    {
        session = null;
        if (!ValidateOptions(options))
        {
            return PlaybackBoundedDemuxResult.LimitExceeded;
        }

        if (input is null || !Enum.IsDefined(input.ContainerKind))
        {
            return PlaybackBoundedDemuxResult.UnsupportedContainer;
        }

        var video = input.Video;
        var audio = input.Audio;
        if (video.Codec != PlaybackBoundedDemuxCodec.H264 ||
            audio.Codec != PlaybackBoundedDemuxCodec.Aac ||
            !Enum.IsDefined(video.Codec) ||
            !Enum.IsDefined(audio.Codec))
        {
            return PlaybackBoundedDemuxResult.UnsupportedCodec;
        }

        var videoValidation = ValidateStream(video, PlaybackBoundedDemuxMediaKind.Video, options!);
        if (videoValidation != PlaybackBoundedDemuxResult.StreamDescriptionAvailable)
        {
            return videoValidation;
        }

        var audioValidation = ValidateStream(audio, PlaybackBoundedDemuxMediaKind.Audio, options!);
        if (audioValidation != PlaybackBoundedDemuxResult.StreamDescriptionAvailable)
        {
            return audioValidation;
        }

        if (video.StreamId == audio.StreamId)
        {
            return PlaybackBoundedDemuxResult.InvalidStreamDescription;
        }

        byte[]? videoExtradata = null;
        byte[]? audioExtradata = null;
        try
        {
            videoExtradata = video.CodecExtradata.ToArray();
            audioExtradata = audio.CodecExtradata.ToArray();
            session = new(
                options!,
                input.ContainerKind,
                new PlaybackBoundedDemuxStreamDescription(video, videoExtradata),
                new PlaybackBoundedDemuxStreamDescription(audio, audioExtradata));
            videoExtradata = null;
            audioExtradata = null;
            return PlaybackBoundedDemuxResult.StreamDescriptionAvailable;
        }
        catch (OutOfMemoryException)
        {
            return PlaybackBoundedDemuxResult.Faulted;
        }
        finally
        {
            if (videoExtradata is not null)
            {
                Array.Clear(videoExtradata);
            }

            if (audioExtradata is not null)
            {
                Array.Clear(audioExtradata);
            }
        }
    }

    public PlaybackBoundedDemuxReadCompletion CreateReadCompletion(
        PlaybackBoundedDemuxResult sourceResult,
        IReadOnlyList<PlaybackBoundedDemuxEventInput>? events,
        long inputBytesObserved,
        int skippedRecordCount,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed != 0)
            {
                return Empty(PlaybackBoundedDemuxResult.Disposed);
            }

            if (_activeCompletion is not null)
            {
                return Empty(PlaybackBoundedDemuxResult.ActiveCompletion);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Empty(PlaybackBoundedDemuxResult.Cancelled);
            }

            if (inputBytesObserved < 0 || inputBytesObserved > _options.MaximumInputBytesPerRead ||
                skippedRecordCount < 0 ||
                skippedRecordCount > _options.MaximumSkippedRecordsPerRead)
            {
                return Empty(PlaybackBoundedDemuxResult.LimitExceeded);
            }

            var eventCount = events?.Count ?? 0;
            if (_videoEnded && _audioEnded)
            {
                return sourceResult == PlaybackBoundedDemuxResult.EndOfStream && eventCount == 0
                    ? new PlaybackBoundedDemuxReadCompletion(
                        PlaybackBoundedDemuxResult.EndOfStream,
                        [],
                        inputBytesObserved,
                        skippedRecordCount)
                    : Empty(PlaybackBoundedDemuxResult.MalformedData);
            }

            if (sourceResult == PlaybackBoundedDemuxResult.NeedMoreInput)
            {
                return eventCount == 0
                    ? new PlaybackBoundedDemuxReadCompletion(
                        sourceResult,
                        [],
                        inputBytesObserved,
                        skippedRecordCount)
                    : Empty(PlaybackBoundedDemuxResult.Faulted);
            }

            if (IsSourceFailure(sourceResult))
            {
                return eventCount == 0
                    ? new PlaybackBoundedDemuxReadCompletion(
                        sourceResult,
                        [],
                        inputBytesObserved,
                        skippedRecordCount)
                    : Empty(PlaybackBoundedDemuxResult.Faulted);
            }

            if (sourceResult is not PlaybackBoundedDemuxResult.PacketsAvailable and
                not PlaybackBoundedDemuxResult.EndOfStream)
            {
                return Empty(PlaybackBoundedDemuxResult.Faulted);
            }

            if (events is null || eventCount == 0 ||
                eventCount > _options.MaximumPacketsPerRead + 2)
            {
                return Empty(eventCount == 0
                    ? PlaybackBoundedDemuxResult.MalformedData
                    : PlaybackBoundedDemuxResult.LimitExceeded);
            }

            var validation = ValidateEvents(events, sourceResult, out var state);
            if (validation != sourceResult)
            {
                return Empty(validation);
            }

            PlaybackBoundedDemuxEvent[] ownedEvents;
            try
            {
                ownedEvents = CopyEvents(events, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Empty(PlaybackBoundedDemuxResult.Cancelled);
            }
            catch (OutOfMemoryException)
            {
                return Empty(PlaybackBoundedDemuxResult.Faulted);
            }
            catch
            {
                return Empty(PlaybackBoundedDemuxResult.Faulted);
            }

            PlaybackBoundedDemuxReadCompletion completion;
            try
            {
                completion = new(
                    sourceResult,
                    ownedEvents,
                    inputBytesObserved,
                    skippedRecordCount,
                    this);
            }
            catch
            {
                ClearEvents(ownedEvents);
                return Empty(PlaybackBoundedDemuxResult.Faulted);
            }

            Commit(state);
            _activeCompletion = completion;
            return completion;
        }
    }

    public void Dispose()
    {
        PlaybackBoundedDemuxReadCompletion? active;
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            active = _activeCompletion;
            _activeCompletion = null;
        }

        active?.Dispose();
        Audio.Clear();
        Video.Clear();
        GC.SuppressFinalize(this);
    }

    internal static bool IsValidTimeBase(PlaybackBoundedDemuxTimeBase value) =>
        value.Numerator is >= 1 and <= PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent &&
        value.Denominator is >= 1 and <= PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent;

    internal void ReleaseCompletion(PlaybackBoundedDemuxReadCompletion completion)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeCompletion, completion))
            {
                _activeCompletion = null;
            }
        }
    }

    private PlaybackBoundedDemuxResult ValidateEvents(
        IReadOnlyList<PlaybackBoundedDemuxEventInput> events,
        PlaybackBoundedDemuxResult sourceResult,
        out PendingState state)
    {
        state = new(
            _hasVideoSequence,
            _lastVideoSequence,
            _hasAudioSequence,
            _lastAudioSequence,
            _videoEnded,
            _audioEnded,
            PacketCount: 0,
            OwnedPacketBytes: 0);

        try
        {
            for (var index = 0; index < events.Count; index++)
            {
                var item = events[index];
                var result = ValidateEvent(item, ref state);
                if (result != PlaybackBoundedDemuxResult.PacketsAvailable)
                {
                    return result;
                }
            }
        }
        catch (Exception exception) when (exception is
            ArgumentOutOfRangeException or InvalidOperationException or OverflowException)
        {
            return PlaybackBoundedDemuxResult.MalformedData;
        }

        if (sourceResult == PlaybackBoundedDemuxResult.EndOfStream)
        {
            return state.VideoEnded && state.AudioEnded
                ? PlaybackBoundedDemuxResult.EndOfStream
                : PlaybackBoundedDemuxResult.MalformedData;
        }

        return state.VideoEnded && state.AudioEnded
            ? PlaybackBoundedDemuxResult.MalformedData
            : PlaybackBoundedDemuxResult.PacketsAvailable;
    }

    private PlaybackBoundedDemuxResult ValidateEvent(
        PlaybackBoundedDemuxEventInput item,
        ref PendingState state)
    {
        if (!Enum.IsDefined(item.Kind) ||
            !Enum.IsDefined(item.MediaKind) ||
            !Enum.IsDefined(item.KeyframeState) ||
            !Enum.IsDefined(item.Discontinuity) ||
            item.Sequence < 0)
        {
            return PlaybackBoundedDemuxResult.MalformedData;
        }

        var stream = item.MediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => Video,
            PlaybackBoundedDemuxMediaKind.Audio => Audio,
            _ => null,
        };
        if (stream is null || item.StreamId != stream.StreamId)
        {
            return PlaybackBoundedDemuxResult.MalformedData;
        }

        if (!AdvanceSequence(item.MediaKind, item.Sequence, ref state) ||
            IsEnded(item.MediaKind, state))
        {
            return PlaybackBoundedDemuxResult.MalformedData;
        }

        if (item.Kind == PlaybackBoundedDemuxEventKind.EndOfStream)
        {
            if (!item.Payload.IsEmpty ||
                item.PresentationTimestamp.IsKnown ||
                item.DecodeTimestamp.IsKnown ||
                item.Duration.IsKnown ||
                item.KeyframeState != PlaybackBoundedDemuxKeyframeState.NotApplicable ||
                item.Discontinuity != PlaybackDecodedFrameDiscontinuityKind.None)
            {
                return PlaybackBoundedDemuxResult.MalformedData;
            }

            SetEnded(item.MediaKind, ref state);
            return PlaybackBoundedDemuxResult.PacketsAvailable;
        }

        if (item.Payload.IsEmpty)
        {
            return PlaybackBoundedDemuxResult.MalformedData;
        }

        if (item.Payload.Length > _options.MaximumPacketPayloadBytes)
        {
            return PlaybackBoundedDemuxResult.LimitExceeded;
        }

        if (!ValidateTime(item.PresentationTimestamp) ||
            !ValidateTime(item.DecodeTimestamp) ||
            !ValidateDuration(item.Duration))
        {
            return PlaybackBoundedDemuxResult.TimestampConversionFailed;
        }

        if (item.MediaKind == PlaybackBoundedDemuxMediaKind.Audio)
        {
            if (item.KeyframeState != PlaybackBoundedDemuxKeyframeState.NotApplicable)
            {
                return PlaybackBoundedDemuxResult.MalformedData;
            }
        }
        else if (item.KeyframeState == PlaybackBoundedDemuxKeyframeState.NotApplicable)
        {
            return PlaybackBoundedDemuxResult.MalformedData;
        }

        state = state with
        {
            PacketCount = checked(state.PacketCount + 1),
            OwnedPacketBytes = checked(state.OwnedPacketBytes + item.Payload.Length),
        };
        if (state.PacketCount > _options.MaximumPacketsPerRead ||
            state.OwnedPacketBytes > _options.MaximumOwnedPacketBytesPerRead)
        {
            return PlaybackBoundedDemuxResult.LimitExceeded;
        }

        return PlaybackBoundedDemuxResult.PacketsAvailable;
    }

    private static PlaybackBoundedDemuxEvent[] CopyEvents(
        IReadOnlyList<PlaybackBoundedDemuxEventInput> events,
        CancellationToken cancellationToken)
    {
        var result = new PlaybackBoundedDemuxEvent[events.Count];
        try
        {
            for (var index = 0; index < events.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = events[index];
                result[index] = new(
                    input,
                    input.Kind == PlaybackBoundedDemuxEventKind.Packet
                        ? input.Payload.ToArray()
                        : null);
            }

            return result;
        }
        catch
        {
            ClearEvents(result);
            throw;
        }
    }

    private static void ClearEvents(IEnumerable<PlaybackBoundedDemuxEvent> events)
    {
        foreach (var item in events)
        {
            item?.Clear();
        }
    }

    private static PlaybackBoundedDemuxResult ValidateStream(
        PlaybackBoundedDemuxStreamDescriptionInput input,
        PlaybackBoundedDemuxMediaKind expectedKind,
        PlaybackBoundedDemuxOptions options)
    {
        if (!Enum.IsDefined(input.MediaKind) || input.MediaKind != expectedKind ||
            input.StreamId < 0 || !IsValidTimeBase(input.TimeBase) ||
            input.CodecExtradata.IsEmpty)
        {
            return PlaybackBoundedDemuxResult.InvalidStreamDescription;
        }

        if (input.CodecExtradata.Length > options.MaximumCodecExtradataBytes)
        {
            return PlaybackBoundedDemuxResult.LimitExceeded;
        }

        if (expectedKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            if (input.Codec != PlaybackBoundedDemuxCodec.H264 ||
                input.Width is < 1 or > PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension ||
                input.Height is < 1 or > PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension ||
                input.SampleRate != 0 || input.ChannelCount != 0)
            {
                return PlaybackBoundedDemuxResult.InvalidStreamDescription;
            }

            try
            {
                if (checked((long)input.Width * input.Height) >
                    PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoPixelCount)
                {
                    return PlaybackBoundedDemuxResult.InvalidStreamDescription;
                }
            }
            catch (OverflowException)
            {
                return PlaybackBoundedDemuxResult.InvalidStreamDescription;
            }
        }
        else if (input.Codec != PlaybackBoundedDemuxCodec.Aac ||
            input.Width != 0 || input.Height != 0 ||
            input.SampleRate is < PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioSampleRate or
                > PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioSampleRate ||
            input.ChannelCount is < PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioChannelCount or
                > PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioChannelCount)
        {
            return PlaybackBoundedDemuxResult.InvalidStreamDescription;
        }

        return PlaybackBoundedDemuxResult.StreamDescriptionAvailable;
    }

    private static bool ValidateOptions(PlaybackBoundedDemuxOptions? options) =>
        options is not null &&
        options.MaximumInputBytesPerRead is >= 1 and <=
            PlaybackBoundedDemuxOptions.MaximumInputBytesPerReadHardLimit &&
        options.MaximumPacketPayloadBytes is >= 1 and <=
            PlaybackBoundedDemuxOptions.MaximumPacketPayloadBytesHardLimit &&
        options.MaximumCodecExtradataBytes is >= 1 and <=
            PlaybackBoundedDemuxOptions.MaximumCodecExtradataBytesHardLimit &&
        options.MaximumPacketsPerRead is >= 1 and <=
            PlaybackBoundedDemuxOptions.MaximumPacketsPerReadHardLimit &&
        options.MaximumOwnedPacketBytesPerRead >= options.MaximumPacketPayloadBytes &&
        options.MaximumOwnedPacketBytesPerRead <=
            PlaybackBoundedDemuxOptions.MaximumOwnedPacketBytesPerReadHardLimit &&
        options.MaximumInputBytesPerRead >= options.MaximumOwnedPacketBytesPerRead &&
        options.MaximumSkippedRecordsPerRead is >= 0 and <=
            PlaybackBoundedDemuxOptions.MaximumSkippedRecordsPerReadHardLimit &&
        options.MaximumTimestampMagnitude > TimeSpan.Zero &&
        options.MaximumTimestampMagnitude <=
            PlaybackBoundedDemuxOptions.MaximumTimestampMagnitudeHardLimit;

    private bool ValidateTime(PlaybackBoundedDemuxTime value)
    {
        if (!value.IsKnown)
        {
            return true;
        }

        return value.Value.Ticks >= -_options.MaximumTimestampMagnitude.Ticks &&
            value.Value.Ticks <= _options.MaximumTimestampMagnitude.Ticks;
    }

    private bool ValidateDuration(PlaybackBoundedDemuxTime value) =>
        !value.IsKnown ||
        value.Value > TimeSpan.Zero &&
        value.Value <= _options.MaximumTimestampMagnitude;

    private static bool AdvanceSequence(
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence,
        ref PendingState state)
    {
        if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            if (state.HasVideoSequence && sequence <= state.LastVideoSequence)
            {
                return false;
            }

            state = state with { HasVideoSequence = true, LastVideoSequence = sequence };
            return true;
        }

        if (state.HasAudioSequence && sequence <= state.LastAudioSequence)
        {
            return false;
        }

        state = state with { HasAudioSequence = true, LastAudioSequence = sequence };
        return true;
    }

    private static bool IsEnded(
        PlaybackBoundedDemuxMediaKind mediaKind,
        PendingState state) =>
        mediaKind == PlaybackBoundedDemuxMediaKind.Video
            ? state.VideoEnded
            : state.AudioEnded;

    private static void SetEnded(
        PlaybackBoundedDemuxMediaKind mediaKind,
        ref PendingState state) =>
        state = mediaKind == PlaybackBoundedDemuxMediaKind.Video
            ? state with { VideoEnded = true }
            : state with { AudioEnded = true };

    private void Commit(PendingState state)
    {
        _hasVideoSequence = state.HasVideoSequence;
        _lastVideoSequence = state.LastVideoSequence;
        _hasAudioSequence = state.HasAudioSequence;
        _lastAudioSequence = state.LastAudioSequence;
        _videoEnded = state.VideoEnded;
        _audioEnded = state.AudioEnded;
    }

    private static bool IsSourceFailure(PlaybackBoundedDemuxResult result) => result is
        PlaybackBoundedDemuxResult.UnsupportedContainer or
        PlaybackBoundedDemuxResult.UnsupportedCodec or
        PlaybackBoundedDemuxResult.InvalidStreamDescription or
        PlaybackBoundedDemuxResult.MalformedData or
        PlaybackBoundedDemuxResult.TruncatedData or
        PlaybackBoundedDemuxResult.TimestampConversionFailed or
        PlaybackBoundedDemuxResult.LimitExceeded or
        PlaybackBoundedDemuxResult.Cancelled or
        PlaybackBoundedDemuxResult.Faulted;

    private static PlaybackBoundedDemuxReadCompletion Empty(
        PlaybackBoundedDemuxResult result) => new(result, [], 0, 0);

    private readonly record struct PendingState(
        bool HasVideoSequence,
        long LastVideoSequence,
        bool HasAudioSequence,
        long LastAudioSequence,
        bool VideoEnded,
        bool AudioEnded,
        int PacketCount,
        long OwnedPacketBytes);
}

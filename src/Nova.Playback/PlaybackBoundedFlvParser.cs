using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackBoundedFlvParserResult
{
    NeedMoreInput,
    StreamDescriptionAvailable,
    PacketsAvailable,
    EndOfStream,
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

public sealed record PlaybackBoundedFlvParserOptions
{
    public const int DefaultMaximumTagDataBytes =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes + 5;
    public const int MaximumTagDataBytesHardLimit = 16 * 1024 * 1024;
    public const int DefaultMaximumScriptTagDataBytes = 1024 * 1024;
    public const int MaximumScriptTagDataBytesHardLimit = 16 * 1024 * 1024;
    public const int DefaultMaximumCarryBytes = DefaultMaximumTagDataBytes + 15;
    public const int MaximumCarryBytesHardLimit = 32 * 1024 * 1024;
    public const long DefaultMaximumSkippedTagBytesPerOperation = 16L * 1024 * 1024;
    public const long MaximumSkippedTagBytesPerOperationHardLimit = 64L * 1024 * 1024;
    public const int DefaultMaximumNalUnitsPerPacket = 4_096;
    public const int MaximumNalUnitsPerPacketHardLimit = 65_536;
    public const int DefaultMaximumParameterSets = 32;
    public const int MaximumParameterSetsHardLimit = 256;
    public const int DefaultMaximumSpsRbspBytes = 64 * 1024;
    public const int MaximumSpsRbspBytesHardLimit = 1024 * 1024;
    public const int DefaultMaximumSpsCycleOffsets = 256;
    public const int MaximumSpsCycleOffsetsHardLimit = 4_096;

    public PlaybackBoundedDemuxOptions DemuxOptions { get; init; } = new();

    public int MaximumTagDataBytes { get; init; } = DefaultMaximumTagDataBytes;

    public int MaximumScriptTagDataBytes { get; init; } =
        DefaultMaximumScriptTagDataBytes;

    public int MaximumCarryBytes { get; init; } = DefaultMaximumCarryBytes;

    public long MaximumSkippedTagBytesPerOperation { get; init; } =
        DefaultMaximumSkippedTagBytesPerOperation;

    public int MaximumNalUnitsPerPacket { get; init; } =
        DefaultMaximumNalUnitsPerPacket;

    public int MaximumParameterSets { get; init; } = DefaultMaximumParameterSets;

    public int MaximumSpsRbspBytes { get; init; } = DefaultMaximumSpsRbspBytes;

    public int MaximumSpsCycleOffsets { get; init; } =
        DefaultMaximumSpsCycleOffsets;
}

public sealed record PlaybackBoundedFlvParserOperation(
    PlaybackBoundedFlvParserResult Result,
    int ConsumedByteCount,
    PlaybackBoundedDemuxReadCompletion? Completion,
    long StreamBytePosition,
    int CarryByteCount,
    int SkippedTagCount,
    int Generation);

public sealed class PlaybackBoundedFlvParser : IDisposable
{
    private const byte AudioTagType = 8;
    private const byte VideoTagType = 9;
    private const byte ScriptTagType = 18;
    private const byte AvcCodecId = 7;
    private const byte AacSoundFormat = 10;
    private const byte AvcSequenceHeader = 0;
    private const byte AvcNalu = 1;
    private const byte AvcEndOfSequence = 2;
    private const byte AacSequenceHeader = 0;
    private const byte AacRaw = 1;
    private const int HeaderRecordLength = 13;
    private const int TagHeaderLength = 11;
    private const int PreviousTagSizeLength = 4;
    private const int VideoStreamId = 0;
    private const int AudioStreamId = 1;

    private static readonly PlaybackBoundedDemuxTimeBase FlvTimeBase = new(1, 1_000);

    private readonly object _sync = new();
    private readonly PlaybackBoundedFlvParserOptions _options;
    private readonly PlaybackBoundedDemuxOptions _demuxOptions;
    private byte[]? _carry;
    private int _carryLength;
    private int _carryTargetLength;
    private ParserPhase _phase = ParserPhase.Header;
    private byte[]? _videoConfiguration;
    private int _videoWidth;
    private int _videoHeight;
    private int _nalLengthSize;
    private PlaybackBoundedDemuxSession? _session;
    private PlaybackBoundedDemuxReadCompletion? _activeCompletion;
    private long _nextVideoSequence;
    private long _nextAudioSequence;
    private bool _videoEnded;
    private bool _audioEnded;
    private bool _faulted;
    private int _generation;
    private long _streamBytePosition;
    private int _isDisposed;

    public PlaybackBoundedFlvParser(PlaybackBoundedFlvParserOptions? options = null)
    {
        _options = options is null
            ? new PlaybackBoundedFlvParserOptions()
            : options with { };
        _demuxOptions = _options.DemuxOptions with { };
        ValidateOptions(_options, _demuxOptions);
    }

    public PlaybackBoundedDemuxSession? Session
    {
        get
        {
            lock (_sync)
            {
                return _session;
            }
        }
    }

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public PlaybackBoundedFlvParserOperation Parse(
        ReadOnlyMemory<byte> input,
        bool isFinal = false,
        bool discontinuity = false,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed != 0)
            {
                return Operation(PlaybackBoundedFlvParserResult.Disposed, 0, null, 0);
            }

            if (_faulted)
            {
                return Operation(PlaybackBoundedFlvParserResult.Faulted, 0, null, 0);
            }

            if (_activeCompletion is { IsDisposed: false })
            {
                return Operation(
                    PlaybackBoundedFlvParserResult.ActiveCompletion,
                    0,
                    null,
                    0);
            }

            _activeCompletion = null;

            if (cancellationToken.IsCancellationRequested)
            {
                return Operation(PlaybackBoundedFlvParserResult.Cancelled, 0, null, 0);
            }

            if (discontinuity)
            {
                ResetForDiscontinuity();
            }

            if (!CanObserveInput(input.Length))
            {
                return Operation(PlaybackBoundedFlvParserResult.LimitExceeded, 0, null, 0);
            }

            var consumed = 0;
            var skippedTagCount = 0;
            long skippedTagBytes = 0;
            var carryBytesAtStart = _carryLength;

            while (true)
            {
                if (_carryLength == 0 && consumed == input.Length)
                {
                    if (!isFinal)
                    {
                        CommitConsumed(consumed);
                        return Operation(
                            PlaybackBoundedFlvParserResult.NeedMoreInput,
                            consumed,
                            null,
                            skippedTagCount);
                    }

                    return CompleteFinalInput(
                        consumed,
                        carryBytesAtStart,
                        skippedTagCount);
                }

                var acquireResult = TryAcquireRecord(
                    input,
                    ref consumed,
                    isFinal,
                    out var record,
                    out var recordFromCarry);
                if (acquireResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
                {
                    CommitConsumed(consumed);
                    if (acquireResult == PlaybackBoundedFlvParserResult.NeedMoreInput)
                    {
                        return Operation(acquireResult, consumed, null, skippedTagCount);
                    }

                    return Terminal(acquireResult, consumed, skippedTagCount);
                }

                if (_phase == ParserPhase.Header)
                {
                    var headerResult = ValidateHeader(record.Span);
                    if (headerResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
                    {
                        CommitConsumed(consumed);
                        return Terminal(headerResult, consumed, skippedTagCount);
                    }

                    _phase = ParserPhase.Tags;
                    ReleaseRecord(recordFromCarry);
                    continue;
                }

                var processResult = ProcessTag(
                    record,
                    consumed,
                    carryBytesAtStart,
                    ref skippedTagCount,
                    ref skippedTagBytes,
                    cancellationToken,
                    out var completion,
                    out var sessionBecameAvailable);

                if (processResult == PlaybackBoundedFlvParserResult.NeedMoreInput)
                {
                    ReleaseRecord(recordFromCarry);
                    continue;
                }

                if (processResult is PlaybackBoundedFlvParserResult.PacketsAvailable or
                    PlaybackBoundedFlvParserResult.EndOfStream)
                {
                    ReleaseRecord(recordFromCarry);
                    CommitConsumed(consumed);
                    _activeCompletion = completion;
                    return Operation(
                        processResult,
                        consumed,
                        completion,
                        skippedTagCount);
                }

                if (sessionBecameAvailable)
                {
                    ReleaseRecord(recordFromCarry);
                    CommitConsumed(consumed);
                    return Operation(
                        PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
                        consumed,
                        null,
                        skippedTagCount);
                }

                CommitConsumed(consumed);
                return Terminal(processResult, consumed, skippedTagCount);
            }
        }
    }

    public void Dispose()
    {
        PlaybackBoundedDemuxReadCompletion? active;
        PlaybackBoundedDemuxSession? session;

        lock (_sync)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            active = _activeCompletion;
            _activeCompletion = null;
            session = _session;
            _session = null;
            ClearCarry();
            ClearVideoConfiguration();
        }

        active?.Dispose();
        session?.Dispose();
        GC.SuppressFinalize(this);
    }

    private PlaybackBoundedFlvParserOperation CompleteFinalInput(
        int consumed,
        int carryBytesAtStart,
        int skippedTagCount)
    {
        if (_phase != ParserPhase.Tags)
        {
            CommitConsumed(consumed);
            return Terminal(
                PlaybackBoundedFlvParserResult.TruncatedData,
                consumed,
                skippedTagCount);
        }

        if (_session is null)
        {
            CommitConsumed(consumed);
            return Terminal(
                PlaybackBoundedFlvParserResult.InvalidStreamDescription,
                consumed,
                skippedTagCount);
        }

        var events = CreateFinalEosEvents();
        var observedBytes = GetObservedByteCount(consumed, carryBytesAtStart);
        var completion = _session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.EndOfStream,
            events,
            observedBytes,
            skippedTagCount);
        if (completion.Result != PlaybackBoundedDemuxResult.EndOfStream)
        {
            completion.Dispose();
            CommitConsumed(consumed);
            return Terminal(
                MapResult(completion.Result),
                consumed,
                skippedTagCount);
        }

        if (!_videoEnded)
        {
            _videoEnded = true;
            _nextVideoSequence = checked(_nextVideoSequence + 1);
        }

        if (!_audioEnded)
        {
            _audioEnded = true;
            _nextAudioSequence = checked(_nextAudioSequence + 1);
        }

        CommitConsumed(consumed);
        _activeCompletion = events.Length == 0 ? null : completion;
        return Operation(
            PlaybackBoundedFlvParserResult.EndOfStream,
            consumed,
            completion,
            skippedTagCount);
    }

    private PlaybackBoundedDemuxEventInput[] CreateFinalEosEvents()
    {
        if (_videoEnded && _audioEnded)
        {
            return [];
        }

        if (!_videoEnded && !_audioEnded)
        {
            return
            [
                CreateEosEvent(
                    VideoStreamId,
                    PlaybackBoundedDemuxMediaKind.Video,
                    _nextVideoSequence),
                CreateEosEvent(
                    AudioStreamId,
                    PlaybackBoundedDemuxMediaKind.Audio,
                    _nextAudioSequence),
            ];
        }

        return !_videoEnded
            ?
            [
                CreateEosEvent(
                    VideoStreamId,
                    PlaybackBoundedDemuxMediaKind.Video,
                    _nextVideoSequence),
            ]
            :
            [
                CreateEosEvent(
                    AudioStreamId,
                    PlaybackBoundedDemuxMediaKind.Audio,
                    _nextAudioSequence),
            ];
    }

    private PlaybackBoundedFlvParserResult ProcessTag(
        ReadOnlyMemory<byte> record,
        int consumed,
        int carryBytesAtStart,
        ref int skippedTagCount,
        ref long skippedTagBytes,
        CancellationToken cancellationToken,
        out PlaybackBoundedDemuxReadCompletion? completion,
        out bool sessionBecameAvailable)
    {
        completion = null;
        sessionBecameAvailable = false;
        var span = record.Span;
        var tagType = span[0];
        var dataSize = ReadUInt24(span.Slice(1, 3));
        var timestamp = ReadFlvTimestamp(span);
        if (ReadUInt24(span.Slice(8, 3)) != 0)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var expectedPreviousTagSize = checked((uint)(TagHeaderLength + dataSize));
        var actualPreviousTagSize = BinaryPrimitives.ReadUInt32BigEndian(
            span.Slice(TagHeaderLength + dataSize, PreviousTagSizeLength));
        if (actualPreviousTagSize != expectedPreviousTagSize)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var body = record.Slice(TagHeaderLength, dataSize);
        switch (tagType)
        {
            case ScriptTagType:
                return SkipTag(
                    dataSize,
                    ref skippedTagCount,
                    ref skippedTagBytes);
            case VideoTagType:
                return ProcessVideoTag(
                    body,
                    timestamp,
                    consumed,
                    carryBytesAtStart,
                    ref skippedTagCount,
                    ref skippedTagBytes,
                    cancellationToken,
                    out completion,
                    out sessionBecameAvailable);
            case AudioTagType:
                return ProcessAudioTag(
                    body,
                    timestamp,
                    consumed,
                    carryBytesAtStart,
                    ref skippedTagCount,
                    ref skippedTagBytes,
                    cancellationToken,
                    out completion,
                    out sessionBecameAvailable);
            default:
                return PlaybackBoundedFlvParserResult.MalformedData;
        }
    }

    private PlaybackBoundedFlvParserResult ProcessVideoTag(
        ReadOnlyMemory<byte> body,
        uint timestamp,
        int consumed,
        int carryBytesAtStart,
        ref int skippedTagCount,
        ref long skippedTagBytes,
        CancellationToken cancellationToken,
        out PlaybackBoundedDemuxReadCompletion? completion,
        out bool sessionBecameAvailable)
    {
        completion = null;
        sessionBecameAvailable = false;
        if (body.Length < 5)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var span = body.Span;
        var frameType = span[0] >> 4;
        var codecId = span[0] & 0x0F;
        if (codecId != AvcCodecId)
        {
            return PlaybackBoundedFlvParserResult.UnsupportedCodec;
        }

        var packetType = span[1];
        var compositionTime = ReadSignedUInt24(span.Slice(2, 3));
        if (packetType == AvcSequenceHeader)
        {
            if (frameType != 1 || compositionTime != 0)
            {
                return PlaybackBoundedFlvParserResult.MalformedData;
            }

            return ProcessAvcSequenceHeader(
                body.Slice(5),
                ref skippedTagCount,
                ref skippedTagBytes);
        }

        if (_session is null)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        if (packetType == AvcEndOfSequence)
        {
            if (frameType != 1 || compositionTime != 0 || body.Length != 5 || _videoEnded)
            {
                return PlaybackBoundedFlvParserResult.MalformedData;
            }

            var eventInput = CreateEosEvent(
                VideoStreamId,
                PlaybackBoundedDemuxMediaKind.Video,
                _nextVideoSequence);
            var result = CreateCompletion(
                PlaybackBoundedDemuxResult.PacketsAvailable,
                [eventInput],
                consumed,
                carryBytesAtStart,
                skippedTagCount,
                cancellationToken,
                out completion);
            if (result == PlaybackBoundedFlvParserResult.PacketsAvailable)
            {
                _videoEnded = true;
                _nextVideoSequence = checked(_nextVideoSequence + 1);
            }

            return result;
        }

        if (packetType != AvcNalu || frameType is not 1 and not 2 || _videoEnded)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var payload = body.Slice(5);
        var nalValidation = ValidateAvccPayload(payload.Span);
        if (nalValidation != PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            return nalValidation;
        }

        var timingResult = TryCreateVideoTiming(
            timestamp,
            compositionTime,
            out var dts,
            out var pts);
        if (timingResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            return timingResult;
        }

        var packet = new PlaybackBoundedDemuxEventInput(
            PlaybackBoundedDemuxEventKind.Packet,
            VideoStreamId,
            PlaybackBoundedDemuxMediaKind.Video,
            _nextVideoSequence,
            payload,
            pts,
            dts,
            PlaybackBoundedDemuxTime.Unknown,
            frameType == 1
                ? PlaybackBoundedDemuxKeyframeState.Keyframe
                : PlaybackBoundedDemuxKeyframeState.NotKeyframe,
            PlaybackDecodedFrameDiscontinuityKind.None);
        var completionResult = CreateCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [packet],
            consumed,
            carryBytesAtStart,
            skippedTagCount,
            cancellationToken,
            out completion);
        if (completionResult == PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            _nextVideoSequence = checked(_nextVideoSequence + 1);
        }

        return completionResult;
    }

    private PlaybackBoundedFlvParserResult ProcessAudioTag(
        ReadOnlyMemory<byte> body,
        uint timestamp,
        int consumed,
        int carryBytesAtStart,
        ref int skippedTagCount,
        ref long skippedTagBytes,
        CancellationToken cancellationToken,
        out PlaybackBoundedDemuxReadCompletion? completion,
        out bool sessionBecameAvailable)
    {
        completion = null;
        sessionBecameAvailable = false;
        if (body.Length < 2)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var soundFormat = body.Span[0] >> 4;
        if (soundFormat != AacSoundFormat)
        {
            return PlaybackBoundedFlvParserResult.UnsupportedCodec;
        }

        var packetType = body.Span[1];
        if (packetType == AacSequenceHeader)
        {
            var sequenceResult = ProcessAacSequenceHeader(
                body.Slice(2),
                ref skippedTagCount,
                ref skippedTagBytes,
                out sessionBecameAvailable);
            return sequenceResult;
        }

        if (packetType != AacRaw)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        if (_session is null)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        if (_audioEnded)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var payload = body.Slice(2);
        if (payload.IsEmpty || payload.Length > _demuxOptions.MaximumPacketPayloadBytes)
        {
            return payload.IsEmpty
                ? PlaybackBoundedFlvParserResult.MalformedData
                : PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        var conversion = PlaybackBoundedDemuxTimestampConverter.TryConvert(
            timestamp,
            FlvTimeBase,
            _demuxOptions.MaximumTimestampMagnitude,
            out var packetTime);
        if (conversion != PlaybackBoundedDemuxTimestampConversionResult.Converted)
        {
            return PlaybackBoundedFlvParserResult.TimestampConversionFailed;
        }

        var packet = new PlaybackBoundedDemuxEventInput(
            PlaybackBoundedDemuxEventKind.Packet,
            AudioStreamId,
            PlaybackBoundedDemuxMediaKind.Audio,
            _nextAudioSequence,
            payload,
            packetTime,
            packetTime,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);
        var result = CreateCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [packet],
            consumed,
            carryBytesAtStart,
            skippedTagCount,
            cancellationToken,
            out completion);
        if (result == PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            _nextAudioSequence = checked(_nextAudioSequence + 1);
        }

        return result;
    }

    private PlaybackBoundedFlvParserResult ProcessAvcSequenceHeader(
        ReadOnlyMemory<byte> configuration,
        ref int skippedTagCount,
        ref long skippedTagBytes)
    {
        if (configuration.IsEmpty ||
            configuration.Length > _demuxOptions.MaximumCodecExtradataBytes)
        {
            return configuration.IsEmpty
                ? PlaybackBoundedFlvParserResult.InvalidStreamDescription
                : PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        if (_session is not null)
        {
            if (!_session.Video.CodecExtradata.Span.SequenceEqual(configuration.Span))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            return SkipTag(
                configuration.Length,
                ref skippedTagCount,
                ref skippedTagBytes);
        }

        if (_videoConfiguration is not null)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        var parseResult = TryParseAvcConfiguration(
            configuration.Span,
            out var width,
            out var height,
            out var nalLengthSize);
        if (parseResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            return parseResult;
        }

        var skippedResult = CheckSkippedTag(
            configuration.Length,
            skippedTagCount,
            skippedTagBytes);
        if (skippedResult != PlaybackBoundedFlvParserResult.NeedMoreInput)
        {
            return skippedResult;
        }

        byte[] ownedConfiguration;
        try
        {
            ownedConfiguration = configuration.ToArray();
        }
        catch (OutOfMemoryException)
        {
            return PlaybackBoundedFlvParserResult.Faulted;
        }

        _videoConfiguration = ownedConfiguration;
        _videoWidth = width;
        _videoHeight = height;
        _nalLengthSize = nalLengthSize;
        skippedTagCount++;
        skippedTagBytes = checked(skippedTagBytes + configuration.Length);
        return PlaybackBoundedFlvParserResult.NeedMoreInput;
    }

    private PlaybackBoundedFlvParserResult ProcessAacSequenceHeader(
        ReadOnlyMemory<byte> configuration,
        ref int skippedTagCount,
        ref long skippedTagBytes,
        out bool sessionBecameAvailable)
    {
        sessionBecameAvailable = false;
        if (configuration.IsEmpty ||
            configuration.Length > _demuxOptions.MaximumCodecExtradataBytes)
        {
            return configuration.IsEmpty
                ? PlaybackBoundedFlvParserResult.InvalidStreamDescription
                : PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        if (_session is not null)
        {
            if (!_session.Audio.CodecExtradata.Span.SequenceEqual(configuration.Span))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            return SkipTag(
                configuration.Length,
                ref skippedTagCount,
                ref skippedTagBytes);
        }

        if (_videoConfiguration is null)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        var parseResult = TryParseAudioSpecificConfig(
            configuration.Span,
            out var sampleRate,
            out var channelCount);
        if (parseResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            return parseResult;
        }

        var skippedResult = CheckSkippedTag(
            configuration.Length,
            skippedTagCount,
            skippedTagBytes);
        if (skippedResult != PlaybackBoundedFlvParserResult.NeedMoreInput)
        {
            return skippedResult;
        }

        var description = new PlaybackBoundedDemuxSessionDescriptionInput(
            PlaybackBoundedDemuxContainerKind.Flv,
            new PlaybackBoundedDemuxStreamDescriptionInput(
                VideoStreamId,
                PlaybackBoundedDemuxMediaKind.Video,
                PlaybackBoundedDemuxCodec.H264,
                _videoWidth,
                _videoHeight,
                SampleRate: 0,
                ChannelCount: 0,
                FlvTimeBase,
                _videoConfiguration),
            new PlaybackBoundedDemuxStreamDescriptionInput(
                AudioStreamId,
                PlaybackBoundedDemuxMediaKind.Audio,
                PlaybackBoundedDemuxCodec.Aac,
                Width: 0,
                Height: 0,
                sampleRate,
                channelCount,
                FlvTimeBase,
                configuration));
        var createResult = PlaybackBoundedDemuxSession.TryCreate(
            _demuxOptions,
            description,
            out var session);
        if (createResult != PlaybackBoundedDemuxResult.StreamDescriptionAvailable ||
            session is null)
        {
            return MapResult(createResult);
        }

        _session = session;
        ClearVideoConfiguration();
        skippedTagCount++;
        skippedTagBytes = checked(skippedTagBytes + configuration.Length);
        sessionBecameAvailable = true;
        return PlaybackBoundedFlvParserResult.StreamDescriptionAvailable;
    }

    private PlaybackBoundedFlvParserResult CreateCompletion(
        PlaybackBoundedDemuxResult sourceResult,
        IReadOnlyList<PlaybackBoundedDemuxEventInput> events,
        int consumed,
        int carryBytesAtStart,
        int skippedTagCount,
        CancellationToken cancellationToken,
        out PlaybackBoundedDemuxReadCompletion? completion)
    {
        completion = null;
        if (_session is null)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        var observedBytes = GetObservedByteCount(consumed, carryBytesAtStart);
        var created = _session.CreateReadCompletion(
            sourceResult,
            events,
            observedBytes,
            skippedTagCount,
            cancellationToken);
        if (created.Result != sourceResult)
        {
            var mapped = MapResult(created.Result);
            created.Dispose();
            return mapped;
        }

        completion = created;
        return MapResult(sourceResult);
    }

    private PlaybackBoundedFlvParserResult TryAcquireRecord(
        ReadOnlyMemory<byte> input,
        ref int consumed,
        bool isFinal,
        out ReadOnlyMemory<byte> record,
        out bool recordFromCarry)
    {
        record = default;
        recordFromCarry = false;

        if (_carry is not null)
        {
            var fillResult = FillCarry(input, ref consumed);
            if (fillResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
            {
                return isFinal && consumed == input.Length
                    ? PlaybackBoundedFlvParserResult.TruncatedData
                    : fillResult;
            }

            record = _carry.AsMemory(0, _carryTargetLength);
            recordFromCarry = true;
            return PlaybackBoundedFlvParserResult.PacketsAvailable;
        }

        var remaining = input.Length - consumed;
        if (_phase == ParserPhase.Header)
        {
            if (remaining >= HeaderRecordLength)
            {
                record = input.Slice(consumed, HeaderRecordLength);
                consumed += HeaderRecordLength;
                return PlaybackBoundedFlvParserResult.PacketsAvailable;
            }

            BeginCarry(HeaderRecordLength);
            CopyToCarry(input.Slice(consumed).Span);
            consumed = input.Length;
            return isFinal
                ? PlaybackBoundedFlvParserResult.TruncatedData
                : PlaybackBoundedFlvParserResult.NeedMoreInput;
        }

        if (remaining < TagHeaderLength)
        {
            BeginCarry(TagHeaderLength);
            CopyToCarry(input.Slice(consumed).Span);
            consumed = input.Length;
            return isFinal
                ? PlaybackBoundedFlvParserResult.TruncatedData
                : PlaybackBoundedFlvParserResult.NeedMoreInput;
        }

        var header = input.Span.Slice(consumed, TagHeaderLength);
        var targetResult = TryGetTagRecordLength(header, out var targetLength);
        if (targetResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
        {
            return targetResult;
        }

        if (remaining >= targetLength)
        {
            record = input.Slice(consumed, targetLength);
            consumed += targetLength;
            return PlaybackBoundedFlvParserResult.PacketsAvailable;
        }

        BeginCarry(targetLength);
        CopyToCarry(input.Slice(consumed).Span);
        consumed = input.Length;
        return isFinal
            ? PlaybackBoundedFlvParserResult.TruncatedData
            : PlaybackBoundedFlvParserResult.NeedMoreInput;
    }

    private PlaybackBoundedFlvParserResult FillCarry(
        ReadOnlyMemory<byte> input,
        ref int consumed)
    {
        if (_carry is null)
        {
            return PlaybackBoundedFlvParserResult.Faulted;
        }

        var remainingInput = input.Length - consumed;
        var required = _carryTargetLength - _carryLength;
        var copyLength = Math.Min(remainingInput, required);
        if (copyLength > 0)
        {
            input.Span.Slice(consumed, copyLength).CopyTo(
                _carry.AsSpan(_carryLength, copyLength));
            _carryLength += copyLength;
            consumed += copyLength;
        }

        if (_phase == ParserPhase.Tags &&
            _carryTargetLength == TagHeaderLength &&
            _carryLength == TagHeaderLength)
        {
            var targetResult = TryGetTagRecordLength(
                _carry.AsSpan(0, TagHeaderLength),
                out var targetLength);
            if (targetResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
            {
                return targetResult;
            }

            ResizeCarry(targetLength);
            remainingInput = input.Length - consumed;
            required = _carryTargetLength - _carryLength;
            copyLength = Math.Min(remainingInput, required);
            if (copyLength > 0)
            {
                input.Span.Slice(consumed, copyLength).CopyTo(
                    _carry.AsSpan(_carryLength, copyLength));
                _carryLength += copyLength;
                consumed += copyLength;
            }
        }

        return _carryLength == _carryTargetLength
            ? PlaybackBoundedFlvParserResult.PacketsAvailable
            : PlaybackBoundedFlvParserResult.NeedMoreInput;
    }

    private PlaybackBoundedFlvParserResult TryGetTagRecordLength(
        ReadOnlySpan<byte> header,
        out int targetLength)
    {
        targetLength = 0;
        var tagType = header[0];
        if (tagType is not AudioTagType and not VideoTagType and not ScriptTagType)
        {
            return PlaybackBoundedFlvParserResult.MalformedData;
        }

        var dataSize = ReadUInt24(header.Slice(1, 3));
        var maximumDataSize = tagType == ScriptTagType
            ? _options.MaximumScriptTagDataBytes
            : _options.MaximumTagDataBytes;
        if (dataSize > maximumDataSize)
        {
            return PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        try
        {
            targetLength = checked(TagHeaderLength + dataSize + PreviousTagSizeLength);
        }
        catch (OverflowException)
        {
            return PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        return targetLength <= _options.MaximumCarryBytes
            ? PlaybackBoundedFlvParserResult.PacketsAvailable
            : PlaybackBoundedFlvParserResult.LimitExceeded;
    }

    private PlaybackBoundedFlvParserResult ValidateAvccPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > _demuxOptions.MaximumPacketPayloadBytes)
        {
            return payload.IsEmpty
                ? PlaybackBoundedFlvParserResult.MalformedData
                : PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        var offset = 0;
        var nalCount = 0;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < _nalLengthSize)
            {
                return PlaybackBoundedFlvParserResult.MalformedData;
            }

            var nalLength = ReadVariableBigEndian(
                payload.Slice(offset, _nalLengthSize));
            offset += _nalLengthSize;
            if (nalLength <= 0 || nalLength > payload.Length - offset)
            {
                return PlaybackBoundedFlvParserResult.MalformedData;
            }

            var nalType = payload[offset] & 0x1F;
            if (nalType is 0 or > 23)
            {
                return PlaybackBoundedFlvParserResult.MalformedData;
            }

            offset += nalLength;
            nalCount++;
            if (nalCount > _options.MaximumNalUnitsPerPacket)
            {
                return PlaybackBoundedFlvParserResult.LimitExceeded;
            }
        }

        return nalCount > 0
            ? PlaybackBoundedFlvParserResult.PacketsAvailable
            : PlaybackBoundedFlvParserResult.MalformedData;
    }

    private PlaybackBoundedFlvParserResult TryCreateVideoTiming(
        uint timestamp,
        int compositionTime,
        out PlaybackBoundedDemuxTime dts,
        out PlaybackBoundedDemuxTime pts)
    {
        dts = PlaybackBoundedDemuxTime.Unknown;
        pts = PlaybackBoundedDemuxTime.Unknown;
        var dtsResult = PlaybackBoundedDemuxTimestampConverter.TryConvert(
            timestamp,
            FlvTimeBase,
            _demuxOptions.MaximumTimestampMagnitude,
            out dts);
        if (dtsResult != PlaybackBoundedDemuxTimestampConversionResult.Converted)
        {
            return PlaybackBoundedFlvParserResult.TimestampConversionFailed;
        }

        long presentationMilliseconds;
        try
        {
            presentationMilliseconds = checked((long)timestamp + compositionTime);
        }
        catch (OverflowException)
        {
            return PlaybackBoundedFlvParserResult.TimestampConversionFailed;
        }

        var ptsResult = PlaybackBoundedDemuxTimestampConverter.TryConvert(
            presentationMilliseconds,
            FlvTimeBase,
            _demuxOptions.MaximumTimestampMagnitude,
            out pts);
        return ptsResult == PlaybackBoundedDemuxTimestampConversionResult.Converted
            ? PlaybackBoundedFlvParserResult.PacketsAvailable
            : PlaybackBoundedFlvParserResult.TimestampConversionFailed;
    }

    private PlaybackBoundedFlvParserResult TryParseAvcConfiguration(
        ReadOnlySpan<byte> configuration,
        out int width,
        out int height,
        out int nalLengthSize)
    {
        width = 0;
        height = 0;
        nalLengthSize = 0;
        if (configuration.Length < 7 || configuration[0] != 1)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        if ((configuration[4] & 0xFC) != 0xFC ||
            (configuration[5] & 0xE0) != 0xE0)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        nalLengthSize = (configuration[4] & 0x03) + 1;
        if (nalLengthSize == 3)
        {
            return PlaybackBoundedFlvParserResult.UnsupportedCodec;
        }

        var sequenceParameterSetCount = configuration[5] & 0x1F;
        if (sequenceParameterSetCount is < 1 ||
            sequenceParameterSetCount > _options.MaximumParameterSets)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        var offset = 6;
        var profileIdc = 0;
        var levelIdc = 0;
        for (var index = 0; index < sequenceParameterSetCount; index++)
        {
            if (!TryReadLengthPrefixedParameterSet(
                configuration,
                ref offset,
                expectedNalType: 7,
                out var parameterSet))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            var spsResult = TryParseSps(
                parameterSet,
                _options,
                out var currentWidth,
                out var currentHeight,
                out var currentProfile,
                out var currentLevel);
            if (spsResult != PlaybackBoundedFlvParserResult.PacketsAvailable)
            {
                return spsResult;
            }

            if (index == 0)
            {
                width = currentWidth;
                height = currentHeight;
                profileIdc = currentProfile;
                levelIdc = currentLevel;
            }
            else if (width != currentWidth || height != currentHeight ||
                profileIdc != currentProfile || levelIdc != currentLevel)
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }
        }

        if (profileIdc != configuration[1] || levelIdc != configuration[3] ||
            offset >= configuration.Length)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        var pictureParameterSetCount = configuration[offset++];
        if (pictureParameterSetCount is < 1 ||
            pictureParameterSetCount > _options.MaximumParameterSets)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        for (var index = 0; index < pictureParameterSetCount; index++)
        {
            if (!TryReadLengthPrefixedParameterSet(
                configuration,
                ref offset,
                expectedNalType: 8,
                out _))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }
        }

        if (offset != configuration.Length)
        {
            return PlaybackBoundedFlvParserResult.UnsupportedCodec;
        }

        return PlaybackBoundedFlvParserResult.PacketsAvailable;
    }

    private bool TryReadLengthPrefixedParameterSet(
        ReadOnlySpan<byte> configuration,
        ref int offset,
        int expectedNalType,
        out ReadOnlySpan<byte> parameterSet)
    {
        parameterSet = default;
        if (configuration.Length - offset < 2)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(configuration.Slice(offset, 2));
        offset += 2;
        if (length < 1 || length > _options.MaximumSpsRbspBytes ||
            configuration.Length - offset < length)
        {
            return false;
        }

        parameterSet = configuration.Slice(offset, length);
        offset += length;
        return (parameterSet[0] & 0x1F) == expectedNalType;
    }

    internal static PlaybackBoundedFlvParserResult TryParseSps(
        ReadOnlySpan<byte> parameterSet,
        PlaybackBoundedFlvParserOptions options,
        out int width,
        out int height,
        out int profileIdc,
        out int levelIdc)
    {
        width = 0;
        height = 0;
        profileIdc = 0;
        levelIdc = 0;
        if (parameterSet.Length < 4 || (parameterSet[0] & 0x1F) != 7)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        byte[]? rbsp = null;
        try
        {
            rbsp = RemoveEmulationPreventionBytes(parameterSet[1..], options.MaximumSpsRbspBytes);
            if (rbsp is null)
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            var reader = new FlvBitReader(rbsp);
            if (!reader.TryReadBits(8, out var profile) ||
                !reader.TryReadBits(8, out _) ||
                !reader.TryReadBits(8, out var level) ||
                !reader.TryReadUnsignedExpGolomb(out _))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            profileIdc = checked((int)profile);
            levelIdc = checked((int)level);
            var chromaFormatIdc = 1u;
            var separateColourPlaneFlag = false;
            if (IsExtendedProfile(profileIdc))
            {
                if (!reader.TryReadUnsignedExpGolomb(out chromaFormatIdc) ||
                    chromaFormatIdc > 3)
                {
                    return PlaybackBoundedFlvParserResult.UnsupportedCodec;
                }

                if (chromaFormatIdc == 3)
                {
                    if (!reader.TryReadBit(out separateColourPlaneFlag))
                    {
                        return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
                    }
                }

                if (!reader.TryReadUnsignedExpGolomb(out var bitDepthLumaMinus8) ||
                    !reader.TryReadUnsignedExpGolomb(out var bitDepthChromaMinus8) ||
                    bitDepthLumaMinus8 > 6 || bitDepthChromaMinus8 > 6 ||
                    !reader.TryReadBit(out _) ||
                    !reader.TryReadBit(out var scalingMatrixPresent))
                {
                    return PlaybackBoundedFlvParserResult.UnsupportedCodec;
                }

                if (scalingMatrixPresent)
                {
                    var scalingListCount = chromaFormatIdc == 3 ? 12 : 8;
                    for (var index = 0; index < scalingListCount; index++)
                    {
                        if (!reader.TryReadBit(out var listPresent))
                        {
                            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
                        }

                        if (listPresent && !TrySkipScalingList(
                            ref reader,
                            index < 6 ? 16 : 64))
                        {
                            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
                        }
                    }
                }
            }

            if (!reader.TryReadUnsignedExpGolomb(out var log2MaxFrameNumMinus4) ||
                log2MaxFrameNumMinus4 > 12 ||
                !reader.TryReadUnsignedExpGolomb(out var pictureOrderCountType) ||
                pictureOrderCountType > 2)
            {
                return PlaybackBoundedFlvParserResult.UnsupportedCodec;
            }

            if (pictureOrderCountType == 0)
            {
                if (!reader.TryReadUnsignedExpGolomb(out var log2MaxPictureOrderCountLsbMinus4) ||
                    log2MaxPictureOrderCountLsbMinus4 > 12)
                {
                    return PlaybackBoundedFlvParserResult.UnsupportedCodec;
                }
            }
            else if (pictureOrderCountType == 1)
            {
                if (!reader.TryReadBit(out _) ||
                    !reader.TryReadSignedExpGolomb(out _) ||
                    !reader.TryReadSignedExpGolomb(out _) ||
                    !reader.TryReadUnsignedExpGolomb(out var cycleCount) ||
                    cycleCount > options.MaximumSpsCycleOffsets)
                {
                    return PlaybackBoundedFlvParserResult.UnsupportedCodec;
                }

                for (var index = 0u; index < cycleCount; index++)
                {
                    if (!reader.TryReadSignedExpGolomb(out _))
                    {
                        return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
                    }
                }
            }

            if (!reader.TryReadUnsignedExpGolomb(out _) ||
                !reader.TryReadBit(out _) ||
                !reader.TryReadUnsignedExpGolomb(out var pictureWidthInMbsMinus1) ||
                !reader.TryReadUnsignedExpGolomb(out var pictureHeightInMapUnitsMinus1) ||
                !reader.TryReadBit(out var frameMbsOnlyFlag))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            if (!frameMbsOnlyFlag && !reader.TryReadBit(out _))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            if (!reader.TryReadBit(out _) ||
                !reader.TryReadBit(out var frameCroppingFlag))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            uint cropLeft = 0;
            uint cropRight = 0;
            uint cropTop = 0;
            uint cropBottom = 0;
            if (frameCroppingFlag &&
                (!reader.TryReadUnsignedExpGolomb(out cropLeft) ||
                 !reader.TryReadUnsignedExpGolomb(out cropRight) ||
                 !reader.TryReadUnsignedExpGolomb(out cropTop) ||
                 !reader.TryReadUnsignedExpGolomb(out cropBottom)))
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            var frameFactor = frameMbsOnlyFlag ? 1L : 2L;
            var codedWidth = checked(((long)pictureWidthInMbsMinus1 + 1) * 16);
            var codedHeight = checked(
                frameFactor * ((long)pictureHeightInMapUnitsMinus1 + 1) * 16);
            var chromaArrayType = separateColourPlaneFlag ? 0u : chromaFormatIdc;
            var subWidthC = chromaArrayType is 1 or 2 ? 2L : 1L;
            var subHeightC = chromaArrayType == 1 ? 2L : 1L;
            var cropUnitX = chromaArrayType == 0 ? 1L : subWidthC;
            var cropUnitY = chromaArrayType == 0
                ? frameFactor
                : subHeightC * frameFactor;
            var croppedWidth = checked(codedWidth -
                cropUnitX * ((long)cropLeft + cropRight));
            var croppedHeight = checked(codedHeight -
                cropUnitY * ((long)cropTop + cropBottom));
            if (croppedWidth <= 0 || croppedHeight <= 0 ||
                croppedWidth > int.MaxValue || croppedHeight > int.MaxValue)
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            width = checked((int)croppedWidth);
            height = checked((int)croppedHeight);
            return PlaybackBoundedFlvParserResult.PacketsAvailable;
        }
        catch (OverflowException)
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }
        finally
        {
            if (rbsp is not null)
            {
                Array.Clear(rbsp);
            }
        }
    }

    private static byte[]? RemoveEmulationPreventionBytes(ReadOnlySpan<byte> source, int maximumBytes)
    {
        if (source.IsEmpty || source.Length > maximumBytes)
        {
            return null;
        }

        var result = new byte[source.Length];
        var output = 0;
        var zeroCount = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var value = source[index];
            if (zeroCount >= 2 && value == 0x03)
            {
                zeroCount = 0;
                continue;
            }

            result[output++] = value;
            zeroCount = value == 0 ? zeroCount + 1 : 0;
        }

        if (output == 0)
        {
            Array.Clear(result);
            return null;
        }

        if (output == result.Length)
        {
            return result;
        }

        var trimmed = result.AsSpan(0, output).ToArray();
        Array.Clear(result);
        return trimmed;
    }

    private static bool TrySkipScalingList(ref FlvBitReader reader, int size)
    {
        var lastScale = 8;
        var nextScale = 8;
        for (var index = 0; index < size; index++)
        {
            if (nextScale != 0)
            {
                if (!reader.TryReadSignedExpGolomb(out var deltaScale))
                {
                    return false;
                }

                nextScale = (lastScale + deltaScale + 256) & 0xFF;
            }

            if (nextScale != 0)
            {
                lastScale = nextScale;
            }
        }

        return true;
    }

    private static bool IsExtendedProfile(int profileIdc) => profileIdc is
        100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or
        134 or 135;

    private static PlaybackBoundedFlvParserResult TryParseAudioSpecificConfig(
        ReadOnlySpan<byte> configuration,
        out int sampleRate,
        out int channelCount)
    {
        sampleRate = 0;
        channelCount = 0;
        var reader = new FlvBitReader(configuration);
        if (!TryReadAudioObjectType(ref reader, out var objectType) || objectType != 2 ||
            !reader.TryReadBits(4, out var frequencyIndex))
        {
            return PlaybackBoundedFlvParserResult.UnsupportedCodec;
        }

        if (frequencyIndex == 15)
        {
            if (!reader.TryReadBits(24, out var explicitFrequency) || explicitFrequency == 0 ||
                explicitFrequency > int.MaxValue)
            {
                return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
            }

            sampleRate = checked((int)explicitFrequency);
        }
        else
        {
            sampleRate = frequencyIndex switch
            {
                0 => 96_000,
                1 => 88_200,
                2 => 64_000,
                3 => 48_000,
                4 => 44_100,
                5 => 32_000,
                6 => 24_000,
                7 => 22_050,
                8 => 16_000,
                9 => 12_000,
                10 => 11_025,
                11 => 8_000,
                12 => 7_350,
                _ => 0,
            };
            if (sampleRate == 0)
            {
                return PlaybackBoundedFlvParserResult.UnsupportedCodec;
            }
        }

        if (!reader.TryReadBits(4, out var channelConfiguration))
        {
            return PlaybackBoundedFlvParserResult.InvalidStreamDescription;
        }

        channelCount = channelConfiguration switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 8,
            _ => 0,
        };
        return channelCount == 0
            ? PlaybackBoundedFlvParserResult.UnsupportedCodec
            : PlaybackBoundedFlvParserResult.PacketsAvailable;
    }

    private static bool TryReadAudioObjectType(
        ref FlvBitReader reader,
        out uint objectType)
    {
        if (!reader.TryReadBits(5, out objectType))
        {
            return false;
        }

        if (objectType != 31)
        {
            return true;
        }

        if (!reader.TryReadBits(6, out var extension))
        {
            return false;
        }

        objectType = extension + 32;
        return true;
    }

    private PlaybackBoundedFlvParserResult SkipTag(
        int dataSize,
        ref int skippedTagCount,
        ref long skippedTagBytes)
    {
        var result = CheckSkippedTag(dataSize, skippedTagCount, skippedTagBytes);
        if (result != PlaybackBoundedFlvParserResult.NeedMoreInput)
        {
            return result;
        }

        skippedTagCount++;
        skippedTagBytes = checked(skippedTagBytes + dataSize);
        return PlaybackBoundedFlvParserResult.NeedMoreInput;
    }

    private PlaybackBoundedFlvParserResult CheckSkippedTag(
        int dataSize,
        int skippedTagCount,
        long skippedTagBytes)
    {
        if (skippedTagCount >= _demuxOptions.MaximumSkippedRecordsPerRead)
        {
            return PlaybackBoundedFlvParserResult.LimitExceeded;
        }

        try
        {
            return checked(skippedTagBytes + dataSize) <=
                _options.MaximumSkippedTagBytesPerOperation
                ? PlaybackBoundedFlvParserResult.NeedMoreInput
                : PlaybackBoundedFlvParserResult.LimitExceeded;
        }
        catch (OverflowException)
        {
            return PlaybackBoundedFlvParserResult.LimitExceeded;
        }
    }

    private static PlaybackBoundedFlvParserResult ValidateHeader(ReadOnlySpan<byte> record)
    {
        if (record.Length != HeaderRecordLength ||
            record[0] != (byte)'F' || record[1] != (byte)'L' || record[2] != (byte)'V')
        {
            return PlaybackBoundedFlvParserResult.UnsupportedContainer;
        }

        if (record[3] != 1 || record[4] != 0x05 ||
            BinaryPrimitives.ReadUInt32BigEndian(record.Slice(5, 4)) != 9 ||
            BinaryPrimitives.ReadUInt32BigEndian(record.Slice(9, 4)) != 0)
        {
            return PlaybackBoundedFlvParserResult.UnsupportedContainer;
        }

        return PlaybackBoundedFlvParserResult.PacketsAvailable;
    }

    private void BeginCarry(int targetLength)
    {
        if (targetLength < 1 || targetLength > _options.MaximumCarryBytes)
        {
            throw new InvalidOperationException("FLV carry target is outside configured bounds.");
        }

        ClearCarry();
        _carry = new byte[targetLength];
        _carryTargetLength = targetLength;
    }

    private void ResizeCarry(int targetLength)
    {
        if (_carry is null || targetLength < _carryLength ||
            targetLength > _options.MaximumCarryBytes)
        {
            throw new InvalidOperationException("FLV carry resize is invalid.");
        }

        var replacement = new byte[targetLength];
        _carry.AsSpan(0, _carryLength).CopyTo(replacement);
        Array.Clear(_carry);
        _carry = replacement;
        _carryTargetLength = targetLength;
    }

    private void CopyToCarry(ReadOnlySpan<byte> source)
    {
        if (_carry is null || source.Length > _carryTargetLength - _carryLength)
        {
            throw new InvalidOperationException("FLV carry copy exceeds its target.");
        }

        source.CopyTo(_carry.AsSpan(_carryLength));
        _carryLength += source.Length;
    }

    private void ReleaseRecord(bool recordFromCarry)
    {
        if (recordFromCarry)
        {
            ClearCarry();
        }
    }

    private void ClearCarry()
    {
        var carry = Interlocked.Exchange(ref _carry, null);
        if (carry is not null)
        {
            Array.Clear(carry);
        }

        _carryLength = 0;
        _carryTargetLength = 0;
    }

    private void ClearVideoConfiguration()
    {
        var configuration = Interlocked.Exchange(ref _videoConfiguration, null);
        if (configuration is not null)
        {
            Array.Clear(configuration);
        }

        _videoWidth = 0;
        _videoHeight = 0;
    }

    private void ResetForDiscontinuity()
    {
        _session?.Dispose();
        _session = null;
        ClearCarry();
        ClearVideoConfiguration();
        _phase = ParserPhase.Header;
        _nalLengthSize = 0;
        _nextVideoSequence = 0;
        _nextAudioSequence = 0;
        _videoEnded = false;
        _audioEnded = false;
        _streamBytePosition = 0;
        _generation = checked(_generation + 1);
    }

    private bool CanObserveInput(int inputLength)
    {
        try
        {
            return checked((long)inputLength + _carryLength) <=
                _demuxOptions.MaximumInputBytesPerRead;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private long GetObservedByteCount(int consumed, int carryBytesAtStart)
    {
        return checked((long)consumed + carryBytesAtStart);
    }

    private void CommitConsumed(int consumed)
    {
        _streamBytePosition = checked(_streamBytePosition + consumed);
    }

    private PlaybackBoundedFlvParserOperation Terminal(
        PlaybackBoundedFlvParserResult result,
        int consumed,
        int skippedTagCount)
    {
        _faulted = true;
        ClearCarry();
        ClearVideoConfiguration();
        return Operation(result, consumed, null, skippedTagCount);
    }

    private PlaybackBoundedFlvParserOperation Operation(
        PlaybackBoundedFlvParserResult result,
        int consumed,
        PlaybackBoundedDemuxReadCompletion? completion,
        int skippedTagCount) =>
        new(
            result,
            consumed,
            completion,
            _streamBytePosition,
            _carryLength,
            skippedTagCount,
            _generation);

    private static PlaybackBoundedDemuxEventInput CreateEosEvent(
        int streamId,
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence) =>
        new(
            PlaybackBoundedDemuxEventKind.EndOfStream,
            streamId,
            mediaKind,
            sequence,
            ReadOnlyMemory<byte>.Empty,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);

    private static uint ReadFlvTimestamp(ReadOnlySpan<byte> tagHeader) =>
        ((uint)tagHeader[7] << 24) |
        ((uint)tagHeader[4] << 16) |
        ((uint)tagHeader[5] << 8) |
        tagHeader[6];

    private static int ReadSignedUInt24(ReadOnlySpan<byte> value)
    {
        var raw = ReadUInt24(value);
        return (raw & 0x0080_0000) != 0
            ? raw | unchecked((int)0xFF00_0000)
            : raw;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> value) =>
        (value[0] << 16) | (value[1] << 8) | value[2];

    private static int ReadVariableBigEndian(ReadOnlySpan<byte> value)
    {
        var result = 0;
        foreach (var item in value)
        {
            result = checked((result << 8) | item);
        }

        return result;
    }

    private static PlaybackBoundedFlvParserResult MapResult(
        PlaybackBoundedDemuxResult result) => result switch
        {
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable =>
                PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxResult.PacketsAvailable =>
                PlaybackBoundedFlvParserResult.PacketsAvailable,
            PlaybackBoundedDemuxResult.EndOfStream =>
                PlaybackBoundedFlvParserResult.EndOfStream,
            PlaybackBoundedDemuxResult.NeedMoreInput =>
                PlaybackBoundedFlvParserResult.NeedMoreInput,
            PlaybackBoundedDemuxResult.UnsupportedContainer =>
                PlaybackBoundedFlvParserResult.UnsupportedContainer,
            PlaybackBoundedDemuxResult.UnsupportedCodec =>
                PlaybackBoundedFlvParserResult.UnsupportedCodec,
            PlaybackBoundedDemuxResult.InvalidStreamDescription =>
                PlaybackBoundedFlvParserResult.InvalidStreamDescription,
            PlaybackBoundedDemuxResult.MalformedData =>
                PlaybackBoundedFlvParserResult.MalformedData,
            PlaybackBoundedDemuxResult.TruncatedData =>
                PlaybackBoundedFlvParserResult.TruncatedData,
            PlaybackBoundedDemuxResult.TimestampConversionFailed =>
                PlaybackBoundedFlvParserResult.TimestampConversionFailed,
            PlaybackBoundedDemuxResult.LimitExceeded =>
                PlaybackBoundedFlvParserResult.LimitExceeded,
            PlaybackBoundedDemuxResult.ActiveCompletion =>
                PlaybackBoundedFlvParserResult.ActiveCompletion,
            PlaybackBoundedDemuxResult.Cancelled =>
                PlaybackBoundedFlvParserResult.Cancelled,
            PlaybackBoundedDemuxResult.Disposed =>
                PlaybackBoundedFlvParserResult.Disposed,
            PlaybackBoundedDemuxResult.Faulted =>
                PlaybackBoundedFlvParserResult.Faulted,
            _ => PlaybackBoundedFlvParserResult.Faulted,
        };

    private static void ValidateOptions(
        PlaybackBoundedFlvParserOptions options,
        PlaybackBoundedDemuxOptions demuxOptions)
    {
        if (!ValidateDemuxOptions(demuxOptions))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The nested bounded demux options are invalid.");
        }

        if (options.MaximumTagDataBytes is < 5 or >
                PlaybackBoundedFlvParserOptions.MaximumTagDataBytesHardLimit ||
            options.MaximumScriptTagDataBytes is < 0 or >
                PlaybackBoundedFlvParserOptions.MaximumScriptTagDataBytesHardLimit ||
            options.MaximumCarryBytes is < HeaderRecordLength or >
                PlaybackBoundedFlvParserOptions.MaximumCarryBytesHardLimit ||
            options.MaximumSkippedTagBytesPerOperation is < 0 or >
                PlaybackBoundedFlvParserOptions.MaximumSkippedTagBytesPerOperationHardLimit ||
            options.MaximumSkippedTagBytesPerOperation > demuxOptions.MaximumInputBytesPerRead ||
            options.MaximumNalUnitsPerPacket is < 1 or >
                PlaybackBoundedFlvParserOptions.MaximumNalUnitsPerPacketHardLimit ||
            options.MaximumParameterSets is < 1 or >
                PlaybackBoundedFlvParserOptions.MaximumParameterSetsHardLimit ||
            options.MaximumSpsRbspBytes is < 4 or >
                PlaybackBoundedFlvParserOptions.MaximumSpsRbspBytesHardLimit ||
            options.MaximumSpsCycleOffsets is < 0 or >
                PlaybackBoundedFlvParserOptions.MaximumSpsCycleOffsetsHardLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var requiredCarry = checked(
            Math.Max(options.MaximumTagDataBytes, options.MaximumScriptTagDataBytes) +
            TagHeaderLength + PreviousTagSizeLength);
        if (options.MaximumCarryBytes < requiredCarry)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The carry limit cannot hold one accepted FLV record.");
        }
    }

    private static bool ValidateDemuxOptions(PlaybackBoundedDemuxOptions options) =>
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

    private enum ParserPhase
    {
        Header,
        Tags,
    }

    private ref struct FlvBitReader
    {
        private readonly ReadOnlySpan<byte> _source;
        private int _bitOffset;

        public FlvBitReader(ReadOnlySpan<byte> source)
        {
            _source = source;
        }

        public bool TryReadBit(out bool value)
        {
            value = false;
            if (_bitOffset >= _source.Length * 8)
            {
                return false;
            }

            var byteOffset = _bitOffset >> 3;
            var bitInByte = 7 - (_bitOffset & 7);
            value = ((_source[byteOffset] >> bitInByte) & 1) != 0;
            _bitOffset++;
            return true;
        }

        public bool TryReadBits(int count, out uint value)
        {
            value = 0;
            if (count is < 0 or > 32 || _source.Length * 8 - _bitOffset < count)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                if (!TryReadBit(out var bit))
                {
                    return false;
                }

                value = (value << 1) | (bit ? 1u : 0u);
            }

            return true;
        }

        public bool TryReadUnsignedExpGolomb(out uint value)
        {
            value = 0;
            var leadingZeros = 0;
            while (true)
            {
                if (!TryReadBit(out var bit))
                {
                    return false;
                }

                if (bit)
                {
                    break;
                }

                leadingZeros++;
                if (leadingZeros > 31)
                {
                    return false;
                }
            }

            if (leadingZeros == 0)
            {
                return true;
            }

            if (!TryReadBits(leadingZeros, out var suffix))
            {
                return false;
            }

            value = ((1u << leadingZeros) - 1) + suffix;
            return true;
        }

        public bool TryReadSignedExpGolomb(out int value)
        {
            value = 0;
            if (!TryReadUnsignedExpGolomb(out var codeNumber))
            {
                return false;
            }

            var magnitude = (codeNumber + 1) >> 1;
            if (magnitude > int.MaxValue)
            {
                return false;
            }

            value = (codeNumber & 1) == 0
                ? -checked((int)magnitude)
                : checked((int)magnitude);
            return true;
        }
    }
}

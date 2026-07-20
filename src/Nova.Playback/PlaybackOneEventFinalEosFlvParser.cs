namespace PeerCast.Nova.Playback;

public sealed class PlaybackOneEventFinalEosFlvParser : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackBoundedFlvParser _parser;
    private PlaybackBoundedDemuxReadCompletion? _activeCompletion;
    private long _nextVideoSequence;
    private long _nextAudioSequence;
    private bool _videoEnded;
    private bool _audioEnded;
    private bool _finalInputAccepted;
    private bool _faulted;
    private long _streamBytePosition;
    private int _carryByteCount;
    private int _generation;
    private int _isDisposed;

    public PlaybackOneEventFinalEosFlvParser(PlaybackBoundedFlvParserOptions? options = null)
    {
        _parser = new(options);
    }

    public PlaybackBoundedDemuxSession? Session => _parser.Session;

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

            if (_faulted)
            {
                return Operation(PlaybackBoundedFlvParserResult.Faulted, 0, null, 0);
            }

            if (discontinuity)
            {
                ResetSequencingState();
            }

            if (_finalInputAccepted && !discontinuity)
            {
                if (!input.IsEmpty)
                {
                    return Terminal(PlaybackBoundedFlvParserResult.MalformedData, 0, 0);
                }

                return EmitNextFinalEos(0, 0, cancellationToken);
            }

            var delegated = _parser.Parse(
                input,
                isFinal: false,
                discontinuity,
                cancellationToken);
            ObserveDelegated(delegated);
            if (!isFinal || IsTerminalOrBlocking(delegated.Result))
            {
                return delegated;
            }

            if (delegated.ConsumedByteCount != input.Length)
            {
                return delegated;
            }

            if (delegated.CarryByteCount != 0)
            {
                return Terminal(
                    PlaybackBoundedFlvParserResult.TruncatedData,
                    delegated.ConsumedByteCount,
                    delegated.SkippedTagCount);
            }

            _finalInputAccepted = true;
            if (delegated.Completion is not null ||
                delegated.Result == PlaybackBoundedFlvParserResult.StreamDescriptionAvailable)
            {
                return delegated;
            }

            if (delegated.Result != PlaybackBoundedFlvParserResult.NeedMoreInput)
            {
                return delegated;
            }

            return EmitNextFinalEos(
                delegated.ConsumedByteCount,
                delegated.SkippedTagCount,
                cancellationToken);
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
        _parser.Dispose();
        GC.SuppressFinalize(this);
    }

    private PlaybackBoundedFlvParserOperation EmitNextFinalEos(
        int consumed,
        int skippedTagCount,
        CancellationToken cancellationToken)
    {
        var session = _parser.Session;
        if (session is null || session.IsDisposed)
        {
            return Terminal(
                PlaybackBoundedFlvParserResult.InvalidStreamDescription,
                consumed,
                skippedTagCount);
        }

        if (_videoEnded && _audioEnded)
        {
            return Operation(
                PlaybackBoundedFlvParserResult.EndOfStream,
                consumed,
                null,
                skippedTagCount);
        }

        var mediaKind = _videoEnded
            ? PlaybackBoundedDemuxMediaKind.Audio
            : PlaybackBoundedDemuxMediaKind.Video;
        var streamId = mediaKind == PlaybackBoundedDemuxMediaKind.Video
            ? session.Video.StreamId
            : session.Audio.StreamId;
        var sequence = mediaKind == PlaybackBoundedDemuxMediaKind.Video
            ? _nextVideoSequence
            : _nextAudioSequence;
        var finalEvent = CreateEosEvent(streamId, mediaKind, sequence);
        var isLastEvent = mediaKind == PlaybackBoundedDemuxMediaKind.Audio
            ? _videoEnded
            : _audioEnded;
        var sourceResult = isLastEvent
            ? PlaybackBoundedDemuxResult.EndOfStream
            : PlaybackBoundedDemuxResult.PacketsAvailable;
        var completion = session.CreateReadCompletion(
            sourceResult,
            [finalEvent],
            consumed,
            skippedTagCount,
            cancellationToken);
        if (completion.Result != sourceResult)
        {
            var result = MapResult(completion.Result);
            completion.Dispose();
            return IsTerminalOrBlocking(result)
                ? Operation(result, consumed, null, skippedTagCount)
                : Terminal(result, consumed, skippedTagCount);
        }

        if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            _videoEnded = true;
            _nextVideoSequence = checked(sequence + 1);
        }
        else
        {
            _audioEnded = true;
            _nextAudioSequence = checked(sequence + 1);
        }

        _activeCompletion = completion;
        return Operation(
            MapResult(sourceResult),
            consumed,
            completion,
            skippedTagCount);
    }

    private void ObserveDelegated(PlaybackBoundedFlvParserOperation operation)
    {
        _streamBytePosition = operation.StreamBytePosition;
        _carryByteCount = operation.CarryByteCount;
        _generation = operation.Generation;
        if (operation.Completion is { } completion)
        {
            _activeCompletion = completion;
            foreach (var item in completion.Events)
            {
                ObserveEvent(item);
            }
        }

        if (IsFaultingResult(operation.Result))
        {
            _faulted = true;
        }
    }

    private void ObserveEvent(PlaybackBoundedDemuxEvent item)
    {
        var nextSequence = checked(item.Sequence + 1);
        if (item.MediaKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            _nextVideoSequence = nextSequence;
            if (item.Kind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                _videoEnded = true;
            }
        }
        else if (item.MediaKind == PlaybackBoundedDemuxMediaKind.Audio)
        {
            _nextAudioSequence = nextSequence;
            if (item.Kind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                _audioEnded = true;
            }
        }
    }

    private void ResetSequencingState()
    {
        _nextVideoSequence = 0;
        _nextAudioSequence = 0;
        _videoEnded = false;
        _audioEnded = false;
        _finalInputAccepted = false;
        _streamBytePosition = 0;
        _carryByteCount = 0;
    }

    private PlaybackBoundedFlvParserOperation Terminal(
        PlaybackBoundedFlvParserResult result,
        int consumed,
        int skippedTagCount)
    {
        _faulted = true;
        _carryByteCount = 0;
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
            _carryByteCount,
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

    private static bool IsTerminalOrBlocking(PlaybackBoundedFlvParserResult result) => result is
        PlaybackBoundedFlvParserResult.ActiveCompletion or
        PlaybackBoundedFlvParserResult.Cancelled or
        PlaybackBoundedFlvParserResult.Disposed or
        PlaybackBoundedFlvParserResult.Faulted or
        PlaybackBoundedFlvParserResult.UnsupportedContainer or
        PlaybackBoundedFlvParserResult.UnsupportedCodec or
        PlaybackBoundedFlvParserResult.InvalidStreamDescription or
        PlaybackBoundedFlvParserResult.MalformedData or
        PlaybackBoundedFlvParserResult.TruncatedData or
        PlaybackBoundedFlvParserResult.TimestampConversionFailed or
        PlaybackBoundedFlvParserResult.LimitExceeded;

    private static bool IsFaultingResult(PlaybackBoundedFlvParserResult result) => result is
        PlaybackBoundedFlvParserResult.UnsupportedContainer or
        PlaybackBoundedFlvParserResult.UnsupportedCodec or
        PlaybackBoundedFlvParserResult.InvalidStreamDescription or
        PlaybackBoundedFlvParserResult.MalformedData or
        PlaybackBoundedFlvParserResult.TruncatedData or
        PlaybackBoundedFlvParserResult.TimestampConversionFailed or
        PlaybackBoundedFlvParserResult.LimitExceeded or
        PlaybackBoundedFlvParserResult.Faulted;

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
}

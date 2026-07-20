namespace PeerCast.Nova.Playback;

public enum PlaybackFiniteOneEventPacketSubmissionResult
{
    PacketAccepted,
    FlushAccepted,
    ReceiveRequired,
    FrameProgressAndAccepted,
    FrameProgressAndEndOfStream,
    FrameProgressAndStillPending,
    NoReceiveProgress,
    StreamEndOfStream,
    ActiveSubmission,
    NoActiveSubmission,
    InvalidConfiguration,
    SourceMismatch,
    CompletionUnavailable,
    EventRejected,
    SequenceRejected,
    StreamAlreadyFlushed,
    Cancelled,
    Unsupported,
    GateRejected,
    NativeAllocationFailed,
    NativeRejected,
    NativeFailure,
    Disposed,
}

internal interface IPlaybackFiniteOneEventPacketSubmissionOperations
{
    PlaybackMetadataAwareNativePacketCompletion SubmitOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedDemuxReadCompletion sourceCompletion);

    PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind);

    PlaybackMetadataAwareNativePacketResult RetryOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind,
        bool isFlush);

    void ReleasePending();
}

public sealed class PlaybackFiniteOneEventPacketSubmissionStateMachine :
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IPlaybackFiniteOneEventPacketSubmissionOperations _operations;
    private PendingSubmission? _pending;
    private bool _hasVideoSequence;
    private long _lastVideoSequence;
    private bool _hasAudioSequence;
    private long _lastAudioSequence;
    private bool _videoFlushAccepted;
    private bool _audioFlushAccepted;
    private bool _videoEndOfStream;
    private bool _audioEndOfStream;
    private int _isDisposed;

    public PlaybackFiniteOneEventPacketSubmissionStateMachine()
        : this(new PlaybackFiniteOneEventPacketSubmissionOperations())
    {
    }

    internal PlaybackFiniteOneEventPacketSubmissionStateMachine(
        IPlaybackFiniteOneEventPacketSubmissionOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public bool HasActiveSubmission
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null;
            }
        }
    }

    public bool VideoFlushAccepted
    {
        get
        {
            lock (_sync)
            {
                return _videoFlushAccepted;
            }
        }
    }

    public bool AudioFlushAccepted
    {
        get
        {
            lock (_sync)
            {
                return _audioFlushAccepted;
            }
        }
    }

    public bool VideoEndOfStream
    {
        get
        {
            lock (_sync)
            {
                return _videoEndOfStream;
            }
        }
    }

    public bool AudioEndOfStream
    {
        get
        {
            lock (_sync)
            {
                return _audioEndOfStream;
            }
        }
    }

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public PlaybackFiniteOneEventPacketSubmissionResult Begin(
        PlaybackBoundedFlvNativeDecoderSession? nativeSession,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackBoundedDemuxReadCompletion? sourceCompletion,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed != 0)
            {
                sourceCompletion?.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.Disposed;
            }

            if (_pending is not null)
            {
                if (sourceCompletion is not null &&
                    !ReferenceEquals(_pending.SourceCompletion, sourceCompletion))
                {
                    sourceCompletion.Dispose();
                }

                return PlaybackFiniteOneEventPacketSubmissionResult.ActiveSubmission;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                sourceCompletion?.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.Cancelled;
            }

            var validation = ValidateBegin(
                nativeSession,
                sourceSession,
                sourceCompletion,
                out var demuxEvent);
            if (validation != PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted)
            {
                sourceCompletion?.Dispose();
                return validation;
            }

            var mediaKind = demuxEvent!.MediaKind;
            if (IsStreamFlushed(mediaKind))
            {
                sourceCompletion!.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.StreamAlreadyFlushed;
            }

            if (!CanAcceptSequence(mediaKind, demuxEvent.Sequence))
            {
                sourceCompletion!.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.SequenceRejected;
            }

            PlaybackMetadataAwareNativePacketCompletion submission;
            try
            {
                submission = _operations.SubmitOne(
                    nativeSession!,
                    sourceSession!,
                    sourceCompletion!);
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or ObjectDisposedException or NotSupportedException)
            {
                sourceCompletion!.Dispose();
                _operations.ReleasePending();
                return PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure;
            }

            if (submission.Result == PlaybackMetadataAwareNativePacketResult.NeedFrameDrain)
            {
                if (!submission.RetainsPendingOwnership)
                {
                    submission.Dispose();
                    _operations.ReleasePending();
                    return PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure;
                }

                _pending = new(
                    nativeSession!,
                    sourceSession!,
                    sourceCompletion!,
                    submission,
                    mediaKind,
                    demuxEvent.Kind,
                    demuxEvent.Sequence);
                return PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired;
            }

            try
            {
                return CompleteInitialSubmission(demuxEvent, submission.Result);
            }
            finally
            {
                submission.Dispose();
                _operations.ReleasePending();
            }
        }
    }

    public PlaybackFiniteOneEventPacketSubmissionResult Advance(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed != 0)
            {
                return PlaybackFiniteOneEventPacketSubmissionResult.Disposed;
            }

            var pending = _pending;
            if (pending is null)
            {
                return PlaybackFiniteOneEventPacketSubmissionResult.NoActiveSubmission;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                ReleasePending(pending);
                return PlaybackFiniteOneEventPacketSubmissionResult.Cancelled;
            }

            if (pending.NativeSession.IsDisposed || pending.SourceSession.IsDisposed)
            {
                ReleasePending(pending);
                return PlaybackFiniteOneEventPacketSubmissionResult.InvalidConfiguration;
            }

            if (!pending.NativeSession.ReferencesSourceSession(pending.SourceSession))
            {
                ReleasePending(pending);
                return PlaybackFiniteOneEventPacketSubmissionResult.SourceMismatch;
            }

            PlaybackDecoderFfmpegFrameReceiveResult receiveResult;
            try
            {
                receiveResult = _operations.ReceiveOne(
                    pending.NativeSession,
                    pending.MediaKind);
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or ObjectDisposedException or NotSupportedException)
            {
                ReleasePending(pending);
                return PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure;
            }

            return receiveResult switch
            {
                PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable =>
                    RetryAfterFrameProgress(pending),
                PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput =>
                    PlaybackFiniteOneEventPacketSubmissionResult.NoReceiveProgress,
                PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream =>
                    CompleteFromDecoderEndOfStream(pending),
                PlaybackDecoderFfmpegFrameReceiveResult.Unsupported =>
                    FailPending(pending, PlaybackFiniteOneEventPacketSubmissionResult.Unsupported),
                PlaybackDecoderFfmpegFrameReceiveResult.GateRejected =>
                    FailPending(pending, PlaybackFiniteOneEventPacketSubmissionResult.GateRejected),
                PlaybackDecoderFfmpegFrameReceiveResult.NativeAllocationFailed =>
                    FailPending(
                        pending,
                        PlaybackFiniteOneEventPacketSubmissionResult.NativeAllocationFailed),
                PlaybackDecoderFfmpegFrameReceiveResult.ContextUnavailable or
                PlaybackDecoderFfmpegFrameReceiveResult.ContextNotOpened or
                PlaybackDecoderFfmpegFrameReceiveResult.NativeRejected =>
                    FailPending(pending, PlaybackFiniteOneEventPacketSubmissionResult.NativeRejected),
                _ => FailPending(
                    pending,
                    PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure),
            };
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            if (_pending is { } pending)
            {
                ReleasePending(pending);
            }
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private PlaybackFiniteOneEventPacketSubmissionResult CompleteInitialSubmission(
        PlaybackBoundedDemuxEvent demuxEvent,
        PlaybackMetadataAwareNativePacketResult result)
    {
        if (result == PlaybackMetadataAwareNativePacketResult.Submitted)
        {
            CommitAccepted(demuxEvent.MediaKind, demuxEvent.Kind, demuxEvent.Sequence);
            return demuxEvent.Kind == PlaybackBoundedDemuxEventKind.EndOfStream
                ? PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted
                : PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted;
        }

        if (result == PlaybackMetadataAwareNativePacketResult.EndOfStream)
        {
            if (demuxEvent.Kind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                CommitAccepted(demuxEvent.MediaKind, demuxEvent.Kind, demuxEvent.Sequence);
            }

            MarkEndOfStream(demuxEvent.MediaKind);
            return PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream;
        }

        return MapPacketFailure(result);
    }

    private PlaybackFiniteOneEventPacketSubmissionResult RetryAfterFrameProgress(
        PendingSubmission pending)
    {
        PlaybackMetadataAwareNativePacketResult retryResult;
        try
        {
            retryResult = _operations.RetryOne(
                pending.NativeSession,
                pending.MediaKind,
                pending.EventKind == PlaybackBoundedDemuxEventKind.EndOfStream);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            ReleasePending(pending);
            return PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure;
        }

        if (retryResult == PlaybackMetadataAwareNativePacketResult.NeedFrameDrain)
        {
            return PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndStillPending;
        }

        if (retryResult == PlaybackMetadataAwareNativePacketResult.Submitted)
        {
            CommitAccepted(pending.MediaKind, pending.EventKind, pending.Sequence);
            ReleasePending(pending);
            return PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted;
        }

        if (retryResult == PlaybackMetadataAwareNativePacketResult.EndOfStream)
        {
            if (pending.EventKind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                CommitAccepted(pending.MediaKind, pending.EventKind, pending.Sequence);
            }

            MarkEndOfStream(pending.MediaKind);
            ReleasePending(pending);
            return PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndEndOfStream;
        }

        return FailPending(pending, MapPacketFailure(retryResult));
    }

    private PlaybackFiniteOneEventPacketSubmissionResult CompleteFromDecoderEndOfStream(
        PendingSubmission pending)
    {
        if (pending.EventKind == PlaybackBoundedDemuxEventKind.EndOfStream)
        {
            CommitAccepted(pending.MediaKind, pending.EventKind, pending.Sequence);
        }

        MarkEndOfStream(pending.MediaKind);
        ReleasePending(pending);
        return PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream;
    }

    private PlaybackFiniteOneEventPacketSubmissionResult FailPending(
        PendingSubmission pending,
        PlaybackFiniteOneEventPacketSubmissionResult result)
    {
        ReleasePending(pending);
        return result;
    }

    private void ReleasePending(PendingSubmission pending)
    {
        if (!ReferenceEquals(_pending, pending))
        {
            return;
        }

        _pending = null;
        try
        {
            pending.Completion.Dispose();
        }
        finally
        {
            _operations.ReleasePending();
        }
    }

    private static PlaybackFiniteOneEventPacketSubmissionResult ValidateBegin(
        PlaybackBoundedFlvNativeDecoderSession? nativeSession,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackBoundedDemuxReadCompletion? sourceCompletion,
        out PlaybackBoundedDemuxEvent? demuxEvent)
    {
        demuxEvent = null;
        if (nativeSession is null || sourceSession is null || sourceCompletion is null ||
            nativeSession.IsDisposed || sourceSession.IsDisposed)
        {
            return PlaybackFiniteOneEventPacketSubmissionResult.InvalidConfiguration;
        }

        if (!nativeSession.ReferencesSourceSession(sourceSession))
        {
            return PlaybackFiniteOneEventPacketSubmissionResult.SourceMismatch;
        }

        if (sourceCompletion.IsDisposed)
        {
            return PlaybackFiniteOneEventPacketSubmissionResult.CompletionUnavailable;
        }

        if (sourceCompletion.Events.Count != 1)
        {
            return PlaybackFiniteOneEventPacketSubmissionResult.EventRejected;
        }

        demuxEvent = sourceCompletion.Events[0];
        if (!Enum.IsDefined(demuxEvent.Kind) ||
            !Enum.IsDefined(demuxEvent.MediaKind) ||
            demuxEvent.Sequence < 0 ||
            demuxEvent.Kind is not PlaybackBoundedDemuxEventKind.Packet and
                not PlaybackBoundedDemuxEventKind.EndOfStream)
        {
            return PlaybackFiniteOneEventPacketSubmissionResult.EventRejected;
        }

        var streamMatches = demuxEvent.MediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                demuxEvent.StreamId == nativeSession.VideoStreamId &&
                demuxEvent.StreamId == sourceSession.Video.StreamId,
            PlaybackBoundedDemuxMediaKind.Audio =>
                demuxEvent.StreamId == nativeSession.AudioStreamId &&
                demuxEvent.StreamId == sourceSession.Audio.StreamId,
            _ => false,
        };

        return streamMatches
            ? PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted
            : PlaybackFiniteOneEventPacketSubmissionResult.EventRejected;
    }

    private bool CanAcceptSequence(PlaybackBoundedDemuxMediaKind mediaKind, long sequence) =>
        mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                !_hasVideoSequence || sequence > _lastVideoSequence,
            PlaybackBoundedDemuxMediaKind.Audio =>
                !_hasAudioSequence || sequence > _lastAudioSequence,
            _ => false,
        };

    private bool IsStreamFlushed(PlaybackBoundedDemuxMediaKind mediaKind) => mediaKind switch
    {
        PlaybackBoundedDemuxMediaKind.Video => _videoFlushAccepted || _videoEndOfStream,
        PlaybackBoundedDemuxMediaKind.Audio => _audioFlushAccepted || _audioEndOfStream,
        _ => true,
    };

    private void CommitAccepted(
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackBoundedDemuxEventKind eventKind,
        long sequence)
    {
        if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            _hasVideoSequence = true;
            _lastVideoSequence = sequence;
            if (eventKind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                _videoFlushAccepted = true;
            }
        }
        else
        {
            _hasAudioSequence = true;
            _lastAudioSequence = sequence;
            if (eventKind == PlaybackBoundedDemuxEventKind.EndOfStream)
            {
                _audioFlushAccepted = true;
            }
        }
    }

    private void MarkEndOfStream(PlaybackBoundedDemuxMediaKind mediaKind)
    {
        if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            _videoEndOfStream = true;
        }
        else if (mediaKind == PlaybackBoundedDemuxMediaKind.Audio)
        {
            _audioEndOfStream = true;
        }
    }

    private static PlaybackFiniteOneEventPacketSubmissionResult MapPacketFailure(
        PlaybackMetadataAwareNativePacketResult result) => result switch
        {
            PlaybackMetadataAwareNativePacketResult.InvalidConfiguration =>
                PlaybackFiniteOneEventPacketSubmissionResult.InvalidConfiguration,
            PlaybackMetadataAwareNativePacketResult.SourceMismatch =>
                PlaybackFiniteOneEventPacketSubmissionResult.SourceMismatch,
            PlaybackMetadataAwareNativePacketResult.CompletionUnavailable =>
                PlaybackFiniteOneEventPacketSubmissionResult.CompletionUnavailable,
            PlaybackMetadataAwareNativePacketResult.EventRejected or
            PlaybackMetadataAwareNativePacketResult.PacketTooLarge or
            PlaybackMetadataAwareNativePacketResult.TimestampRejected =>
                PlaybackFiniteOneEventPacketSubmissionResult.EventRejected,
            PlaybackMetadataAwareNativePacketResult.Unsupported =>
                PlaybackFiniteOneEventPacketSubmissionResult.Unsupported,
            PlaybackMetadataAwareNativePacketResult.GateRejected =>
                PlaybackFiniteOneEventPacketSubmissionResult.GateRejected,
            PlaybackMetadataAwareNativePacketResult.NativeAllocationFailed =>
                PlaybackFiniteOneEventPacketSubmissionResult.NativeAllocationFailed,
            PlaybackMetadataAwareNativePacketResult.NativeRejected =>
                PlaybackFiniteOneEventPacketSubmissionResult.NativeRejected,
            _ => PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure,
        };

    private sealed record PendingSubmission(
        PlaybackBoundedFlvNativeDecoderSession NativeSession,
        PlaybackBoundedDemuxSession SourceSession,
        PlaybackBoundedDemuxReadCompletion SourceCompletion,
        PlaybackMetadataAwareNativePacketCompletion Completion,
        PlaybackBoundedDemuxMediaKind MediaKind,
        PlaybackBoundedDemuxEventKind EventKind,
        long Sequence);
}

internal sealed class PlaybackFiniteOneEventPacketSubmissionOperations :
    IPlaybackFiniteOneEventPacketSubmissionOperations,
    IPlaybackMetadataAwareNativePacketOperations
{
    private readonly PlaybackMetadataAwareNativePacketOperations _packetOperations = new();
    private readonly PlaybackMetadataAwareNativePacketBoundary _packetBoundary;
    private readonly PlaybackDecoderFfmpegPacketFrameBoundary _frameBoundary = new();
    private IPlaybackMetadataAwareNativePacketOwner? _pendingPacket;
    private PlaybackBoundedDemuxMediaKind _pendingMediaKind;
    private bool _pendingIsFlush;

    public PlaybackFiniteOneEventPacketSubmissionOperations()
    {
        _packetBoundary = new(
            this,
            OperatingSystem.IsWindows() && Environment.Is64BitProcess);
    }

    public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedDemuxReadCompletion sourceCompletion)
    {
        ReleasePending();
        if (sourceCompletion.Events.Count == 1)
        {
            var demuxEvent = sourceCompletion.Events[0];
            _pendingMediaKind = demuxEvent.MediaKind;
            _pendingIsFlush = demuxEvent.Kind == PlaybackBoundedDemuxEventKind.EndOfStream;
        }

        var result = _packetBoundary.SubmitOne(nativeSession, sourceSession, sourceCompletion);
        if (result.Result != PlaybackMetadataAwareNativePacketResult.NeedFrameDrain)
        {
            ReleasePending();
        }

        return result;
    }

    public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind) =>
        _frameBoundary.ReceiveFrame(
            PlaybackMetadataAwareNativePacketOperations.GetContext(nativeSession, mediaKind));

    public PlaybackMetadataAwareNativePacketResult RetryOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind,
        bool isFlush)
    {
        if (mediaKind != _pendingMediaKind || isFlush != _pendingIsFlush)
        {
            return PlaybackMetadataAwareNativePacketResult.NativeRejected;
        }

        var result = isFlush
            ? _packetOperations.SendFlush(nativeSession, mediaKind)
            : _pendingPacket?.SendOnce(nativeSession, mediaKind) ??
                PlaybackMetadataAwareNativeSendResult.Rejected;
        return MapNativeSend(result);
    }

    public void ReleasePending()
    {
        _pendingPacket = null;
        _pendingMediaKind = default;
        _pendingIsFlush = false;
    }

    public PlaybackMetadataAwareNativePacketCreation CreatePacket(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackMetadataAwareNativePacketInput input)
    {
        var creation = _packetOperations.CreatePacket(session, mediaKind, input);
        if (creation.Result == PlaybackMetadataAwareNativeSendResult.Success)
        {
            _pendingPacket = creation.Owner;
        }

        return creation;
    }

    public PlaybackMetadataAwareNativeSendResult SendFlush(
        PlaybackBoundedFlvNativeDecoderSession session,
        PlaybackBoundedDemuxMediaKind mediaKind) =>
        _packetOperations.SendFlush(session, mediaKind);

    private static PlaybackMetadataAwareNativePacketResult MapNativeSend(
        PlaybackMetadataAwareNativeSendResult result) => result switch
        {
            PlaybackMetadataAwareNativeSendResult.Success =>
                PlaybackMetadataAwareNativePacketResult.Submitted,
            PlaybackMetadataAwareNativeSendResult.Again =>
                PlaybackMetadataAwareNativePacketResult.NeedFrameDrain,
            PlaybackMetadataAwareNativeSendResult.EndOfStream =>
                PlaybackMetadataAwareNativePacketResult.EndOfStream,
            PlaybackMetadataAwareNativeSendResult.GateRejected =>
                PlaybackMetadataAwareNativePacketResult.GateRejected,
            PlaybackMetadataAwareNativeSendResult.AllocationFailed =>
                PlaybackMetadataAwareNativePacketResult.NativeAllocationFailed,
            _ => PlaybackMetadataAwareNativePacketResult.NativeRejected,
        };
}

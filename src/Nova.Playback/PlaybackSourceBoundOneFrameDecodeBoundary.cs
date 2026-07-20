namespace PeerCast.Nova.Playback;

public enum PlaybackSourceBoundOneFrameDecodeMode
{
    Ordinary,
    PendingSubmissionRelief,
    FlushDrain,
}

public enum PlaybackSourceBoundOneFrameDecodeState
{
    Accepting,
    Faulted,
    Disposed,
}

public enum PlaybackSourceBoundOneFrameDecodeResult
{
    None,
    FrameAvailable,
    NeedMoreInput,
    EndOfStream,
    SubmissionAccepted,
    SubmissionStillPending,
    ActiveCompletion,
    NoActiveSubmission,
    InvalidConfiguration,
    SourceMismatch,
    GenerationMismatch,
    StreamRejected,
    StreamRequiresDrain,
    StreamNotFlushed,
    StreamAlreadyEnded,
    SubmissionRejected,
    DecoderNoProgress,
    WorkLimitExceeded,
    Cancelled,
    Unsupported,
    GateRejected,
    NativeAllocationFailed,
    FrameRejected,
    NativeRejected,
    NativeFailure,
    Faulted,
    Disposed,
}

public sealed record PlaybackSourceBoundOneFrameDecodeOptions
{
    public const long DefaultMaximumManagedFrameBytes = 64L * 1024 * 1024;
    public const long MaximumManagedFrameBytesHardLimit = 256L * 1024 * 1024;

    public long MaximumManagedFrameBytes { get; init; } = DefaultMaximumManagedFrameBytes;
}

public sealed record PlaybackSourceBoundOneFrameDecodeSnapshot(
    PlaybackSourceBoundOneFrameDecodeState State,
    PlaybackSourceBoundOneFrameDecodeResult Result,
    int DecoderGeneration,
    int DiscontinuityGeneration,
    long NextVideoSequence,
    long NextAudioSequence,
    bool VideoFlushAccepted,
    bool AudioFlushAccepted,
    bool VideoEndOfStream,
    bool AudioEndOfStream,
    bool HasActiveDecodedCompletion,
    bool HasActiveSubmission,
    bool HasReliefRetry);

public sealed class PlaybackSourceBoundOneFrameDecodeBudget
{
    public const int MaximumFrameCountHardLimit = 4_096;
    public const long MaximumManagedBytesHardLimit =
        PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit;

    private readonly object _sync = new();
    private int _remainingFrameCount;
    private long _remainingManagedBytes;

    public PlaybackSourceBoundOneFrameDecodeBudget(
        int maximumFrameCount,
        long maximumManagedBytes)
    {
        if (maximumFrameCount < 1 || maximumFrameCount > MaximumFrameCountHardLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameCount),
                maximumFrameCount,
                "The frame-count budget is outside the supported range.");
        }

        if (maximumManagedBytes < 1 || maximumManagedBytes > MaximumManagedBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumManagedBytes),
                maximumManagedBytes,
                "The managed-byte budget is outside the supported range.");
        }

        _remainingFrameCount = maximumFrameCount;
        _remainingManagedBytes = maximumManagedBytes;
    }

    public int RemainingFrameCount
    {
        get
        {
            lock (_sync)
            {
                return _remainingFrameCount;
            }
        }
    }

    public long RemainingManagedBytes
    {
        get
        {
            lock (_sync)
            {
                return _remainingManagedBytes;
            }
        }
    }

    internal bool CanReceiveFrame
    {
        get
        {
            lock (_sync)
            {
                return _remainingFrameCount > 0 && _remainingManagedBytes > 0;
            }
        }
    }

    internal bool TryConsume(long managedBytes)
    {
        if (managedBytes < 1)
        {
            return false;
        }

        lock (_sync)
        {
            if (_remainingFrameCount < 1 || managedBytes > _remainingManagedBytes)
            {
                return false;
            }

            _remainingFrameCount--;
            _remainingManagedBytes -= managedBytes;
            return true;
        }
    }
}

public sealed record PlaybackSourceBoundOneFrameDecodeOperation(
    PlaybackSourceBoundOneFrameDecodeResult Result,
    PlaybackSourceBoundDecodedFrameCompletion? Completion,
    PlaybackFiniteOneEventPacketSubmissionResult SubmissionResult);

public sealed class PlaybackSourceBoundDecodedFrameCompletion : IDisposable, IAsyncDisposable
{
    private PlaybackSourceBoundOneFrameDecodeBoundary? _owner;
    private PlaybackDecoderFfmpegManagedFrame? _frame;

    internal PlaybackSourceBoundDecodedFrameCompletion(
        PlaybackSourceBoundOneFrameDecodeBoundary owner,
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        long sequence,
        long managedByteCount,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        PlaybackDecoderFfmpegManagedFrame frame)
    {
        _owner = owner;
        _frame = frame;
        SourceSession = sourceSession;
        DecoderGeneration = decoderGeneration;
        DiscontinuityGeneration = discontinuityGeneration;
        MediaKind = mediaKind;
        StreamId = streamId;
        Sequence = sequence;
        ManagedByteCount = managedByteCount;
        Mode = mode;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public int DecoderGeneration { get; }

    public int DiscontinuityGeneration { get; }

    public PlaybackBoundedDemuxMediaKind MediaKind { get; }

    public int StreamId { get; }

    public long Sequence { get; }

    public long ManagedByteCount { get; }

    public PlaybackDecoderFfmpegManagedFrame Frame
    {
        get
        {
            var value = Volatile.Read(ref _frame);
            ObjectDisposedException.ThrowIf(value is null, this);
            return value;
        }
    }

    public bool IsDisposed => Volatile.Read(ref _frame) is null;

    internal PlaybackSourceBoundOneFrameDecodeMode Mode { get; }

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
        ReferenceEquals(SourceSession, sourceSession);

    public void Dispose()
    {
        var frame = Interlocked.Exchange(ref _frame, null);
        var owner = Interlocked.Exchange(ref _owner, null);
        try
        {
            frame?.Dispose();
        }
        finally
        {
            owner?.ReleaseCompletion(this);
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal readonly record struct PlaybackSourceBoundOneFrameNativeReceive(
    PlaybackDecoderFfmpegManagedFrameCopyResult Result,
    PlaybackDecoderFfmpegManagedFrame? Frame);

internal interface IPlaybackSourceBoundOneFrameDecodeOperations
{
    bool ValidateContextRole(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind);

    PlaybackSourceBoundOneFrameNativeReceive ReceiveOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind);
}

public sealed class PlaybackSourceBoundOneFrameDecodeBoundary : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackBoundedDemuxSession _sourceSession;
    private readonly int _decoderGeneration;
    private readonly int _discontinuityGeneration;
    private readonly PlaybackSourceBoundOneFrameDecodeOptions _options;
    private readonly IPlaybackSourceBoundOneFrameDecodeOperations _receiveOperations;
    private readonly StagedSubmissionOperations _stagedSubmissionOperations;

    private PlaybackFiniteOneEventPacketSubmissionStateMachine? _submission;
    private PlaybackBoundedFlvNativeDecoderSession? _nativeSession;
    private PlaybackSourceBoundDecodedFrameCompletion? _activeCompletion;
    private PlaybackSourceBoundOneFrameDecodeState _state =
        PlaybackSourceBoundOneFrameDecodeState.Accepting;
    private PlaybackSourceBoundOneFrameDecodeResult _result =
        PlaybackSourceBoundOneFrameDecodeResult.None;
    private long _nextVideoSequence;
    private long _nextAudioSequence;
    private bool _videoEndOfStream;
    private bool _audioEndOfStream;
    private bool _reliefRetryReady;
    private PlaybackBoundedDemuxMediaKind _reliefMediaKind;

    public PlaybackSourceBoundOneFrameDecodeBoundary(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        int decoderGeneration,
        int discontinuityGeneration = 0,
        PlaybackSourceBoundOneFrameDecodeOptions? options = null)
        : this(
            sourceSession,
            nativeSession,
            decoderGeneration,
            discontinuityGeneration,
            options,
            new PlaybackFiniteOneEventPacketSubmissionOperations(),
            new PlaybackSourceBoundOneFrameDecodeOperations())
    {
    }

    internal PlaybackSourceBoundOneFrameDecodeBoundary(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackSourceBoundOneFrameDecodeOptions? options,
        IPlaybackFiniteOneEventPacketSubmissionOperations submissionOperations,
        IPlaybackSourceBoundOneFrameDecodeOperations receiveOperations)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        ArgumentNullException.ThrowIfNull(nativeSession);
        ArgumentNullException.ThrowIfNull(submissionOperations);
        ArgumentNullException.ThrowIfNull(receiveOperations);

        if (decoderGeneration < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decoderGeneration),
                decoderGeneration,
                "The decoder generation cannot be negative.");
        }

        if (discontinuityGeneration < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discontinuityGeneration),
                discontinuityGeneration,
                "The discontinuity generation cannot be negative.");
        }

        _options = options is null
            ? new PlaybackSourceBoundOneFrameDecodeOptions()
            : options with { };
        ValidateOptions(_options);

        if (!IsValidSourceBinding(sourceSession, nativeSession) ||
            !receiveOperations.ValidateContextRole(
                nativeSession,
                PlaybackBoundedDemuxMediaKind.Video) ||
            !receiveOperations.ValidateContextRole(
                nativeSession,
                PlaybackBoundedDemuxMediaKind.Audio))
        {
            throw new ArgumentException(
                "The native decoder session must match the exact live H.264/AAC source session.",
                nameof(nativeSession));
        }

        _sourceSession = sourceSession;
        _nativeSession = nativeSession;
        _decoderGeneration = decoderGeneration;
        _discontinuityGeneration = discontinuityGeneration;
        _receiveOperations = receiveOperations;
        _stagedSubmissionOperations = new(submissionOperations);
        _submission = new(_stagedSubmissionOperations);
    }

    public PlaybackSourceBoundOneFrameDecodeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var submission = _submission;
                return new(
                    _state,
                    _result,
                    _decoderGeneration,
                    _discontinuityGeneration,
                    _nextVideoSequence,
                    _nextAudioSequence,
                    submission?.VideoFlushAccepted == true,
                    submission?.AudioFlushAccepted == true,
                    IsStreamEndOfStream(PlaybackBoundedDemuxMediaKind.Video),
                    IsStreamEndOfStream(PlaybackBoundedDemuxMediaKind.Audio),
                    _activeCompletion is not null,
                    submission?.HasActiveSubmission == true,
                    _reliefRetryReady);
            }
        }
    }

    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _state == PlaybackSourceBoundOneFrameDecodeState.Disposed;
            }
        }
    }

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
        ReferenceEquals(_sourceSession, sourceSession);

    public PlaybackFiniteOneEventPacketSubmissionResult BeginSubmission(
        PlaybackBoundedDemuxReadCompletion? sourceCompletion,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state == PlaybackSourceBoundOneFrameDecodeState.Disposed)
            {
                sourceCompletion?.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.Disposed;
            }

            if (_state == PlaybackSourceBoundOneFrameDecodeState.Faulted)
            {
                sourceCompletion?.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure;
            }

            if (_activeCompletion is not null || _reliefRetryReady)
            {
                sourceCompletion?.Dispose();
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion);
                return PlaybackFiniteOneEventPacketSubmissionResult.ActiveSubmission;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                sourceCompletion?.Dispose();
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.Cancelled);
                return PlaybackFiniteOneEventPacketSubmissionResult.Cancelled;
            }

            var submission = _submission;
            var nativeSession = _nativeSession;
            if (submission is null || nativeSession is null ||
                submission.IsDisposed || nativeSession.IsDisposed || _sourceSession.IsDisposed ||
                !IsValidSourceBinding(_sourceSession, nativeSession))
            {
                sourceCompletion?.Dispose();
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.InvalidConfiguration);
                return PlaybackFiniteOneEventPacketSubmissionResult.InvalidConfiguration;
            }

            if (sourceCompletion is { IsDisposed: false } &&
                sourceCompletion.Events.Count == 1 &&
                IsStreamEndOfStream(sourceCompletion.Events[0].MediaKind))
            {
                sourceCompletion.Dispose();
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.StreamAlreadyEnded);
                return PlaybackFiniteOneEventPacketSubmissionResult.StreamAlreadyFlushed;
            }

            try
            {
                var result = submission.Begin(
                    nativeSession,
                    _sourceSession,
                    sourceCompletion,
                    cancellationToken);
                SynchronizeEndOfStream(submission);
                SetResult(result switch
                {
                    PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted or
                    PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted =>
                        PlaybackSourceBoundOneFrameDecodeResult.SubmissionAccepted,
                    PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired =>
                        PlaybackSourceBoundOneFrameDecodeResult.SubmissionStillPending,
                    PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream =>
                        PlaybackSourceBoundOneFrameDecodeResult.EndOfStream,
                    PlaybackFiniteOneEventPacketSubmissionResult.Cancelled =>
                        PlaybackSourceBoundOneFrameDecodeResult.Cancelled,
                    _ => PlaybackSourceBoundOneFrameDecodeResult.SubmissionRejected,
                });
                return result;
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or ObjectDisposedException or NotSupportedException or
                OverflowException)
            {
                sourceCompletion?.Dispose();
                _ = FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeFailure);
                return PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure;
            }
        }
    }

    public PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
        PlaybackBoundedDemuxSession? sourceSession,
        int decoderGeneration,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        CancellationToken cancellationToken = default,
        PlaybackSourceBoundOneFrameDecodeBudget? budget = null)
    {
        lock (_sync)
        {
            if (_state == PlaybackSourceBoundOneFrameDecodeState.Disposed)
            {
                return Complete(PlaybackSourceBoundOneFrameDecodeResult.Disposed);
            }

            if (_state == PlaybackSourceBoundOneFrameDecodeState.Faulted)
            {
                return Complete(PlaybackSourceBoundOneFrameDecodeResult.Faulted);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.Cancelled);
                return Complete(PlaybackSourceBoundOneFrameDecodeResult.Cancelled);
            }

            var validation = ValidateReceiveRequest(
                sourceSession,
                decoderGeneration,
                mediaKind,
                streamId,
                mode);
            if (validation != PlaybackSourceBoundOneFrameDecodeResult.None)
            {
                SetResult(validation);
                return Complete(validation);
            }

            if (_activeCompletion is not null)
            {
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion);
                return Complete(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion);
            }

            if (_reliefRetryReady)
            {
                if (mode != PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief ||
                    mediaKind != _reliefMediaKind)
                {
                    SetResult(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion);
                    return Complete(PlaybackSourceBoundOneFrameDecodeResult.ActiveCompletion);
                }

                return RetryPendingAfterReleasedFrame(cancellationToken);
            }

            var modeValidation = ValidateMode(mediaKind, mode);
            if (modeValidation != PlaybackSourceBoundOneFrameDecodeResult.None)
            {
                SetResult(modeValidation);
                return Complete(modeValidation);
            }

            if (budget is not null && !budget.CanReceiveFrame)
            {
                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded);
            }

            var nativeSession = _nativeSession!;
            PlaybackSourceBoundOneFrameNativeReceive receive;
            try
            {
                receive = _receiveOperations.ReceiveOne(nativeSession, mediaKind);
            }
            catch (OutOfMemoryException)
            {
                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeAllocationFailed);
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or ObjectDisposedException or NotSupportedException or
                OverflowException)
            {
                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeFailure);
            }

            if (!Enum.IsDefined(receive.Result))
            {
                receive.Frame?.Dispose();
                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeFailure);
            }

            if (receive.Result == PlaybackDecoderFfmpegManagedFrameCopyResult.Copied)
            {
                if (receive.Frame is null)
                {
                    return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeFailure);
                }

                return CompleteFrame(
                    receive.Frame,
                    mediaKind,
                    streamId,
                    mode,
                    cancellationToken,
                    budget);
            }

            if (receive.Frame is not null)
            {
                receive.Frame.Dispose();
                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeFailure);
            }

            if (receive.Result == PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput)
            {
                if (mode == PlaybackSourceBoundOneFrameDecodeMode.Ordinary)
                {
                    SetResult(PlaybackSourceBoundOneFrameDecodeResult.NeedMoreInput);
                    return Complete(PlaybackSourceBoundOneFrameDecodeResult.NeedMoreInput);
                }

                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.DecoderNoProgress);
            }

            if (receive.Result == PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream)
            {
                return CompleteEndOfStream(mediaKind, mode, cancellationToken);
            }

            return FailLocked(MapReceiveFailure(receive.Result));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_state == PlaybackSourceBoundOneFrameDecodeState.Disposed)
            {
                return;
            }

            _state = PlaybackSourceBoundOneFrameDecodeState.Disposed;
            _result = PlaybackSourceBoundOneFrameDecodeResult.Disposed;
            DisposeOwnedLocked();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void ReleaseCompletion(PlaybackSourceBoundDecodedFrameCompletion completion)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_activeCompletion, completion))
            {
                return;
            }

            _activeCompletion = null;
            if (_state == PlaybackSourceBoundOneFrameDecodeState.Accepting &&
                completion.Mode == PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief &&
                _submission?.HasActiveSubmission == true)
            {
                _reliefRetryReady = true;
                _reliefMediaKind = completion.MediaKind;
            }
        }
    }

    private PlaybackSourceBoundOneFrameDecodeResult ValidateReceiveRequest(
        PlaybackBoundedDemuxSession? sourceSession,
        int decoderGeneration,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackSourceBoundOneFrameDecodeMode mode)
    {
        if (sourceSession is null || sourceSession.IsDisposed || _sourceSession.IsDisposed ||
            _nativeSession is null || _nativeSession.IsDisposed ||
            _submission is null || _submission.IsDisposed)
        {
            return PlaybackSourceBoundOneFrameDecodeResult.InvalidConfiguration;
        }

        if (!ReferenceEquals(sourceSession, _sourceSession) ||
            !_nativeSession.ReferencesSourceSession(sourceSession))
        {
            return PlaybackSourceBoundOneFrameDecodeResult.SourceMismatch;
        }

        if (decoderGeneration != _decoderGeneration)
        {
            return PlaybackSourceBoundOneFrameDecodeResult.GenerationMismatch;
        }

        if (!Enum.IsDefined(mediaKind) || !Enum.IsDefined(mode))
        {
            return PlaybackSourceBoundOneFrameDecodeResult.InvalidConfiguration;
        }

        var streamMatches = mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                streamId == _sourceSession.Video.StreamId &&
                streamId == _nativeSession.VideoStreamId &&
                _sourceSession.Video.MediaKind == PlaybackBoundedDemuxMediaKind.Video &&
                _sourceSession.Video.Codec == PlaybackBoundedDemuxCodec.H264,
            PlaybackBoundedDemuxMediaKind.Audio =>
                streamId == _sourceSession.Audio.StreamId &&
                streamId == _nativeSession.AudioStreamId &&
                _sourceSession.Audio.MediaKind == PlaybackBoundedDemuxMediaKind.Audio &&
                _sourceSession.Audio.Codec == PlaybackBoundedDemuxCodec.Aac,
            _ => false,
        };
        if (!streamMatches || !_receiveOperations.ValidateContextRole(_nativeSession, mediaKind))
        {
            return PlaybackSourceBoundOneFrameDecodeResult.StreamRejected;
        }

        return PlaybackSourceBoundOneFrameDecodeResult.None;
    }

    private PlaybackSourceBoundOneFrameDecodeResult ValidateMode(
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackSourceBoundOneFrameDecodeMode mode)
    {
        var submission = _submission!;
        if (mode == PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief)
        {
            if (!submission.HasActiveSubmission ||
                !_stagedSubmissionOperations.TryGetPending(out var pendingMediaKind, out _))
            {
                return PlaybackSourceBoundOneFrameDecodeResult.NoActiveSubmission;
            }

            return pendingMediaKind == mediaKind
                ? PlaybackSourceBoundOneFrameDecodeResult.None
                : PlaybackSourceBoundOneFrameDecodeResult.StreamRejected;
        }

        if (submission.HasActiveSubmission)
        {
            return PlaybackSourceBoundOneFrameDecodeResult.SubmissionStillPending;
        }

        if (IsStreamEndOfStream(mediaKind))
        {
            return PlaybackSourceBoundOneFrameDecodeResult.StreamAlreadyEnded;
        }

        var flushAccepted = IsFlushAccepted(mediaKind);
        if (mode == PlaybackSourceBoundOneFrameDecodeMode.Ordinary)
        {
            return flushAccepted
                ? PlaybackSourceBoundOneFrameDecodeResult.StreamRequiresDrain
                : PlaybackSourceBoundOneFrameDecodeResult.None;
        }

        return flushAccepted
            ? PlaybackSourceBoundOneFrameDecodeResult.None
            : PlaybackSourceBoundOneFrameDecodeResult.StreamNotFlushed;
    }

    private PlaybackSourceBoundOneFrameDecodeOperation CompleteFrame(
        PlaybackDecoderFfmpegManagedFrame frame,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        CancellationToken cancellationToken,
        PlaybackSourceBoundOneFrameDecodeBudget? budget)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            frame.Dispose();
            SetResult(PlaybackSourceBoundOneFrameDecodeResult.Cancelled);
            return Complete(PlaybackSourceBoundOneFrameDecodeResult.Cancelled);
        }

        if (frame.IsDisposed || frame.Metadata.MediaKind != MapFrameMediaKind(mediaKind) ||
            !TryGetManagedByteCount(frame, out var managedByteCount) ||
            managedByteCount > _options.MaximumManagedFrameBytes)
        {
            frame.Dispose();
            return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.FrameRejected);
        }

        var sequence = mediaKind == PlaybackBoundedDemuxMediaKind.Video
            ? _nextVideoSequence
            : _nextAudioSequence;
        if (sequence == long.MaxValue)
        {
            frame.Dispose();
            return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded);
        }

        PlaybackSourceBoundDecodedFrameCompletion completion;
        try
        {
            completion = new(
                this,
                _sourceSession,
                _decoderGeneration,
                _discontinuityGeneration,
                mediaKind,
                streamId,
                sequence,
                managedByteCount,
                mode,
                frame);
        }
        catch (OutOfMemoryException)
        {
            frame.Dispose();
            return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.NativeAllocationFailed);
        }

        if (budget is not null && !budget.TryConsume(managedByteCount))
        {
            completion.Dispose();
            return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded);
        }

        _activeCompletion = completion;
        if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
        {
            _nextVideoSequence = checked(sequence + 1);
        }
        else
        {
            _nextAudioSequence = checked(sequence + 1);
        }

        SetResult(PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable);
        return new(
            PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable,
            completion,
            PlaybackFiniteOneEventPacketSubmissionResult.NoActiveSubmission);
    }

    private PlaybackSourceBoundOneFrameDecodeOperation CompleteEndOfStream(
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief)
        {
            if (!_stagedSubmissionOperations.StageReceive(
                mediaKind,
                PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream))
            {
                return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.SubmissionRejected);
            }

            var advance = _submission!.Advance(cancellationToken);
            SynchronizeEndOfStream(_submission);
            if (advance == PlaybackFiniteOneEventPacketSubmissionResult.Cancelled)
            {
                SetResult(PlaybackSourceBoundOneFrameDecodeResult.Cancelled);
                return Complete(
                    PlaybackSourceBoundOneFrameDecodeResult.Cancelled,
                    advance);
            }

            if (advance != PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream)
            {
                return FailLocked(
                    PlaybackSourceBoundOneFrameDecodeResult.SubmissionRejected,
                    advance);
            }

            MarkEndOfStream(mediaKind);
            SetResult(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream);
            return Complete(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream, advance);
        }

        MarkEndOfStream(mediaKind);
        SetResult(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream);
        return Complete(PlaybackSourceBoundOneFrameDecodeResult.EndOfStream);
    }

    private PlaybackSourceBoundOneFrameDecodeOperation RetryPendingAfterReleasedFrame(
        CancellationToken cancellationToken)
    {
        _reliefRetryReady = false;
        if (_submission is null || !_submission.HasActiveSubmission ||
            !_stagedSubmissionOperations.StageReceive(
                _reliefMediaKind,
                PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable))
        {
            return FailLocked(PlaybackSourceBoundOneFrameDecodeResult.SubmissionRejected);
        }

        var advance = _submission.Advance(cancellationToken);
        SynchronizeEndOfStream(_submission);
        return advance switch
        {
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted =>
                CompleteAndSet(
                    PlaybackSourceBoundOneFrameDecodeResult.SubmissionAccepted,
                    advance),
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndStillPending =>
                CompleteAndSet(
                    PlaybackSourceBoundOneFrameDecodeResult.SubmissionStillPending,
                    advance),
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndEndOfStream or
            PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream =>
                CompleteAndSet(
                    PlaybackSourceBoundOneFrameDecodeResult.EndOfStream,
                    advance),
            PlaybackFiniteOneEventPacketSubmissionResult.Cancelled =>
                CompleteAndSet(
                    PlaybackSourceBoundOneFrameDecodeResult.Cancelled,
                    advance),
            _ => FailLocked(
                PlaybackSourceBoundOneFrameDecodeResult.SubmissionRejected,
                advance),
        };
    }

    private PlaybackSourceBoundOneFrameDecodeOperation CompleteAndSet(
        PlaybackSourceBoundOneFrameDecodeResult result,
        PlaybackFiniteOneEventPacketSubmissionResult submissionResult)
    {
        SetResult(result);
        return Complete(result, submissionResult);
    }

    private PlaybackSourceBoundOneFrameDecodeOperation FailLocked(
        PlaybackSourceBoundOneFrameDecodeResult result,
        PlaybackFiniteOneEventPacketSubmissionResult submissionResult =
            PlaybackFiniteOneEventPacketSubmissionResult.NativeFailure)
    {
        _state = PlaybackSourceBoundOneFrameDecodeState.Faulted;
        _result = result;
        DisposeOwnedLocked();
        return new(result, null, submissionResult);
    }

    private void DisposeOwnedLocked()
    {
        var completion = _activeCompletion;
        var submission = _submission;
        var nativeSession = _nativeSession;
        _activeCompletion = null;
        _submission = null;
        _nativeSession = null;
        _reliefRetryReady = false;

        try
        {
            completion?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal state.
        }

        try
        {
            submission?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal state.
        }

        try
        {
            nativeSession?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal state.
        }
    }

    private void SynchronizeEndOfStream(
        PlaybackFiniteOneEventPacketSubmissionStateMachine submission)
    {
        if (submission.VideoEndOfStream)
        {
            _videoEndOfStream = true;
        }

        if (submission.AudioEndOfStream)
        {
            _audioEndOfStream = true;
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

    private bool IsStreamEndOfStream(PlaybackBoundedDemuxMediaKind mediaKind) =>
        mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                _videoEndOfStream || _submission?.VideoEndOfStream == true,
            PlaybackBoundedDemuxMediaKind.Audio =>
                _audioEndOfStream || _submission?.AudioEndOfStream == true,
            _ => true,
        };

    private bool IsFlushAccepted(PlaybackBoundedDemuxMediaKind mediaKind) =>
        mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => _submission?.VideoFlushAccepted == true,
            PlaybackBoundedDemuxMediaKind.Audio => _submission?.AudioFlushAccepted == true,
            _ => false,
        };

    private void SetResult(PlaybackSourceBoundOneFrameDecodeResult result) => _result = result;

    private static PlaybackDecoderFfmpegFrameMediaKind MapFrameMediaKind(
        PlaybackBoundedDemuxMediaKind mediaKind) => mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackBoundedDemuxMediaKind.Audio =>
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
            _ => throw new InvalidOperationException("The media kind is undefined."),
        };

    private static bool TryGetManagedByteCount(
        PlaybackDecoderFfmpegManagedFrame frame,
        out long managedByteCount)
    {
        managedByteCount = 0;
        if (frame.Planes.Count < 1)
        {
            return false;
        }

        try
        {
            foreach (var plane in frame.Planes)
            {
                if (plane.Buffer.IsEmpty)
                {
                    return false;
                }

                managedByteCount = checked(managedByteCount + plane.Buffer.Length);
            }

            return managedByteCount > 0 &&
                managedByteCount <=
                    PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit;
        }
        catch (OverflowException)
        {
            managedByteCount = 0;
            return false;
        }
    }

    private static PlaybackSourceBoundOneFrameDecodeResult MapReceiveFailure(
        PlaybackDecoderFfmpegManagedFrameCopyResult result) => result switch
        {
            PlaybackDecoderFfmpegManagedFrameCopyResult.Unsupported =>
                PlaybackSourceBoundOneFrameDecodeResult.Unsupported,
            PlaybackDecoderFfmpegManagedFrameCopyResult.GateRejected =>
                PlaybackSourceBoundOneFrameDecodeResult.GateRejected,
            PlaybackDecoderFfmpegManagedFrameCopyResult.AllocationFailed =>
                PlaybackSourceBoundOneFrameDecodeResult.NativeAllocationFailed,
            PlaybackDecoderFfmpegManagedFrameCopyResult.InvalidMetadata or
            PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData or
            PlaybackDecoderFfmpegManagedFrameCopyResult.ConsumerRejected =>
                PlaybackSourceBoundOneFrameDecodeResult.FrameRejected,
            PlaybackDecoderFfmpegManagedFrameCopyResult.ContextUnavailable or
            PlaybackDecoderFfmpegManagedFrameCopyResult.ContextNotOpened =>
                PlaybackSourceBoundOneFrameDecodeResult.NativeRejected,
            _ => PlaybackSourceBoundOneFrameDecodeResult.NativeFailure,
        };

    private static bool IsValidSourceBinding(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedFlvNativeDecoderSession nativeSession) =>
        !sourceSession.IsDisposed &&
        !nativeSession.IsDisposed &&
        nativeSession.ReferencesSourceSession(sourceSession) &&
        sourceSession.ContainerKind == PlaybackBoundedDemuxContainerKind.Flv &&
        sourceSession.Video.StreamId == nativeSession.VideoStreamId &&
        sourceSession.Audio.StreamId == nativeSession.AudioStreamId &&
        sourceSession.Video.MediaKind == PlaybackBoundedDemuxMediaKind.Video &&
        sourceSession.Audio.MediaKind == PlaybackBoundedDemuxMediaKind.Audio &&
        sourceSession.Video.Codec == PlaybackBoundedDemuxCodec.H264 &&
        sourceSession.Audio.Codec == PlaybackBoundedDemuxCodec.Aac &&
        sourceSession.Video.TimeBase == nativeSession.VideoTimeBase &&
        sourceSession.Audio.TimeBase == nativeSession.AudioTimeBase;

    private static void ValidateOptions(PlaybackSourceBoundOneFrameDecodeOptions options)
    {
        if (options.MaximumManagedFrameBytes < 1 ||
            options.MaximumManagedFrameBytes >
                PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumManagedFrameBytes)} is outside the supported range.");
        }
    }

    private static PlaybackSourceBoundOneFrameDecodeOperation Complete(
        PlaybackSourceBoundOneFrameDecodeResult result,
        PlaybackFiniteOneEventPacketSubmissionResult submissionResult =
            PlaybackFiniteOneEventPacketSubmissionResult.NoActiveSubmission) =>
        new(result, null, submissionResult);

    private sealed class StagedSubmissionOperations :
        IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        private readonly IPlaybackFiniteOneEventPacketSubmissionOperations _inner;
        private bool _hasPending;
        private PlaybackBoundedDemuxMediaKind _pendingMediaKind;
        private bool _pendingIsFlush;
        private bool _hasStagedReceive;
        private PlaybackDecoderFfmpegFrameReceiveResult _stagedReceive;

        public StagedSubmissionOperations(
            IPlaybackFiniteOneEventPacketSubmissionOperations inner)
        {
            _inner = inner;
        }

        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion)
        {
            _hasPending = false;
            _hasStagedReceive = false;
            var completion = _inner.SubmitOne(nativeSession, sourceSession, sourceCompletion);
            if (completion.Result == PlaybackMetadataAwareNativePacketResult.NeedFrameDrain &&
                sourceCompletion.Events.Count == 1)
            {
                var demuxEvent = sourceCompletion.Events[0];
                _hasPending = true;
                _pendingMediaKind = demuxEvent.MediaKind;
                _pendingIsFlush =
                    demuxEvent.Kind == PlaybackBoundedDemuxEventKind.EndOfStream;
            }

            return completion;
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            if (!_hasPending || !_hasStagedReceive || mediaKind != _pendingMediaKind)
            {
                return PlaybackDecoderFfmpegFrameReceiveResult.NativeFailure;
            }

            _hasStagedReceive = false;
            return _stagedReceive;
        }

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush) =>
            _hasPending && mediaKind == _pendingMediaKind && isFlush == _pendingIsFlush
                ? _inner.RetryOne(nativeSession, mediaKind, isFlush)
                : PlaybackMetadataAwareNativePacketResult.NativeRejected;

        public void ReleasePending()
        {
            try
            {
                _inner.ReleasePending();
            }
            finally
            {
                _hasPending = false;
                _pendingMediaKind = default;
                _pendingIsFlush = false;
                _hasStagedReceive = false;
                _stagedReceive = default;
            }
        }

        public bool TryGetPending(
            out PlaybackBoundedDemuxMediaKind mediaKind,
            out bool isFlush)
        {
            mediaKind = _pendingMediaKind;
            isFlush = _pendingIsFlush;
            return _hasPending;
        }

        public bool StageReceive(
            PlaybackBoundedDemuxMediaKind mediaKind,
            PlaybackDecoderFfmpegFrameReceiveResult result)
        {
            if (!_hasPending || _hasStagedReceive || mediaKind != _pendingMediaKind ||
                result is not PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable and
                    not PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream)
            {
                return false;
            }

            _hasStagedReceive = true;
            _stagedReceive = result;
            return true;
        }
    }
}

internal sealed class PlaybackSourceBoundOneFrameDecodeOperations :
    IPlaybackSourceBoundOneFrameDecodeOperations
{
    private readonly PlaybackDecoderFfmpegManagedFrameCopyBoundary _copyBoundary = new();

    public bool ValidateContextRole(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        try
        {
            var context = PlaybackMetadataAwareNativePacketOperations.GetContext(
                nativeSession,
                mediaKind);
            var expectedCodec = mediaKind switch
            {
                PlaybackBoundedDemuxMediaKind.Video => PlaybackDecoderFfmpegCodecId.H264,
                PlaybackBoundedDemuxMediaKind.Audio => PlaybackDecoderFfmpegCodecId.Aac,
                _ => default,
            };
            return Enum.IsDefined(mediaKind) &&
                context.CodecId == expectedCodec &&
                !context.IsDisposed &&
                context.IsOpened;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    public PlaybackSourceBoundOneFrameNativeReceive ReceiveOne(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        var consumer = new CapturingConsumer();
        var context = PlaybackMetadataAwareNativePacketOperations.GetContext(
            nativeSession,
            mediaKind);
        var result = _copyBoundary.ReceiveAndCopy(context, consumer);
        if (result != PlaybackDecoderFfmpegManagedFrameCopyResult.Copied &&
            consumer.Frame is not null)
        {
            consumer.Frame.Dispose();
            return new(PlaybackDecoderFfmpegManagedFrameCopyResult.NativeFailure, null);
        }

        return new(result, consumer.Frame);
    }

    private sealed class CapturingConsumer : IPlaybackDecoderFfmpegManagedFrameConsumer
    {
        public PlaybackDecoderFfmpegManagedFrame? Frame { get; private set; }

        public bool Consume(PlaybackDecoderFfmpegManagedFrame frame)
        {
            if (Frame is not null)
            {
                return false;
            }

            Frame = frame;
            return true;
        }
    }
}

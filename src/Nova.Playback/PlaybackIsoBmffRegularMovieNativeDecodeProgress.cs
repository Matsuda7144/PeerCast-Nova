namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
{
    Created,
    MissingSubmission,
    SubmissionUnavailable,
    SourceMismatch,
}

public enum PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
{
    PacketSubmitted,
    PacketBackpressure,
    FrameCopied,
    PacketsExhausted,
    SourceTruncated,
    VideoFlushAccepted,
    AudioFlushAccepted,
    VideoFlushBackpressure,
    AudioFlushBackpressure,
    VideoEndOfStream,
    AudioEndOfStream,
    Completed,
    ActiveFrame,
    PacketRejected,
    ReceiveNoProgress,
    ReceiveRejected,
    FrameLimitExceeded,
    FlushRejected,
    Faulted,
    Disposed,
}

internal readonly record struct
    PlaybackIsoBmffRegularMovieNativeDecodeReceive(
        PlaybackDecoderFfmpegManagedFrameCopyResult Result,
        PlaybackDecoderFfmpegManagedFrame? Frame);

internal interface IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations
{
    PlaybackMetadataAwareNativeSendResult SendFlush(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role);

    PlaybackIsoBmffRegularMovieNativeDecodeReceive ReceiveOne(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role);
}

internal sealed class PlaybackIsoBmffRegularMovieNativeDecodeProgressOperations
    : IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations
{
    private readonly PlaybackDecoderFfmpegManagedFrameCopyBoundary
        _copyBoundary = new();

    public PlaybackMetadataAwareNativeSendResult SendFlush(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        var context = GetContext(session, role);
        return context is null
            ? PlaybackMetadataAwareNativeSendResult.Rejected
            : PlaybackMetadataAwareNativePacketOperations.SendFlush(context);
    }

    public PlaybackIsoBmffRegularMovieNativeDecodeReceive ReceiveOne(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        var context = GetContext(session, role);
        if (context is null)
        {
            return new(
                PlaybackDecoderFfmpegManagedFrameCopyResult
                    .ContextUnavailable,
                null);
        }

        var consumer = new CapturingConsumer();
        var result = _copyBoundary.ReceiveAndCopy(context, consumer);
        if (result != PlaybackDecoderFfmpegManagedFrameCopyResult.Copied &&
            consumer.Frame is not null)
        {
            consumer.Frame.Dispose();
            return new(
                PlaybackDecoderFfmpegManagedFrameCopyResult.NativeFailure,
                null);
        }

        return new(result, consumer.Frame);
    }

    private static PlaybackDecoderFfmpegContextLease? GetContext(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role) =>
        role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                session.VideoContext,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                session.AudioContext,
            _ => null,
        };

    private sealed class CapturingConsumer :
        IPlaybackDecoderFfmpegManagedFrameConsumer
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

public sealed class
    PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion :
    IDisposable,
    IAsyncDisposable
{
    private PlaybackIsoBmffRegularMovieNativeDecodeProgress? _source;
    private PlaybackDecoderFfmpegManagedFrame? _frame;

    internal PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion(
        PlaybackIsoBmffRegularMovieNativeDecodeProgress source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        long sequence,
        PlaybackDecoderFfmpegManagedFrame frame)
    {
        _source = source;
        _frame = frame;
        Role = role;
        Sequence = sequence;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role
    { get; }

    public long Sequence { get; }

    public PlaybackDecoderFfmpegManagedFrame Frame =>
        Volatile.Read(ref _frame) ??
        throw new ObjectDisposedException(
            nameof(
                PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion));

    public bool IsDisposed => Volatile.Read(ref _frame) is null;

    internal bool TryDetachFrame(
        out PlaybackDecoderFfmpegManagedFrame? frame)
    {
        frame = Interlocked.Exchange(ref _frame, null);
        return frame is not null;
    }

    public void Dispose()
    {
        var frame = Interlocked.Exchange(ref _frame, null);
        var source = Interlocked.Exchange(ref _source, null);
        try
        {
            frame?.Dispose();
        }
        finally
        {
            source?.Release(this);
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance(
    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult Result,
    PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot? Packet,
    PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion? Frame);

public sealed class PlaybackIsoBmffRegularMovieNativeDecodeProgress :
    IDisposable,
    IAsyncDisposable
{
    public const int MaximumFrameCount =
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
            .MaximumMergeStepCount;

    private readonly object _sync = new();
    private readonly IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations
        _operations;
    private PlaybackIsoBmffRegularMovieNativePacketSubmission? _submission;
    private PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion?
        _activeFrame;
    private bool _retryPendingPacket;
    private bool _packetsExhausted;
    private bool _flushPending;
    private bool _retryFlush;
    private bool _draining;
    private int _flushRoleIndex;
    private long _nextFrameSequence;
    private bool _completed;
    private bool _truncated;
    private bool _faulted;

    internal PlaybackIsoBmffRegularMovieNativeDecodeProgress(
        PlaybackIsoBmffRegularMovieNativePacketSubmission submission,
        IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations operations)
    {
        _submission = submission;
        _operations = operations;
    }

    public bool IsDisposed => Volatile.Read(ref _submission) is null;

    public bool HasActiveFrame
    {
        get
        {
            lock (_sync)
            {
                return _activeFrame is not null;
            }
        }
    }

    public long NextFrameSequence
    {
        get
        {
            lock (_sync)
            {
                return _nextFrameSequence;
            }
        }
    }

    internal PlaybackIsoBmffRegularMovieNativeDecoderSession SourceSession
    {
        get
        {
            lock (_sync)
            {
                return _submission?.SourceSession ??
                    throw new ObjectDisposedException(
                        nameof(
                            PlaybackIsoBmffRegularMovieNativeDecodeProgress));
            }
        }
    }

    public PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance Advance()
    {
        lock (_sync)
        {
            if (_faulted)
            {
                return Complete(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .Faulted);
            }

            var submission = _submission;
            if (submission is null)
            {
                return Complete(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .Disposed);
            }

            if (_activeFrame is not null)
            {
                return new(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .ActiveFrame,
                    null,
                    _activeFrame);
            }

            if (_completed)
            {
                return Complete(
                    _truncated
                        ? PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                            .SourceTruncated
                        : PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                            .Completed);
            }

            try
            {
                if (submission.HasPendingPacket)
                {
                    return _retryPendingPacket
                        ? RetryPacket(submission)
                        : ReceiveForPendingPacket(submission);
                }

                if (!_packetsExhausted)
                {
                    return SubmitPacket(submission);
                }

                return AdvanceFlush(submission);
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or NotSupportedException or
                OverflowException or ObjectDisposedException)
            {
                return Fail(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .ReceiveRejected);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            var frame = _activeFrame;
            _activeFrame = null;
            frame?.Dispose();
            Interlocked.Exchange(ref _submission, null)?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void Release(
        PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion completion)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeFrame, completion))
            {
                _activeFrame = null;
            }
        }
    }

    internal bool Owns(
        PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion completion)
    {
        lock (_sync)
        {
            return ReferenceEquals(_activeFrame, completion);
        }
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance
        SubmitPacket(
            PlaybackIsoBmffRegularMovieNativePacketSubmission submission)
    {
        var advance = submission.SubmitNext();
        return advance.Result switch
        {
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Submitted =>
                new(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .PacketSubmitted,
                    advance.Snapshot,
                    null),
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .NeedFrameDrain =>
                new(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .PacketBackpressure,
                    advance.Snapshot,
                    null),
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Exhausted =>
                MarkPacketsExhausted(),
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Truncated =>
                MarkTruncated(),
            _ => Fail(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .PacketRejected,
                advance.Snapshot),
        };
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance RetryPacket(
        PlaybackIsoBmffRegularMovieNativePacketSubmission submission)
    {
        var advance = submission.SubmitNext();
        _retryPendingPacket = false;
        return advance.Result switch
        {
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Submitted =>
                new(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .PacketSubmitted,
                    advance.Snapshot,
                    null),
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .NeedFrameDrain =>
                new(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .PacketBackpressure,
                    advance.Snapshot,
                    null),
            _ => Fail(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .PacketRejected,
                advance.Snapshot),
        };
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance
        ReceiveForPendingPacket(
            PlaybackIsoBmffRegularMovieNativePacketSubmission submission)
    {
        var snapshot = submission.PendingSnapshot;
        if (snapshot is null)
        {
            return Fail(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .PacketRejected);
        }

        var receive = _operations.ReceiveOne(
            submission.SourceSession,
            snapshot.Role);
        if (receive.Result ==
                PlaybackDecoderFfmpegManagedFrameCopyResult.Copied &&
            receive.Frame is not null)
        {
            _retryPendingPacket = true;
            return CaptureFrame(snapshot.Role, receive.Frame);
        }

        receive.Frame?.Dispose();
        return receive.Result ==
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput
                ? Fail(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .ReceiveNoProgress)
                : Fail(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .ReceiveRejected);
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance
        AdvanceFlush(
            PlaybackIsoBmffRegularMovieNativePacketSubmission submission)
    {
        if (!TryGetFlushRole(submission.SourceSession, out var role))
        {
            _completed = true;
            return Complete(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .Completed);
        }

        if (_flushPending)
        {
            return _retryFlush
                ? SendFlush(submission, role)
                : ReceiveFlush(submission, role);
        }

        if (_draining)
        {
            return ReceiveFlush(submission, role);
        }

        return SendFlush(submission, role);
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance SendFlush(
        PlaybackIsoBmffRegularMovieNativePacketSubmission submission,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        var result = _operations.SendFlush(submission.SourceSession, role);
        _retryFlush = false;
        if (result == PlaybackMetadataAwareNativeSendResult.Success)
        {
            _flushPending = false;
            _draining = true;
            return Complete(FlushAccepted(role));
        }

        if (result == PlaybackMetadataAwareNativeSendResult.Again)
        {
            _flushPending = true;
            _draining = false;
            return Complete(FlushBackpressure(role));
        }

        if (result == PlaybackMetadataAwareNativeSendResult.EndOfStream)
        {
            CompleteFlushRole();
            return Complete(EndOfStream(role));
        }

        return Fail(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .FlushRejected);
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance ReceiveFlush(
        PlaybackIsoBmffRegularMovieNativePacketSubmission submission,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        var receive = _operations.ReceiveOne(submission.SourceSession, role);
        if (receive.Result ==
                PlaybackDecoderFfmpegManagedFrameCopyResult.Copied &&
            receive.Frame is not null)
        {
            _retryFlush = _flushPending;
            return CaptureFrame(role, receive.Frame);
        }

        receive.Frame?.Dispose();
        if (receive.Result ==
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream)
        {
            CompleteFlushRole();
            return Complete(EndOfStream(role));
        }

        return receive.Result ==
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput
                ? Fail(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .ReceiveNoProgress)
                : Fail(
                    PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                        .ReceiveRejected);
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance CaptureFrame(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        PlaybackDecoderFfmpegManagedFrame frame)
    {
        if (_nextFrameSequence >= MaximumFrameCount)
        {
            frame.Dispose();
            return Fail(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .FrameLimitExceeded);
        }

        var expectedKind = role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
                ? PlaybackDecoderFfmpegFrameMediaKind.Video
                : PlaybackDecoderFfmpegFrameMediaKind.Audio;
        if (frame.IsDisposed || frame.Metadata.MediaKind != expectedKind)
        {
            frame.Dispose();
            return Fail(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .ReceiveRejected);
        }

        var completion = new
            PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion(
                this,
                role,
                _nextFrameSequence,
                frame);
        _nextFrameSequence++;
        _activeFrame = completion;
        return new(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult.FrameCopied,
            null,
            completion);
    }

    private bool TryGetFlushRole(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        out PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        role = default;
        if (_flushRoleIndex == 0 && session.HasVideo)
        {
            role = PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                .Video;
            return true;
        }

        var audioIndex = session.HasVideo ? 1 : 0;
        if (_flushRoleIndex == audioIndex && session.HasAudio)
        {
            role = PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                .Audio;
            return true;
        }

        return false;
    }

    private void CompleteFlushRole()
    {
        _flushPending = false;
        _retryFlush = false;
        _draining = false;
        _flushRoleIndex++;
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance
        MarkPacketsExhausted()
    {
        _packetsExhausted = true;
        return Complete(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketsExhausted);
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance
        MarkTruncated()
    {
        _completed = true;
        _truncated = true;
        return Complete(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .SourceTruncated);
    }

    private PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance Fail(
        PlaybackIsoBmffRegularMovieNativeDecodeProgressResult result,
        PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot? packet =
            null)
    {
        _faulted = true;
        _activeFrame?.Dispose();
        _activeFrame = null;
        Interlocked.Exchange(ref _submission, null)?.Dispose();
        return new(result, packet, null);
    }

    private static PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
        FlushAccepted(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role) =>
        role == PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
            ? PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .VideoFlushAccepted
            : PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .AudioFlushAccepted;

    private static PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
        FlushBackpressure(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role) =>
        role == PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
            ? PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .VideoFlushBackpressure
            : PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .AudioFlushBackpressure;

    private static PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
        EndOfStream(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role) =>
        role == PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
            ? PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .VideoEndOfStream
            : PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .AudioEndOfStream;

    private static PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance
        Complete(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult result) =>
        new(result, null, null);
}

public sealed record PlaybackIsoBmffRegularMovieNativeDecodeProgressCreation(
    PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult Result,
    PlaybackIsoBmffRegularMovieNativeDecodeProgress? Progress);

public sealed class PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory
{
    private readonly IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations
        _operations;

    public PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory()
        : this(new PlaybackIsoBmffRegularMovieNativeDecodeProgressOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory(
        IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieNativeDecodeProgressCreation Create(
        PlaybackIsoBmffRegularMovieNativePacketSubmission? submission)
    {
        if (submission is null)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
                    .MissingSubmission,
                null);
        }

        if (submission.IsDisposed)
        {
            submission.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
                    .SubmissionUnavailable,
                null);
        }

        PlaybackIsoBmffRegularMovieNativeDecoderSession session;
        try
        {
            session = submission.SourceSession;
        }
        catch (ObjectDisposedException)
        {
            submission.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
                    .SubmissionUnavailable,
                null);
        }

        var source = session.Source.Source.Source;
        if (session.IsDisposed ||
            !ReferenceEquals(source.MergeTraversal.Source, source.CursorSet) ||
            !ReferenceEquals(source.CursorSet.Source, source.Selection) ||
            !ReferenceEquals(source.Selection.Source, source.SequenceSet))
        {
            submission.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
                    .SourceMismatch,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
                .Created,
            new PlaybackIsoBmffRegularMovieNativeDecodeProgress(
                submission,
                _operations));
    }
}

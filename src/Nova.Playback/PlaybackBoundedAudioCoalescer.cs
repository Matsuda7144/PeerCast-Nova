namespace PeerCast.Nova.Playback;

public enum PlaybackBoundedAudioCoalescerResult
{
    Accepting,
    BatchReady,
    BatchPending,
    InvalidFrame,
    SequenceRejected,
    GenerationRejected,
    BudgetExceeded,
    Cancelled,
    Disposed,
}

public sealed record PlaybackBoundedAudioCoalescerOptions
{
    public const int DefaultTargetSampleFrames = 3_840;
    public const int MaximumTargetSampleFrames = 48_000;

    public long Generation { get; init; } = 1;

    public int TargetSampleFrames { get; init; } = DefaultTargetSampleFrames;

    public long MaximumOwnedBytes { get; init; } =
        PlaybackSourceBoundOneFrameDecodeOptions.DefaultMaximumManagedFrameBytes;
}

public readonly record struct PlaybackBoundedAudioCoalescerSnapshot(
    PlaybackBoundedAudioCoalescerResult Result,
    long Generation,
    long NextSequence,
    int TargetSampleFrames,
    int BufferedSampleFrames,
    int RetainedRemainderSampleFrames,
    long AcceptedFrameCount,
    long PublishedBatchCount,
    long OwnedByteCount,
    bool HasReadyBatch,
    bool IsDisposed);

/// <summary>
/// Generation-scoped bounded coalescer for canonical decoded audio. Source bytes are copied
/// immediately, one complete batch is published at a time, and a final-frame remainder is
/// retained for the next batch without repeating or dropping samples.
/// </summary>
public sealed class PlaybackBoundedAudioCoalescer : IDisposable
{
    public const PlaybackDecodedAudioSampleFormat SampleFormat =
        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved;
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BytesPerSampleFrame = Channels * sizeof(float);

    private readonly object _sync = new();
    private readonly PlaybackBoundedAudioCoalescerOptions _options;
    private byte[]? _payload;
    private byte[]? _retainedRemainder;
    private PlaybackBoundedAudioCoalescerResult _result =
        PlaybackBoundedAudioCoalescerResult.Accepting;
    private long _nextSequence;
    private int _bufferedSampleFrames;
    private int _retainedRemainderSampleFrames;
    private long _acceptedFrameCount;
    private long _publishedBatchCount;
    private bool _hasReadyBatch;
    private bool _isDisposed;

    public PlaybackBoundedAudioCoalescer(
        PlaybackBoundedAudioCoalescerOptions? options = null)
    {
        _options = options is null
            ? new PlaybackBoundedAudioCoalescerOptions()
            : options with { };
        if (_options.Generation < 0 ||
            _options.TargetSampleFrames is < 1 or
                > PlaybackBoundedAudioCoalescerOptions.MaximumTargetSampleFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var payloadBytes = checked(
            _options.TargetSampleFrames * BytesPerSampleFrame);
        if (_options.MaximumOwnedBytes < payloadBytes ||
            _options.MaximumOwnedBytes >
                PlaybackSourceBoundOneFrameDecodeOptions
                    .MaximumManagedFrameBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _payload = new byte[payloadBytes];
    }

    public PlaybackBoundedAudioCoalescerSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _result,
                    _options.Generation,
                    _nextSequence,
                    _options.TargetSampleFrames,
                    _bufferedSampleFrames,
                    _retainedRemainderSampleFrames,
                    _acceptedFrameCount,
                    _publishedBatchCount,
                    OwnedByteCount(),
                    _hasReadyBatch,
                    _isDisposed);
            }
        }
    }

    public PlaybackBoundedAudioCoalescerResult Append(
        long sequence,
        long generation,
        PlaybackDecodedAudioSampleFormat sampleFormat,
        int sampleRate,
        int channels,
        int sampleFrames,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.Disposed);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.Cancelled);
            }

            if (_hasReadyBatch)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.BatchPending);
            }

            if (generation != _options.Generation)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.GenerationRejected);
            }

            if (sequence != _nextSequence || sequence < 0 ||
                _nextSequence == long.MaxValue)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.SequenceRejected);
            }

            int sourceBytes;
            try
            {
                sourceBytes = checked(sampleFrames * BytesPerSampleFrame);
            }
            catch (OverflowException)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.InvalidFrame);
            }

            if (sampleFormat != SampleFormat || sampleRate != SampleRate ||
                channels != Channels || sampleFrames <= 0 ||
                sampleFrames > _options.TargetSampleFrames ||
                payload.Length != sourceBytes || _payload is null)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.InvalidFrame);
            }

            var remainingSampleFrames =
                _options.TargetSampleFrames - _bufferedSampleFrames;
            var copiedSampleFrames = Math.Min(sampleFrames, remainingSampleFrames);
            var remainderSampleFrames = sampleFrames - copiedSampleFrames;
            int copiedBytes;
            int destinationOffset;
            int remainderBytes;
            try
            {
                copiedBytes = checked(copiedSampleFrames * BytesPerSampleFrame);
                destinationOffset = checked(
                    _bufferedSampleFrames * BytesPerSampleFrame);
                remainderBytes = checked(
                    remainderSampleFrames * BytesPerSampleFrame);
            }
            catch (OverflowException)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.BudgetExceeded);
            }

            var ownedAfterAppend = checked(
                (long)_payload.Length + remainderBytes);
            if (destinationOffset < 0 || copiedBytes < 1 ||
                destinationOffset > _payload.Length - copiedBytes ||
                ownedAfterAppend > _options.MaximumOwnedBytes)
            {
                return SetResult(PlaybackBoundedAudioCoalescerResult.BudgetExceeded);
            }

            byte[]? remainder = null;
            if (remainderBytes > 0)
            {
                remainder = new byte[remainderBytes];
                payload.Span.Slice(copiedBytes, remainderBytes).CopyTo(remainder);
            }

            payload.Span[..copiedBytes].CopyTo(
                _payload.AsSpan(destinationOffset, copiedBytes));
            _retainedRemainder = remainder;
            _retainedRemainderSampleFrames = remainderSampleFrames;
            _bufferedSampleFrames = checked(
                _bufferedSampleFrames + copiedSampleFrames);
            _acceptedFrameCount = checked(_acceptedFrameCount + 1);
            _nextSequence = checked(_nextSequence + 1);
            _hasReadyBatch =
                _bufferedSampleFrames == _options.TargetSampleFrames;
            return SetResult(_hasReadyBatch
                ? PlaybackBoundedAudioCoalescerResult.BatchReady
                : PlaybackBoundedAudioCoalescerResult.Accepting);
        }
    }

    public bool TryTakeReadyBuffer(
        long outputGeneration,
        out PlaybackWindowsAudioSampleBuffer buffer)
    {
        lock (_sync)
        {
            if (_isDisposed || !_hasReadyBatch || outputGeneration < 0 ||
                _payload is null)
            {
                buffer = default;
                return false;
            }

            var publishedPayload = _payload;
            var nextPayload = new byte[publishedPayload.Length];
            if (_retainedRemainder is not null)
            {
                _retainedRemainder.CopyTo(nextPayload, 0);
            }

            _payload = nextPayload;
            _bufferedSampleFrames = _retainedRemainderSampleFrames;
            _retainedRemainder = null;
            _retainedRemainderSampleFrames = 0;
            _hasReadyBatch = false;
            _publishedBatchCount = checked(_publishedBatchCount + 1);
            _result = PlaybackBoundedAudioCoalescerResult.Accepting;
            buffer = new(
                SampleFormat,
                SampleRate,
                Channels,
                _options.TargetSampleFrames,
                publishedPayload,
                outputGeneration);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _payload = null;
            _retainedRemainder = null;
            _bufferedSampleFrames = 0;
            _retainedRemainderSampleFrames = 0;
            _hasReadyBatch = false;
            _result = PlaybackBoundedAudioCoalescerResult.Disposed;
        }

        GC.SuppressFinalize(this);
    }

    private long OwnedByteCount() =>
        (_payload?.LongLength ?? 0) +
        (_retainedRemainder?.LongLength ?? 0);

    private PlaybackBoundedAudioCoalescerResult SetResult(
        PlaybackBoundedAudioCoalescerResult result)
    {
        _result = result;
        return result;
    }
}

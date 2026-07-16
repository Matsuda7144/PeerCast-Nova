using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal sealed class PlaybackLifecycleProbeDecoder : IPlaybackDecoder
{
    private readonly Action<long> _inputObserved;
    private long _inputCount;
    private long _byteCount;
    private long _resetCount;
    private int _completeCount;
    private int _disposeCount;
    private int _disposed;

    public PlaybackLifecycleProbeDecoder(Action<long> inputObserved)
    {
        ArgumentNullException.ThrowIfNull(inputObserved);
        _inputObserved = inputObserved;
    }

    public long InputCount => Volatile.Read(ref _inputCount);

    public long ByteCount => Volatile.Read(ref _byteCount);

    public long ResetCount => Volatile.Read(ref _resetCount);

    public int CompleteCount => Volatile.Read(ref _completeCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public ValueTask DecodeAsync(
        PlaybackEncodedInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        Interlocked.Add(ref _byteCount, input.Payload.Length);
        var count = Interlocked.Increment(ref _inputCount);
        _inputObserved(count);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        Interlocked.Increment(ref _resetCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        Interlocked.Increment(ref _completeCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Interlocked.Increment(ref _disposeCount);
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }
}

internal sealed class PlaybackLifecycleProbeDecoderFactory :
    IPlaybackDecoderBackend
{
    private readonly Action<long> _inputObserved;
    private PlaybackLifecycleProbeDecoder? _lastCreated;
    private int _createCount;

    public PlaybackLifecycleProbeDecoderFactory(
        Action<long> inputObserved)
    {
        ArgumentNullException.ThrowIfNull(inputObserved);
        _inputObserved = inputObserved;
    }

    public int CreateCount => Volatile.Read(ref _createCount);

    public PlaybackDecoderBackendDescriptor Descriptor { get; } = new(
        "nova.lifecycle-probe",
        0,
        [
            PlaybackMediaContainer.FlashVideo,
            PlaybackMediaContainer.Ogg,
            PlaybackMediaContainer.MpegTransportStream,
            PlaybackMediaContainer.AdvancedSystemsFormat,
            PlaybackMediaContainer.Matroska,
            PlaybackMediaContainer.WebM,
        ],
        PlaybackDecoderBackendProvenance.FirstPartyManaged);

    public PlaybackLifecycleProbeDecoder? LastCreated =>
        Volatile.Read(ref _lastCreated);

    public IPlaybackDecoder Create(
        PlaybackDecoderCreationContext context)
    {
        Interlocked.Increment(ref _createCount);
        var decoder = new PlaybackLifecycleProbeDecoder(
            _inputObserved);
        Volatile.Write(ref _lastCreated, decoder);
        return decoder;
    }
}

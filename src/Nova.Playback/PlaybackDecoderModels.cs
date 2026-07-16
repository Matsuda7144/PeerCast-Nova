namespace PeerCast.Nova.Playback;

public enum PlaybackEncodedInputKind
{
    Bootstrap,
    Packet,
}

public enum PlaybackDecoderRunEndReason
{
    SourceCompleted,
    MediaUnrecognized,
    DecoderUnsupported,
    OwnerStopped,
}

/// <summary>
/// Fixed container evidence supplied to a synchronous decoder factory.
/// </summary>
public readonly record struct PlaybackDecoderCreationContext
{
    internal PlaybackDecoderCreationContext(
        PlaybackMediaContainer container,
        PlaybackMediaClassificationBasis basis,
        IPlaybackDecodedFrameSink outputSink)
    {
        ArgumentNullException.ThrowIfNull(outputSink);
        Container = container;
        Basis = basis;
        OutputSink = outputSink;
    }

    public PlaybackMediaContainer Container { get; }

    public PlaybackMediaClassificationBasis Basis { get; }

    public IPlaybackDecodedFrameSink OutputSink { get; }
}

/// <summary>
/// Encoded bytes whose payload is valid only until the decoder callback
/// completes. A decoder must copy any bytes it needs after the returned
/// ValueTask has completed.
/// </summary>
public readonly record struct PlaybackEncodedInput
{
    internal PlaybackEncodedInput(
        PlaybackEncodedInputKind kind,
        long sequence,
        long? streamPosition,
        PlaybackDiscontinuityKind discontinuity,
        ReadOnlyMemory<byte> payload)
    {
        Kind = kind;
        Sequence = sequence;
        StreamPosition = streamPosition;
        Discontinuity = discontinuity;
        Payload = payload;
    }

    public PlaybackEncodedInputKind Kind { get; }

    public long Sequence { get; }

    public long? StreamPosition { get; }

    public PlaybackDiscontinuityKind Discontinuity { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}

/// <summary>
/// Content-free terminal result for one decoder-runner lifetime.
/// Submitted counts include callbacks that were still active when a finite
/// owner stop returned with deferred cleanup.
/// </summary>
public sealed record PlaybackDecoderRunResult
{
    internal PlaybackDecoderRunResult(
        PlaybackDecoderRunEndReason endReason,
        PlaybackMediaClassificationState classificationState,
        PlaybackMediaContainer container,
        PlaybackMediaClassificationBasis? basis,
        long submittedInputCount,
        long submittedByteCount,
        long resetCount,
        bool cleanupDeferred)
    {
        EndReason = endReason;
        ClassificationState = classificationState;
        Container = container;
        Basis = basis;
        SubmittedInputCount = submittedInputCount;
        SubmittedByteCount = submittedByteCount;
        ResetCount = resetCount;
        CleanupDeferred = cleanupDeferred;
    }

    public PlaybackDecoderRunEndReason EndReason { get; }

    public PlaybackMediaClassificationState ClassificationState { get; }

    public PlaybackMediaContainer Container { get; }

    public PlaybackMediaClassificationBasis? Basis { get; }

    public long SubmittedInputCount { get; }

    public long SubmittedByteCount { get; }

    public long ResetCount { get; }

    public bool CleanupDeferred { get; }
}

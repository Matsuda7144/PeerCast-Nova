namespace PeerCast.Nova.Playback;

public enum PlaybackMediaContainer
{
    Unknown,
    FlashVideo,
    Ogg,
    MpegTransportStream,
    AdvancedSystemsFormat,
    Matroska,
    WebM,
    FragmentedMp4,
}

public enum PlaybackMediaClassificationState
{
    Collecting,
    Recognized,
    LimitReached,
    Ended,
}

public enum PlaybackMediaClassificationBasis
{
    FileHeader,
    CapturePattern,
    RepeatedPacketSync,
    EbmlDocumentType,
    Fmp4InitializationProfile,
}

public enum PlaybackMediaClassificationLimit
{
    PrefixBytes,
    Discontinuities,
}

/// <summary>
/// Content-free result of bounded media-container classification.
/// </summary>
public sealed record PlaybackMediaClassificationSnapshot
{
    internal PlaybackMediaClassificationSnapshot(
        PlaybackMediaClassificationState state,
        PlaybackMediaContainer container,
        PlaybackMediaClassificationBasis? basis,
        PlaybackMediaClassificationLimit? limit,
        long observedPacketCount,
        long examinedByteCount,
        int discontinuityCount,
        int retainedPrefixByteCount)
    {
        State = state;
        Container = container;
        Basis = basis;
        Limit = limit;
        ObservedPacketCount = observedPacketCount;
        ExaminedByteCount = examinedByteCount;
        DiscontinuityCount = discontinuityCount;
        RetainedPrefixByteCount = retainedPrefixByteCount;
    }

    public PlaybackMediaClassificationState State { get; }

    public PlaybackMediaContainer Container { get; }

    public PlaybackMediaClassificationBasis? Basis { get; }

    public PlaybackMediaClassificationLimit? Limit { get; }

    public long ObservedPacketCount { get; }

    public long ExaminedByteCount { get; }

    public int DiscontinuityCount { get; }

    public int RetainedPrefixByteCount { get; }
}

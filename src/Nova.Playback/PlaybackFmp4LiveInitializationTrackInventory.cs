using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
{
    Projected,
    InvalidConfiguration,
    InvalidInitializationEnvelope,
    InvalidMovieBox,
    TrackLimitExceeded,
}

public sealed record PlaybackFmp4LiveInitializationTrackInventoryOptions
{
    public const int DefaultMaximumTrackCount = 16;
    public const int MaximumTrackCountHardLimit = 256;

    public int MaximumTrackCount { get; init; } = DefaultMaximumTrackCount;
}

public sealed class PlaybackFmp4LiveInitializationTrackInventory
{
    internal PlaybackFmp4LiveInitializationTrackInventory(
        PlaybackFmp4LiveSegmentEnvelope envelope,
        ReadOnlyCollection<PlaybackIsoBmffBoxLocation> tracks)
    {
        Envelope = envelope;
        Tracks = tracks;
    }

    public PlaybackFmp4LiveSegmentEnvelope Envelope { get; }

    public IReadOnlyList<PlaybackIsoBmffBoxLocation> Tracks { get; }
}

public static class PlaybackFmp4LiveInitializationTrackInventoryProjector
{
    private const uint MovieBoxType = 0x6d6f6f76;
    private const uint TrackBoxType = 0x7472616b;

    public static PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
        Project(
            ReadOnlySpan<byte> initializationSegment,
            int maxSegmentBytes,
            out PlaybackFmp4LiveInitializationTrackInventory? inventory,
            PlaybackFmp4LiveInitializationTrackInventoryOptions? options = null)
    {
        inventory = null;
        options ??= new();
        if (options.MaximumTrackCount is < 1 or
            > PlaybackFmp4LiveInitializationTrackInventoryOptions
                .MaximumTrackCountHardLimit)
        {
            return PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                .InvalidConfiguration;
        }

        var envelopeResult = PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
            initializationSegment,
            maxSegmentBytes,
            out var envelope);
        if (envelopeResult !=
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted ||
            envelope is null ||
            envelope.Kind != PlaybackFmp4LiveSegmentKind.Initialization)
        {
            return PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                .InvalidInitializationEnvelope;
        }

        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                initializationSegment,
                envelope.FirstBoxLength,
                out var movie) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            movie is null ||
            movie.Header.Type != MovieBoxType ||
            movie.BoxLength != envelope.SecondBoxLength)
        {
            return PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                .InvalidMovieBox;
        }

        var moviePayload = initializationSegment.Slice(
            movie.PayloadOffset,
            movie.PayloadLength);
        var tracks = new List<PlaybackIsoBmffBoxLocation>();
        var childOffset = 0;
        while (childOffset != moviePayload.Length)
        {
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    moviePayload,
                    childOffset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                    .InvalidMovieBox;
            }

            if (child.Header.Type == TrackBoxType)
            {
                if (tracks.Count >= options.MaximumTrackCount)
                {
                    return PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                        .TrackLimitExceeded;
                }

                tracks.Add(new(
                    child.Header,
                    movie.PayloadOffset + child.Offset,
                    child.BoxLength));
            }

            childOffset = child.NextOffset;
        }

        inventory = new(envelope, tracks.AsReadOnly());
        return PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
            .Projected;
    }
}

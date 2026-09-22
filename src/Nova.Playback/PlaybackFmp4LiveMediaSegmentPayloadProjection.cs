using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
{
    Projected,
    MissingInitializationDefaults,
    MissingFragmentBinding,
    InvalidMediaEnvelope,
    InvalidMovieFragmentBox,
    InvalidMediaDataBox,
    InvalidResolvedRunProjection,
    InvalidRunDataOrigin,
    UnsupportedDataOrigin,
    InvalidSampleByteRangeProjection,
    ZeroSampleSize,
    SampleRangeOverflow,
    SampleOutsideMediaData,
    OverlappingSampleRanges,
}

public sealed class PlaybackFmp4LiveMediaSegmentTrackPayloadProjection
{
    internal PlaybackFmp4LiveMediaSegmentTrackPayloadProjection(
        PlaybackFmp4LiveMediaFragmentResolvedTrackRun source,
        PlaybackIsoBmffFragmentRunDataOrigin dataOrigin,
        PlaybackIsoBmffFragmentSampleByteRangeProjection sampleByteRanges)
    {
        Source = source;
        DataOrigin = dataOrigin;
        SampleByteRanges = sampleByteRanges;
    }

    public PlaybackFmp4LiveMediaFragmentResolvedTrackRun Source { get; }

    public PlaybackIsoBmffFragmentRunDataOrigin DataOrigin { get; }

    public PlaybackIsoBmffFragmentSampleByteRangeProjection SampleByteRanges
    { get; }

    public uint TrackId => Source.TrackId;
}

public sealed class PlaybackFmp4LiveMediaSegmentPayloadProjection
{
    internal PlaybackFmp4LiveMediaSegmentPayloadProjection(
        PlaybackFmp4LiveSegmentEnvelope envelope,
        PlaybackIsoBmffBoxLocation mediaDataBox,
        PlaybackFmp4LiveMediaFragmentResolvedRunProjection resolvedRuns,
        ReadOnlyCollection<PlaybackFmp4LiveMediaSegmentTrackPayloadProjection>
            tracks)
    {
        Envelope = envelope;
        MediaDataBox = mediaDataBox;
        ResolvedRuns = resolvedRuns;
        Tracks = tracks;
    }

    public PlaybackFmp4LiveSegmentEnvelope Envelope { get; }

    public PlaybackIsoBmffBoxLocation MediaDataBox { get; }

    public PlaybackFmp4LiveMediaFragmentResolvedRunProjection ResolvedRuns
    { get; }

    public IReadOnlyList<PlaybackFmp4LiveMediaSegmentTrackPayloadProjection>
        Tracks
    { get; }

    public uint SequenceNumber => ResolvedRuns.SequenceNumber;
}

public static class PlaybackFmp4LiveMediaSegmentPayloadProjector
{
    private const uint MovieFragmentBoxType = 0x6d6f6f66;
    private const uint MediaDataBoxType = 0x6d646174;

    public static PlaybackFmp4LiveMediaSegmentPayloadProjectionResult Project(
        ReadOnlySpan<byte> mediaSegment,
        int maxSegmentBytes,
        PlaybackFmp4LiveInitializationFragmentDefaultsProjection?
            initializationDefaults,
        PlaybackFmp4LiveMediaFragmentProfileBinding? fragmentBinding,
        out PlaybackFmp4LiveMediaSegmentPayloadProjection? projection)
    {
        projection = null;
        if (initializationDefaults is null)
        {
            return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .MissingInitializationDefaults;
        }

        if (fragmentBinding is null)
        {
            return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .MissingFragmentBinding;
        }

        if (PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                mediaSegment,
                maxSegmentBytes,
                out var envelope) !=
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted ||
            envelope is null ||
            envelope.Kind != PlaybackFmp4LiveSegmentKind.Media)
        {
            return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .InvalidMediaEnvelope;
        }

        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                mediaSegment,
                0,
                out var movieFragment) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            movieFragment is null ||
            movieFragment.Offset != 0 ||
            movieFragment.BoxLength != envelope.FirstBoxLength ||
            movieFragment.Header.Type != MovieFragmentBoxType)
        {
            return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .InvalidMovieFragmentBox;
        }

        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                mediaSegment,
                envelope.FirstBoxLength,
                out var mediaData) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            mediaData is null ||
            mediaData.Offset != envelope.FirstBoxLength ||
            mediaData.NextOffset != mediaSegment.Length ||
            mediaData.BoxLength != envelope.SecondBoxLength ||
            mediaData.Header.Type != MediaDataBoxType)
        {
            return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .InvalidMediaDataBox;
        }

        var movieFragmentBox = mediaSegment.Slice(
            movieFragment.Offset,
            movieFragment.BoxLength);
        if (PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
                movieFragmentBox,
                initializationDefaults,
                fragmentBinding,
                out var resolvedRuns) !=
                PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                    .Projected ||
            resolvedRuns is null)
        {
            return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .InvalidResolvedRunProjection;
        }

        var mediaDataPayloadStart = new BigInteger(mediaData.PayloadOffset);
        var mediaDataPayloadEndExclusive =
            mediaDataPayloadStart + mediaData.PayloadLength;
        var tracks = new PlaybackFmp4LiveMediaSegmentTrackPayloadProjection[
            resolvedRuns.Tracks.Count];
        var projectedRanges = new List<
            (BigInteger Offset, BigInteger EndOffsetExclusive)>();
        for (var index = 0; index < tracks.Length; index++)
        {
            var resolvedTrack = resolvedRuns.Tracks[index];
            var originResult = PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                resolvedTrack.Identity.TrackFragmentHeader,
                resolvedTrack.TrackRun,
                new PlaybackIsoBmffFragmentRunDataOriginContext(
                    MovieFragmentOffset: BigInteger.Zero),
                out var dataOrigin);
            if (originResult !=
                    PlaybackIsoBmffFragmentRunDataOriginResolveResult.Resolved ||
                dataOrigin is null)
            {
                return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                    .InvalidRunDataOrigin;
            }

            if (dataOrigin.BaseDataOriginKind !=
                    PlaybackIsoBmffFragmentBaseDataOriginKind.MovieFragment ||
                dataOrigin.RunDataStartKind !=
                    PlaybackIsoBmffFragmentRunDataStartKind
                        .TrackRunRelativeOffset)
            {
                return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                    .UnsupportedDataOrigin;
            }

            if (dataOrigin.RunDataStart < mediaDataPayloadStart ||
                dataOrigin.RunDataStart > mediaDataPayloadEndExclusive)
            {
                return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                    .SampleOutsideMediaData;
            }

            if (PlaybackIsoBmffFragmentSampleByteRangeProjector.Project(
                    dataOrigin,
                    resolvedTrack.ResolvedRun,
                    out var sampleByteRanges) !=
                    PlaybackIsoBmffFragmentSampleByteRangeProjectResult.Projected ||
                sampleByteRanges is null)
            {
                return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                    .InvalidSampleByteRangeProjection;
            }

            foreach (var range in sampleByteRanges.Ranges)
            {
                if (range.Length == 0)
                {
                    return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                        .ZeroSampleSize;
                }

                if (range.Offset < BigInteger.Zero ||
                    range.EndOffsetExclusive < range.Offset ||
                    range.Offset > ulong.MaxValue ||
                    range.EndOffsetExclusive > ulong.MaxValue)
                {
                    return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                        .SampleRangeOverflow;
                }

                var boundedRange = new PlaybackIsoBmffSampleByteRange(
                    (ulong)range.Offset,
                    range.Length,
                    (ulong)range.EndOffsetExclusive);
                var boundsResult =
                    PlaybackIsoBmffSampleMediaDataBoundsValidator.Validate(
                        boundedRange,
                        mediaData,
                        containerOffset: 0);
                if (boundsResult ==
                    PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                        .Contained)
                {
                    projectedRanges.Add((
                        range.Offset,
                        range.EndOffsetExclusive));
                    continue;
                }

                if (boundsResult ==
                    PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                        .OffsetOverflow)
                {
                    return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                        .SampleRangeOverflow;
                }

                return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                    .SampleOutsideMediaData;
            }

            tracks[index] = new(
                resolvedTrack,
                dataOrigin,
                sampleByteRanges);
        }

        projectedRanges.Sort(static (left, right) =>
            left.Offset.CompareTo(right.Offset));
        for (var index = 1; index < projectedRanges.Count; index++)
        {
            var previous = projectedRanges[index - 1];
            var current = projectedRanges[index];
            if (current.Offset < previous.EndOffsetExclusive)
            {
                return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                    .OverlappingSampleRanges;
            }
        }

        projection = new(
            envelope,
            mediaData,
            resolvedRuns,
            Array.AsReadOnly(tracks));
        return PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.Projected;
    }
}

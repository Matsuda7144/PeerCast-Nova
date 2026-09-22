using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
{
    Projected,
    InvalidHandlerProjection,
    InvalidTrackBox,
    MissingMediaBox,
    DuplicateMediaBox,
    MissingMediaInformationBox,
    DuplicateMediaInformationBox,
    MissingSampleTableBox,
    DuplicateSampleTableBox,
    MissingSampleDescriptionBox,
    DuplicateSampleDescriptionBox,
    InvalidSampleDescriptionBox,
    UnsupportedSampleDescriptionShape,
    UnsupportedSampleEntry,
    HandlerMismatch,
}

public sealed class PlaybackFmp4LiveInitializationTrackSampleDescription
{
    internal PlaybackFmp4LiveInitializationTrackSampleDescription(
        PlaybackFmp4LiveInitializationTrackHandler source,
        PlaybackIsoBmffSampleDescriptionBox sampleDescription,
        PlaybackIsoBmffSampleEntryCodecCandidate codecCandidate)
    {
        Source = source;
        SampleDescription = sampleDescription;
        CodecCandidate = codecCandidate;
    }

    public PlaybackFmp4LiveInitializationTrackHandler Source { get; }

    public PlaybackIsoBmffSampleDescriptionBox SampleDescription { get; }

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate { get; }

    public PlaybackIsoBmffHandlerKind Kind => Source.Kind;
}

public sealed class PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection
{
    internal PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection(
        PlaybackFmp4LiveInitializationTrackHandlerProjection source,
        ReadOnlyCollection<PlaybackFmp4LiveInitializationTrackSampleDescription>
            tracks)
    {
        Source = source;
        Tracks = tracks;
    }

    public PlaybackFmp4LiveInitializationTrackHandlerProjection Source { get; }

    public IReadOnlyList<PlaybackFmp4LiveInitializationTrackSampleDescription>
        Tracks
    { get; }
}

public static class PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector
{
    private const uint MediaBoxType = 0x6d646961;
    private const uint MediaInformationBoxType = 0x6d696e66;
    private const uint SampleTableBoxType = 0x7374626c;
    private const uint SampleDescriptionBoxType = 0x73747364;
    private const uint Avc1SampleEntryType = 0x61766331;
    private const uint Avc3SampleEntryType = 0x61766333;
    private const uint Mp4aSampleEntryType = 0x6d703461;

    private enum ChildLookupResult
    {
        Found,
        Missing,
        Duplicate,
        InvalidSequence,
    }

    public static
        PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
        Project(
            ReadOnlySpan<byte> initializationSegment,
            int maxSegmentBytes,
            out PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection?
                projection)
    {
        projection = null;
        if (PlaybackFmp4LiveInitializationTrackHandlerProjector.Project(
                initializationSegment,
                maxSegmentBytes,
                out var handlers) !=
                PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .Projected ||
            handlers is null)
        {
            return
                PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                    .InvalidHandlerProjection;
        }

        var tracks = new PlaybackFmp4LiveInitializationTrackSampleDescription[
            handlers.Tracks.Count];
        for (var index = 0; index < handlers.Tracks.Count; index++)
        {
            var source = handlers.Tracks[index];
            var recordedTrack = source.Track;
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    initializationSegment,
                    recordedTrack.Offset,
                    out var actualTrack) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                actualTrack is null ||
                actualTrack.Offset != recordedTrack.Offset ||
                actualTrack.BoxLength != recordedTrack.BoxLength ||
                actualTrack.Header.Type != recordedTrack.Header.Type ||
                actualTrack.Header.HeaderSize != recordedTrack.Header.HeaderSize)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .InvalidTrackBox;
            }

            var trackPayload = initializationSegment.Slice(
                actualTrack.PayloadOffset,
                actualTrack.PayloadLength);
            var mediaResult = FindUniqueChild(
                trackPayload,
                MediaBoxType,
                out var media);
            if (mediaResult == ChildLookupResult.Missing)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .MissingMediaBox;
            }

            if (mediaResult == ChildLookupResult.Duplicate)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .DuplicateMediaBox;
            }

            if (mediaResult != ChildLookupResult.Found || media is null)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .InvalidTrackBox;
            }

            var mediaPayload = trackPayload.Slice(
                media.PayloadOffset,
                media.PayloadLength);
            var mediaInformationResult = FindUniqueChild(
                mediaPayload,
                MediaInformationBoxType,
                out var mediaInformation);
            if (mediaInformationResult == ChildLookupResult.Missing)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .MissingMediaInformationBox;
            }

            if (mediaInformationResult == ChildLookupResult.Duplicate)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .DuplicateMediaInformationBox;
            }

            if (mediaInformationResult != ChildLookupResult.Found ||
                mediaInformation is null)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .InvalidTrackBox;
            }

            var mediaInformationPayload = mediaPayload.Slice(
                mediaInformation.PayloadOffset,
                mediaInformation.PayloadLength);
            var sampleTableResult = FindUniqueChild(
                mediaInformationPayload,
                SampleTableBoxType,
                out var sampleTable);
            if (sampleTableResult == ChildLookupResult.Missing)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .MissingSampleTableBox;
            }

            if (sampleTableResult == ChildLookupResult.Duplicate)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .DuplicateSampleTableBox;
            }

            if (sampleTableResult != ChildLookupResult.Found ||
                sampleTable is null)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .InvalidTrackBox;
            }

            var sampleTablePayload = mediaInformationPayload.Slice(
                sampleTable.PayloadOffset,
                sampleTable.PayloadLength);
            var sampleDescriptionResult = FindUniqueChild(
                sampleTablePayload,
                SampleDescriptionBoxType,
                out var sampleDescriptionLocation);
            if (sampleDescriptionResult == ChildLookupResult.Missing)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .MissingSampleDescriptionBox;
            }

            if (sampleDescriptionResult == ChildLookupResult.Duplicate)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .DuplicateSampleDescriptionBox;
            }

            if (sampleDescriptionResult != ChildLookupResult.Found ||
                sampleDescriptionLocation is null)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .InvalidTrackBox;
            }

            var sampleDescriptionBox = sampleTablePayload.Slice(
                sampleDescriptionLocation.Offset,
                sampleDescriptionLocation.BoxLength);
            if (PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                    sampleDescriptionBox,
                    out var sampleDescription) !=
                    PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed ||
                sampleDescription is null)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .InvalidSampleDescriptionBox;
            }

            if (sampleDescription.Entries.Count != 1)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .UnsupportedSampleDescriptionShape;
            }

            var codecCandidate = sampleDescription.Entries[0].Header.Type switch
            {
                Avc1SampleEntryType => PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
                Avc3SampleEntryType => PlaybackIsoBmffSampleEntryCodecCandidate.Avc3,
                Mp4aSampleEntryType => PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
                _ => (PlaybackIsoBmffSampleEntryCodecCandidate?)null,
            };
            if (codecCandidate is null)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .UnsupportedSampleEntry;
            }

            var expectedKind = codecCandidate is
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 or
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3
                ? PlaybackIsoBmffHandlerKind.Video
                : PlaybackIsoBmffHandlerKind.Audio;
            if (source.Kind != expectedKind)
            {
                return
                    PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                        .HandlerMismatch;
            }

            tracks[index] = new(
                source,
                sampleDescription,
                codecCandidate.Value);
        }

        projection = new(handlers, Array.AsReadOnly(tracks));
        return
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected;
    }

    private static ChildLookupResult FindUniqueChild(
        ReadOnlySpan<byte> container,
        uint type,
        out PlaybackIsoBmffBoxLocation? match)
    {
        match = null;
        var offset = 0;
        while (offset != container.Length)
        {
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    container,
                    offset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return ChildLookupResult.InvalidSequence;
            }

            if (child.Header.Type == type)
            {
                if (match is not null)
                {
                    return ChildLookupResult.Duplicate;
                }

                match = child;
            }

            offset = child.NextOffset;
        }

        return match is null ? ChildLookupResult.Missing : ChildLookupResult.Found;
    }
}

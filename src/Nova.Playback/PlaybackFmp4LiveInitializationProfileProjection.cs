namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationProfileProjectionResult
{
    Projected,
    InvalidTrackIdentityProjection,
    InvalidSampleDescriptionProjection,
    UnsupportedTrackCount,
    ProjectionTrackMismatch,
    InvalidSampleDescriptionLocation,
    DuplicateVideoTrack,
    DuplicateAudioTrack,
    MissingVideoTrack,
    MissingAudioTrack,
    InvalidVideoConfiguration,
    InvalidAudioConfiguration,
}

public sealed class PlaybackFmp4LiveInitializationProfileProjection
{
    internal PlaybackFmp4LiveInitializationProfileProjection(
        PlaybackFmp4LiveInitializationTrackIdentityProjection identities,
        PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection
            sampleDescriptions,
        PlaybackFmp4LiveInitializationTrackIdentity videoIdentity,
        PlaybackFmp4LiveInitializationAvcTrackConfiguration videoConfiguration,
        PlaybackFmp4LiveInitializationTrackIdentity audioIdentity,
        PlaybackFmp4LiveInitializationAacTrackConfiguration audioConfiguration)
    {
        Identities = identities;
        SampleDescriptions = sampleDescriptions;
        VideoIdentity = videoIdentity;
        VideoConfiguration = videoConfiguration;
        AudioIdentity = audioIdentity;
        AudioConfiguration = audioConfiguration;
    }

    public PlaybackFmp4LiveInitializationTrackIdentityProjection Identities
    { get; }

    public PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection
        SampleDescriptions
    { get; }

    public PlaybackFmp4LiveInitializationTrackIdentity VideoIdentity { get; }

    public PlaybackFmp4LiveInitializationAvcTrackConfiguration VideoConfiguration
    { get; }

    public PlaybackFmp4LiveInitializationTrackIdentity AudioIdentity { get; }

    public PlaybackFmp4LiveInitializationAacTrackConfiguration AudioConfiguration
    { get; }

    public uint VideoTrackId => VideoIdentity.TrackId;

    public uint AudioTrackId => AudioIdentity.TrackId;
}

public static class PlaybackFmp4LiveInitializationProfileProjector
{
    private const uint MediaBoxType = 0x6d646961;
    private const uint MediaInformationBoxType = 0x6d696e66;
    private const uint SampleTableBoxType = 0x7374626c;
    private const uint SampleDescriptionBoxType = 0x73747364;

    private enum ChildLookupResult
    {
        Found,
        Missing,
        Duplicate,
        InvalidSequence,
    }

    public static PlaybackFmp4LiveInitializationProfileProjectionResult Project(
        ReadOnlySpan<byte> initializationSegment,
        int maxSegmentBytes,
        out PlaybackFmp4LiveInitializationProfileProjection? projection)
    {
        projection = null;
        if (PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
                initializationSegment,
                maxSegmentBytes,
                out var identities) !=
                PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .Projected ||
            identities is null)
        {
            return PlaybackFmp4LiveInitializationProfileProjectionResult
                .InvalidTrackIdentityProjection;
        }

        if (PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                initializationSegment,
                maxSegmentBytes,
                out var sampleDescriptions) !=
                PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                    .Projected ||
            sampleDescriptions is null)
        {
            return PlaybackFmp4LiveInitializationProfileProjectionResult
                .InvalidSampleDescriptionProjection;
        }

        if (identities.Tracks.Count != 2 ||
            sampleDescriptions.Tracks.Count != 2)
        {
            return PlaybackFmp4LiveInitializationProfileProjectionResult
                .UnsupportedTrackCount;
        }

        PlaybackFmp4LiveInitializationTrackIdentity? videoIdentity = null;
        PlaybackFmp4LiveInitializationAvcTrackConfiguration? videoConfiguration =
            null;
        PlaybackFmp4LiveInitializationTrackIdentity? audioIdentity = null;
        PlaybackFmp4LiveInitializationAacTrackConfiguration? audioConfiguration =
            null;

        for (var index = 0; index < identities.Tracks.Count; index++)
        {
            var identity = identities.Tracks[index];
            var sampleDescription = sampleDescriptions.Tracks[index];
            if (!SameTrack(identity.Track, sampleDescription.Source.Track))
            {
                return PlaybackFmp4LiveInitializationProfileProjectionResult
                    .ProjectionTrackMismatch;
            }

            if (!TryGetSampleDescriptionBox(
                    initializationSegment,
                    identity.Track,
                    out var sampleDescriptionOffset,
                    out var sampleDescriptionLength))
            {
                return PlaybackFmp4LiveInitializationProfileProjectionResult
                    .InvalidSampleDescriptionLocation;
            }

            var sampleDescriptionBox = initializationSegment.Slice(
                sampleDescriptionOffset,
                sampleDescriptionLength);
            if (sampleDescription.CodecCandidate is
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 or
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3)
            {
                if (videoIdentity is not null)
                {
                    return PlaybackFmp4LiveInitializationProfileProjectionResult
                        .DuplicateVideoTrack;
                }

                if (PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector
                        .Project(
                            sampleDescriptionBox,
                            sampleDescription,
                            out videoConfiguration) !=
                        PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                            .Projected ||
                    videoConfiguration is null)
                {
                    return PlaybackFmp4LiveInitializationProfileProjectionResult
                        .InvalidVideoConfiguration;
                }

                videoIdentity = identity;
            }
            else if (sampleDescription.CodecCandidate ==
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a)
            {
                if (audioIdentity is not null)
                {
                    return PlaybackFmp4LiveInitializationProfileProjectionResult
                        .DuplicateAudioTrack;
                }

                if (PlaybackFmp4LiveInitializationAacTrackConfigurationProjector
                        .Project(
                            sampleDescriptionBox,
                            sampleDescription,
                            out audioConfiguration) !=
                        PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                            .Projected ||
                    audioConfiguration is null)
                {
                    return PlaybackFmp4LiveInitializationProfileProjectionResult
                        .InvalidAudioConfiguration;
                }

                audioIdentity = identity;
            }
        }

        if (videoIdentity is null || videoConfiguration is null)
        {
            return PlaybackFmp4LiveInitializationProfileProjectionResult
                .MissingVideoTrack;
        }

        if (audioIdentity is null || audioConfiguration is null)
        {
            return PlaybackFmp4LiveInitializationProfileProjectionResult
                .MissingAudioTrack;
        }

        projection = new(
            identities,
            sampleDescriptions,
            videoIdentity,
            videoConfiguration,
            audioIdentity,
            audioConfiguration);
        return PlaybackFmp4LiveInitializationProfileProjectionResult.Projected;
    }

    private static bool TryGetSampleDescriptionBox(
        ReadOnlySpan<byte> initializationSegment,
        PlaybackIsoBmffBoxLocation recordedTrack,
        out int offset,
        out int length)
    {
        offset = 0;
        length = 0;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                initializationSegment,
                recordedTrack.Offset,
                out var actualTrack) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            actualTrack is null ||
            !SameTrack(recordedTrack, actualTrack))
        {
            return false;
        }

        var trackPayload = initializationSegment.Slice(
            actualTrack.PayloadOffset,
            actualTrack.PayloadLength);
        if (FindUniqueChild(trackPayload, MediaBoxType, out var media) !=
                ChildLookupResult.Found ||
            media is null)
        {
            return false;
        }

        var mediaPayload = trackPayload.Slice(
            media.PayloadOffset,
            media.PayloadLength);
        if (FindUniqueChild(
                mediaPayload,
                MediaInformationBoxType,
                out var mediaInformation) != ChildLookupResult.Found ||
            mediaInformation is null)
        {
            return false;
        }

        var mediaInformationPayload = mediaPayload.Slice(
            mediaInformation.PayloadOffset,
            mediaInformation.PayloadLength);
        if (FindUniqueChild(
                mediaInformationPayload,
                SampleTableBoxType,
                out var sampleTable) != ChildLookupResult.Found ||
            sampleTable is null)
        {
            return false;
        }

        var sampleTablePayload = mediaInformationPayload.Slice(
            sampleTable.PayloadOffset,
            sampleTable.PayloadLength);
        if (FindUniqueChild(
                sampleTablePayload,
                SampleDescriptionBoxType,
                out var sampleDescription) != ChildLookupResult.Found ||
            sampleDescription is null)
        {
            return false;
        }

        offset = actualTrack.PayloadOffset +
            media.PayloadOffset +
            mediaInformation.PayloadOffset +
            sampleTable.PayloadOffset +
            sampleDescription.Offset;
        length = sampleDescription.BoxLength;
        return offset >= 0 &&
            length > 0 &&
            offset <= initializationSegment.Length - length;
    }

    private static bool SameTrack(
        PlaybackIsoBmffBoxLocation left,
        PlaybackIsoBmffBoxLocation right) =>
        left.Offset == right.Offset &&
        left.BoxLength == right.BoxLength &&
        left.Header.Type == right.Header.Type &&
        left.Header.HeaderSize == right.Header.HeaderSize;

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

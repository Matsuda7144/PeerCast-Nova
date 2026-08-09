namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackCodecPacketTests
{
    [Fact]
    public void Bind_RetainsExactTrackAndPacketSources()
    {
        var packet = Packet(PlaybackIsoBmffHandlerKind.Audio, 48_000);
        var trackIdentity = TrackIdentity(7);
        var mediaTiming = MediaTiming(48_000);
        var handler = Handler(PlaybackIsoBmffHandlerKind.Audio);

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.Bound,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet,
                trackIdentity,
                mediaTiming,
                handler,
                out var trackPacket));
        var bound = Assert.IsType<PlaybackIsoBmffTrackCodecPacket>(trackPacket);
        Assert.Same(packet, bound.Packet);
        Assert.Same(trackIdentity, bound.TrackIdentity);
        Assert.Same(mediaTiming, bound.MediaTiming);
        Assert.Same(handler, bound.Handler);
        Assert.Equal(7u, bound.TrackId);
        Assert.Equal(48_000u, bound.MediaTimescale);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Audio, bound.MediaKind);
    }

    [Fact]
    public void Bind_MissingInputsAreDistinct()
    {
        var packet = Packet(PlaybackIsoBmffHandlerKind.Audio, 48_000);
        var trackIdentity = TrackIdentity(1);
        var mediaTiming = MediaTiming(48_000);
        var handler = Handler(PlaybackIsoBmffHandlerKind.Audio);

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.MissingPacket,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                null, trackIdentity, mediaTiming, handler, out var noPacket));
        Assert.Null(noPacket);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.MissingTrackIdentity,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet, null, mediaTiming, handler, out var noTrack));
        Assert.Null(noTrack);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.MissingMediaTiming,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet, trackIdentity, null, handler, out var noTiming));
        Assert.Null(noTiming);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.MissingHandler,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet, trackIdentity, mediaTiming, null, out var noHandler));
        Assert.Null(noHandler);
    }

    [Fact]
    public void Bind_RejectsInvalidTrackTimescaleAndHandler()
    {
        var packet = Packet(PlaybackIsoBmffHandlerKind.Audio, 48_000);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.InvalidTrackIdentity,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet,
                TrackIdentity(0),
                MediaTiming(48_000),
                Handler(PlaybackIsoBmffHandlerKind.Audio),
                out var invalidTrack));
        Assert.Null(invalidTrack);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult
                .MediaTimescaleMismatch,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet,
                TrackIdentity(1),
                MediaTiming(44_100),
                Handler(PlaybackIsoBmffHandlerKind.Audio),
                out var wrongTimescale));
        Assert.Null(wrongTimescale);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingResult.HandlerMismatch,
            PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                packet,
                TrackIdentity(1),
                MediaTiming(48_000),
                Handler(PlaybackIsoBmffHandlerKind.Video),
                out var wrongHandler));
        Assert.Null(wrongHandler);
    }

    private static PlaybackIsoBmffCodecPacketProjection Packet(
        PlaybackIsoBmffHandlerKind mediaKind,
        uint timescale)
    {
        var codec = mediaKind == PlaybackIsoBmffHandlerKind.Audio
            ? PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a
            : PlaybackIsoBmffSampleEntryCodecCandidate.Avc1;
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            Location(codec == PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a
                ? 0x6D703461u
                : 0x61766331u),
            codec,
            mediaKind);
        var candidate = Candidate(timescale);
        return new(candidate, selection, candidate.Payload, null, null);
    }

    private static PlaybackIsoBmffEditedSamplePacketCandidate Candidate(
        uint timescale)
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(
            10,
            2,
            12,
            0,
            false);
        var appearances = Array.AsReadOnly(
            new[]
            {
                new PlaybackIsoBmffSampleEditMovieOverlap(
                    0,
                    10,
                    12,
                    1,
                    true),
            });
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            1,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
        var editedRange = new PlaybackIsoBmffEditedSampleByteRange(
            admission,
            new(8, 2, 10),
            new(0, 1, 1, 0, 1, 1));
        var mediaData = new PlaybackIsoBmffEditedSampleMediaData(
            editedRange,
            Location(0x6D646174));
        return new(
            new PlaybackIsoBmffOwnedEditedSamplePayload(mediaData, [1, 2]),
            timescale);
    }

    internal static PlaybackIsoBmffTrackHeaderIdentityBox TrackIdentity(
        uint trackId) => new(
            0,
            3,
            0,
            0,
            trackId,
            1_000,
            0,
            0);

    private static PlaybackIsoBmffMediaHeaderTimingBox MediaTiming(
        uint timescale) => new(
            0,
            0,
            0,
            timescale,
            1_000,
            "und");

    private static PlaybackIsoBmffHandlerReferenceBox Handler(
        PlaybackIsoBmffHandlerKind kind) => new(
            kind == PlaybackIsoBmffHandlerKind.Audio
                ? 0x736F756Eu
                : 0x76696465u,
            kind,
            string.Empty);

    private static PlaybackIsoBmffBoxLocation Location(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}

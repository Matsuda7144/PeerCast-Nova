namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackCodecPacketSequenceTests
{
    [Fact]
    public void TryCreate_OwnsExactAscendingTrackPackets()
    {
        var first = Packet(1);
        var second = Packet(2, first);
        var third = Packet(4, first);
        var packets = new[] { first, second, third };

        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult.Created,
            PlaybackIsoBmffTrackCodecPacketSequenceFactory.TryCreate(
                packets,
                out var sequence));
        packets[0] = third;
        var created = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketSequence>(sequence);
        Assert.Same(first.MovieTiming, created.MovieTiming);
        Assert.Same(first.TrackPacket.TrackIdentity, created.TrackIdentity);
        Assert.Same(first.TrackPacket.MediaTiming, created.MediaTiming);
        Assert.Same(first.TrackPacket.Handler, created.Handler);
        Assert.Equal(first.TrackId, created.TrackId);
        Assert.Equal(first.TrackPacket.MediaKind, created.MediaKind);
        Assert.Collection(
            created.Entries,
            entry => AssertEntry(entry, 0, 1, first),
            entry => AssertEntry(entry, 1, 2, second),
            entry => AssertEntry(entry, 2, 4, third));
    }

    [Fact]
    public void TryCreate_RejectsMissingEmptyAndExcessivePackets()
    {
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .MissingPackets,
            null);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .EmptyPacketSequence,
            []);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .InvalidSampleNumber,
            [Packet(0)]);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .PacketLimitExceeded,
            Enumerable.Repeat(
                    Packet(1),
                    PlaybackIsoBmffTrackCodecPacketSequenceFactory
                        .MaximumPacketCount + 1)
                .ToArray());
    }

    [Fact]
    public void TryCreate_RejectsMissingAndForeignMovieOrTrackSources()
    {
        var first = Packet(1);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .MissingPacket,
            [first, null!]);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .MovieTimingMismatch,
            [first, Packet(2)]);

        var foreignTrack = PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
            first.TrackId,
            first.TrackPacket.MediaKind,
            first.MovieTiming,
            sampleNumber: 2);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .TrackSourceMismatch,
            [first, foreignTrack]);
    }

    [Fact]
    public void TryCreate_RejectsForeignMediaTimingAndHandlerSources()
    {
        var first = Packet(1);
        var second = Packet(2, first);
        var foreignTiming = new PlaybackIsoBmffTrackCodecPacket(
            second.TrackPacket.Packet,
            first.TrackPacket.TrackIdentity,
            new(
                first.TrackPacket.MediaTiming.Version,
                first.TrackPacket.MediaTiming.CreationTime,
                first.TrackPacket.MediaTiming.ModificationTime,
                first.TrackPacket.MediaTiming.Timescale,
                first.TrackPacket.MediaTiming.DurationUnits,
                first.TrackPacket.MediaTiming.Language),
            first.TrackPacket.Handler);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .MediaTimingMismatch,
            [first, new(foreignTiming, first.MovieTiming)]);

        var foreignHandler = new PlaybackIsoBmffTrackCodecPacket(
            second.TrackPacket.Packet,
            first.TrackPacket.TrackIdentity,
            first.TrackPacket.MediaTiming,
            new(
                first.TrackPacket.Handler.HandlerType,
                first.TrackPacket.Handler.Kind,
                first.TrackPacket.Handler.Name));
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .HandlerMismatch,
            [first, new(foreignHandler, first.MovieTiming)]);
    }

    [Theory]
    [InlineData(1ul, 1ul)]
    [InlineData(2ul, 1ul)]
    public void TryCreate_RejectsNonIncreasingSampleNumbers(
        ulong firstSampleNumber,
        ulong secondSampleNumber)
    {
        var first = Packet(firstSampleNumber);
        AssertResult(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .NonIncreasingSampleNumber,
            [first, Packet(secondSampleNumber, first)]);
    }

    private static void AssertEntry(
        PlaybackIsoBmffTrackCodecPacketSequenceEntry entry,
        int sequenceIndex,
        ulong sampleNumber,
        PlaybackIsoBmffMovieTrackCodecPacket packet)
    {
        Assert.Equal(sequenceIndex, entry.SequenceIndex);
        Assert.Equal(sampleNumber, entry.SampleNumber);
        Assert.Same(packet, entry.Packet);
    }

    private static void AssertResult(
        PlaybackIsoBmffTrackCodecPacketSequenceCreationResult expected,
        IReadOnlyList<PlaybackIsoBmffMovieTrackCodecPacket>? packets)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackCodecPacketSequenceFactory.TryCreate(
                packets,
                out var sequence));
        Assert.Null(sequence);
    }

    internal static PlaybackIsoBmffTrackCodecPacketSequence Sequence(
        uint trackId = 7,
        PlaybackIsoBmffHandlerKind mediaKind =
            PlaybackIsoBmffHandlerKind.Audio,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming = null,
        IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>? appearances =
            null)
    {
        var first = PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
            trackId,
            mediaKind,
            movieTiming,
            appearances,
            sampleNumber: 1);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketSequenceCreationResult.Created,
            PlaybackIsoBmffTrackCodecPacketSequenceFactory.TryCreate(
                [first, Packet(2, first), Packet(4, first)],
                out var sequence));
        return Assert.IsType<PlaybackIsoBmffTrackCodecPacketSequence>(sequence);
    }

    private static PlaybackIsoBmffMovieTrackCodecPacket Packet(
        ulong sampleNumber,
        PlaybackIsoBmffMovieTrackCodecPacket? source = null) =>
        PlaybackIsoBmffMovieCodecPacketIndexTests.MoviePacket(
            source?.TrackId ?? 7,
            source?.TrackPacket.MediaKind ?? PlaybackIsoBmffHandlerKind.Audio,
            source?.MovieTiming,
            mediaTimescale: source?.MediaTimescale,
            sampleNumber: sampleNumber,
            metadataSource: source);
}

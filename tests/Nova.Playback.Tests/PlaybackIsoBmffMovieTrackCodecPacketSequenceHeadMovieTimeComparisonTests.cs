namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonTests
{
    [Fact]
    public void Compare_UsesExactRationalStartThenEnd()
    {
        AssertRelation(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .VideoBeforeAudio,
            Appearance(45_000, 54_000, 90_000),
            Appearance(24_000, 33_600, 48_000));
        AssertRelation(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .SameMovieInterval,
            Appearance(45_000, 54_000, 90_000),
            Appearance(24_000, 28_800, 48_000));
        AssertRelation(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .AudioBeforeVideo,
            Appearance(45_000, 54_000, 90_000),
            Appearance(12_000, 16_800, 48_000));
    }

    [Fact]
    public void Compare_AvoidsInt128CrossMultiplicationOverflow()
    {
        var nearMaximum = Int128.MaxValue - 2;
        AssertRelation(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .VideoBeforeAudio,
            Appearance(nearMaximum - 1, nearMaximum, 90_000),
            Appearance(nearMaximum, nearMaximum + 1, 48_000));
    }

    [Fact]
    public void Compare_ClassifiesSingleRoleAndExhaustionWithoutWinner()
    {
        var videoOnly = Snapshot(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000),
            0,
            null);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .VideoOnly,
            Compare(videoOnly).Relation);

        var audioOnly = Snapshot(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000),
            null,
            1);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .AudioOnly,
            Compare(audioOnly).Relation);

        var source = Snapshot(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000));
        var exhaustedCursorSet = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                source.CursorSet.Selection,
                Exhausted(source.CursorSet.VideoCursor!),
                Exhausted(source.CursorSet.AudioCursor!));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .Exhausted,
            Compare(new(exhaustedCursorSet, null, null)).Relation);
    }

    [Fact]
    public void Compare_RetainsExactSnapshotAndLeadingAppearances()
    {
        var snapshot = Snapshot(
            Appearance(45_000, 54_000, 90_000),
            Appearance(12_000, 16_800, 48_000));
        var comparison = Compare(snapshot);

        Assert.Same(snapshot, comparison.Snapshot);
        Assert.Equal(snapshot.VideoHead!.Packet.Appearances[0],
            comparison.VideoAppearance);
        Assert.Equal(snapshot.AudioHead!.Packet.Appearances[0],
            comparison.AudioAppearance);
        Assert.Equal(0,
            snapshot.CursorSet.VideoCursor!.NextSequenceIndex);
        Assert.Equal(0,
            snapshot.CursorSet.AudioCursor!.NextSequenceIndex);
    }

    [Fact]
    public void Compare_RejectsMissingOrMismatchedHeads()
    {
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .MissingSnapshot,
            null);
        var snapshot = Snapshot(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000));
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .InvalidVideoHead,
            new(snapshot.CursorSet, snapshot.AudioHead, snapshot.AudioHead));
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .InvalidAudioHead,
            new(snapshot.CursorSet, snapshot.VideoHead, null));
    }

    private static PlaybackIsoBmffTrackCodecPacketSequenceCursor Exhausted(
        PlaybackIsoBmffTrackCodecPacketSequenceCursor cursor) =>
        new(cursor.Sequence, cursor.Sequence.Entries.Count);

    private static void AssertRelation(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
            expected,
        PlaybackIsoBmffSampleEditMovieOverlap video,
        PlaybackIsoBmffSampleEditMovieOverlap audio) =>
        Assert.Equal(expected, Compare(Snapshot(video, audio)).Relation);

    private static void AssertResult(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
            expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot? snapshot)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparer
                .Compare(snapshot, out var comparison));
        Assert.Null(comparison);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
            Compare(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot
                    snapshot)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .Compared,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparer
                .Compare(snapshot, out var comparison));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison>(
                comparison);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot
        Snapshot(
            PlaybackIsoBmffSampleEditMovieOverlap video,
            PlaybackIsoBmffSampleEditMovieOverlap audio,
            int? videoStreamIndex = 0,
            int? audioStreamIndex = 1)
    {
        var movieTiming =
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var sequences = new[]
        {
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                7,
                PlaybackIsoBmffHandlerKind.Video,
                movieTiming,
                [video]),
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                9,
                PlaybackIsoBmffHandlerKind.Audio,
                movieTiming,
                [audio]),
        };
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                new(videoStreamIndex, audioStreamIndex),
                out var selection));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(selection, out var cursorSet));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjectionResult
                .Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshotProjector
                .TryProject(cursorSet, out var snapshot));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot>(snapshot);
    }

    internal static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) => new(0, start, end, denominator, true);
}

using Relation = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult;
using Role = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
{
    [Fact]
    public void Select_UsesEarlierRelationAndRetainsExactSource()
    {
        var comparison = Comparison(
            Appearance(45_000, 54_000, 90_000),
            Appearance(12_000, 16_800, 48_000));

        var selected = Select(comparison, TieBreak.PreferVideo);
        Assert.Same(comparison, selected.Comparison);
        Assert.Equal(TieBreak.PreferVideo, selected.TieBreak);
        Assert.Equal(Role.Audio, selected.Role);
        Assert.Same(comparison.Snapshot.CursorSet.AudioCursor,
            selected.Cursor);
        Assert.Same(comparison.Snapshot.AudioHead, selected.Head);
        Assert.Equal(comparison.AudioAppearance,
            selected.LeadingAppearance);
        Assert.False(selected.IsExhausted);
    }

    [Fact]
    public void Select_AppliesCallerTieBreakOnlyToEqualIntervals()
    {
        var comparison = Comparison(
            Appearance(45_000, 54_000, 90_000),
            Appearance(24_000, 28_800, 48_000));

        Assert.Equal(
            Role.Video,
            Select(comparison, TieBreak.PreferVideo).Role);
        Assert.Equal(
            Role.Audio,
            Select(comparison, TieBreak.PreferAudio).Role);
    }

    [Fact]
    public void Select_ClassifiesSingleRoleAndExhaustion()
    {
        var videoOnly = Comparison(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000),
            0,
            null);
        Assert.Equal(
            Role.Video,
            Select(videoOnly, TieBreak.PreferAudio).Role);

        var audioOnly = Comparison(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000),
            null,
            1);
        Assert.Equal(
            Role.Audio,
            Select(audioOnly, TieBreak.PreferVideo).Role);

        var source = Comparison(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000));
        var cursorSet = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                source.Snapshot.CursorSet.Selection,
                Exhausted(source.Snapshot.CursorSet.VideoCursor!),
                Exhausted(source.Snapshot.CursorSet.AudioCursor!));
        var exhaustedSnapshot = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot(
                cursorSet,
                null,
                null);
        var exhausted =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonTests
                .Compare(exhaustedSnapshot);
        var selected = Select(exhausted, TieBreak.PreferVideo);
        Assert.Equal(Role.None, selected.Role);
        Assert.Null(selected.Cursor);
        Assert.Null(selected.Head);
        Assert.Null(selected.LeadingAppearance);
        Assert.True(selected.IsExhausted);
    }

    [Fact]
    public void Select_RejectsMissingComparisonAndInvalidTieBreak()
    {
        AssertResult(
            Result.MissingComparison,
            null,
            TieBreak.PreferVideo);
        AssertResult(
            Result.InvalidTieBreak,
            Comparison(
                Appearance(10, 12, 90_000),
                Appearance(10, 12, 48_000)),
            (TieBreak)99);
    }

    [Fact]
    public void Select_RejectsInconsistentComparisonWithoutPartialResult()
    {
        var valid = Comparison(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000));
        var invalid = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison(
                valid.Snapshot,
                null,
                valid.AudioAppearance,
                Relation.VideoBeforeAudio);

        AssertResult(
            Result.InvalidComparison,
            invalid,
            TieBreak.PreferVideo);
    }

    private static PlaybackIsoBmffTrackCodecPacketSequenceCursor Exhausted(
        PlaybackIsoBmffTrackCodecPacketSequenceCursor cursor) =>
        new(cursor.Sequence, cursor.Sequence.Entries.Count);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison?
            comparison,
        TieBreak tieBreak)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelector
                .Select(comparison, tieBreak, out var selection));
        Assert.Null(selection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection Select(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
                comparison,
            TieBreak tieBreak)
    {
        Assert.Equal(
            Result.Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelector
                .Select(comparison, tieBreak, out var selection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection>(
                selection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
            Comparison(
                PlaybackIsoBmffSampleEditMovieOverlap video,
                PlaybackIsoBmffSampleEditMovieOverlap audio,
                int? videoStreamIndex = 0,
                int? audioStreamIndex = 1) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonTests
            .Compare(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonTests
                    .Snapshot(
                        video,
                        audio,
                        videoStreamIndex,
                        audioStreamIndex));

    internal static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonTests
            .Appearance(start, end, denominator);

}

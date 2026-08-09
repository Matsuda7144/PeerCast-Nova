using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult;
using Role = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceTests
{
    [Fact]
    public void TryAdvance_AdvancesOnlyExactSelectedVideoCursor()
    {
        var roleSelection = RoleSelection(
            Appearance(12_000, 16_800, 90_000),
            Appearance(24_000, 28_800, 48_000),
            TieBreak.PreferAudio);

        var advance = Advance(roleSelection);
        Assert.Same(roleSelection, advance.RoleSelection);
        Assert.Equal(Role.Video, advance.Role);
        Assert.Same(roleSelection.Comparison.Snapshot.CursorSet,
            advance.SourceCursorSet);
        Assert.Same(roleSelection.Head, advance.Entry);
        Assert.Same(roleSelection.Cursor, advance.Transition.SourceCursor);
        Assert.Equal(
            roleSelection.Cursor!.NextSequenceIndex + 1,
            advance.Transition.SuccessorCursor.NextSequenceIndex);
        Assert.Same(
            advance.Transition.SuccessorCursor,
            advance.SuccessorCursorSet.VideoCursor);
        Assert.Same(
            advance.SourceCursorSet.AudioCursor,
            advance.SuccessorCursorSet.AudioCursor);
        Assert.Equal(0, roleSelection.Cursor.NextSequenceIndex);
    }

    [Fact]
    public void TryAdvance_AdvancesOnlyExactSelectedAudioCursor()
    {
        var roleSelection = RoleSelection(
            Appearance(45_000, 54_000, 90_000),
            Appearance(12_000, 16_800, 48_000),
            TieBreak.PreferVideo);

        var advance = Advance(roleSelection);
        Assert.Equal(Role.Audio, advance.Role);
        Assert.Same(
            advance.SourceCursorSet.VideoCursor,
            advance.SuccessorCursorSet.VideoCursor);
        Assert.Same(
            advance.Transition.SuccessorCursor,
            advance.SuccessorCursorSet.AudioCursor);
        Assert.Same(advance.Entry, advance.Transition.Entry);
    }

    [Fact]
    public void TryAdvance_UsesCallerTieBreakForEqualIntervals()
    {
        var video = Appearance(45_000, 54_000, 90_000);
        var audio = Appearance(24_000, 28_800, 48_000);

        Assert.Equal(
            Role.Video,
            Advance(RoleSelection(video, audio, TieBreak.PreferVideo)).Role);
        Assert.Equal(
            Role.Audio,
            Advance(RoleSelection(video, audio, TieBreak.PreferAudio)).Role);
    }

    [Fact]
    public void TryAdvance_ReportsExactExhaustionWithoutTransition()
    {
        var source = RoleSelection(
            Appearance(10, 12, 90_000),
            Appearance(10, 12, 48_000),
            TieBreak.PreferVideo);
        var sourceSet = source.Comparison.Snapshot.CursorSet;
        var exhaustedSet = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                sourceSet.Selection,
                Exhausted(sourceSet.VideoCursor!),
                Exhausted(sourceSet.AudioCursor!));
        var snapshot = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot(
                exhaustedSet,
                null,
                null);
        var comparison =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonTests
                .Compare(snapshot);
        var exhaustedSelection =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
                .Select(comparison, TieBreak.PreferVideo);

        AssertResult(Result.Exhausted, exhaustedSelection);
    }

    [Fact]
    public void TryAdvance_RejectsMissingOrForgedRoleSelection()
    {
        AssertResult(Result.MissingRoleSelection, null);
        var valid = RoleSelection(
            Appearance(10, 12, 90_000),
            Appearance(20, 22, 48_000),
            TieBreak.PreferVideo);
        var forged = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection(
                valid.Comparison,
                valid.TieBreak,
                Role.Audio,
                valid.Cursor,
                valid.Head,
                valid.LeadingAppearance);

        AssertResult(Result.InvalidRoleSelection, forged);
    }

    private static PlaybackIsoBmffTrackCodecPacketSequenceCursor Exhausted(
        PlaybackIsoBmffTrackCodecPacketSequenceCursor cursor) =>
        new(cursor.Sequence, cursor.Sequence.Entries.Count);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection?
            roleSelection)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvancer
                .TryAdvance(roleSelection, out var advance));
        Assert.Null(advance);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance Advance(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection
                roleSelection)
    {
        Assert.Equal(
            Result.Advanced,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvancer
                .TryAdvance(roleSelection, out var advance));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance>(
                advance);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection
            RoleSelection(
                PlaybackIsoBmffSampleEditMovieOverlap video,
                PlaybackIsoBmffSampleEditMovieOverlap audio,
                TieBreak tieBreak) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
            .Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
                    .Comparison(video, audio),
                tieBreak);

    internal static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionTests
            .Appearance(start, end, denominator);
}

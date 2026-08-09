namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
{
    Advanced,
    Exhausted,
    MissingRoleSelection,
    InvalidRoleSelection,
    CursorAdvanceFailed,
    TransitionValidationFailed,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection
            roleSelection,
        PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgement transition,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
            successorCursorSet)
    {
        RoleSelection = roleSelection;
        Transition = transition;
        SuccessorCursorSet = successorCursorSet;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection
        RoleSelection
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgement Transition
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        SuccessorCursorSet
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role =>
        RoleSelection.Role;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        SourceCursorSet =>
        RoleSelection.Comparison.Snapshot.CursorSet;

    public PlaybackIsoBmffTrackCodecPacketSequenceEntry Entry =>
        Transition.Entry;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvancer
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
            TryAdvance(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection?
                    roleSelection,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance?
                    advance)
    {
        advance = null;
        if (roleSelection is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                    .MissingRoleSelection;
        }

        if (!IsExactRoleSelection(roleSelection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                    .InvalidRoleSelection;
        }

        if (roleSelection.IsExhausted)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                    .Exhausted;
        }

        if (PlaybackIsoBmffTrackCodecPacketSequenceCursorFactory.TryAdvance(
                roleSelection.Cursor,
                out var entry,
                out var successorCursor) !=
            PlaybackIsoBmffTrackCodecPacketSequenceCursorAdvanceResult.Advanced)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                    .CursorAdvanceFailed;
        }

        if (!ReferenceEquals(entry, roleSelection.Head) ||
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementFactory
                .TryCreate(
                    roleSelection.Cursor,
                    entry,
                    successorCursor,
                    out var transition) !=
            PlaybackIsoBmffTrackCodecPacketSequenceAcknowledgementResult
                .Acknowledged)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                    .TransitionValidationFailed;
        }

        var sourceCursorSet =
            roleSelection.Comparison.Snapshot.CursorSet;
        var successorCursorSet = roleSelection.Role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
            ? new PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                sourceCursorSet.Selection,
                successorCursor,
                sourceCursorSet.AudioCursor)
            : new PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet(
                sourceCursorSet.Selection,
                sourceCursorSet.VideoCursor,
                successorCursor);
        advance = new(
            roleSelection,
            transition!,
            successorCursorSet);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvanceResult
                .Advanced;
    }

    private static bool IsExactRoleSelection(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection
            roleSelection)
    {
        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelector
                .Select(
                    roleSelection.Comparison,
                    roleSelection.TieBreak,
                    out var exact) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                .Selected ||
            exact is null)
        {
            return false;
        }

        return exact.Role == roleSelection.Role &&
            ReferenceEquals(exact.Cursor, roleSelection.Cursor) &&
            ReferenceEquals(exact.Head, roleSelection.Head) &&
            exact.LeadingAppearance == roleSelection.LeadingAppearance;
    }
}

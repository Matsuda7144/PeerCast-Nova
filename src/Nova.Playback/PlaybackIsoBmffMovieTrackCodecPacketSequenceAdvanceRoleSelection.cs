namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
{
    PreferVideo,
    PreferAudio,
}

public enum PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
{
    None,
    Video,
    Audio,
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
{
    Selected,
    MissingComparison,
    InvalidTieBreak,
    InvalidComparison,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
            comparison,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
            tieBreak,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry? head,
        PlaybackIsoBmffSampleEditMovieOverlap? leadingAppearance)
    {
        Comparison = comparison;
        TieBreak = tieBreak;
        Role = role;
        Cursor = cursor;
        Head = head;
        LeadingAppearance = leadingAppearance;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
            Comparison
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
        TieBreak
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceCursor? Cursor { get; }

    public PlaybackIsoBmffTrackCodecPacketSequenceEntry? Head { get; }

    public PlaybackIsoBmffSampleEditMovieOverlap? LeadingAppearance { get; }

    public bool IsExhausted =>
        Role == PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.None;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
            Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison?
                    comparison,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                    tieBreak,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelection?
                    selection)
    {
        selection = null;
        if (comparison is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                    .MissingComparison;
        }

        if (!Enum.IsDefined(tieBreak))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                    .InvalidTieBreak;
        }

        var role = ResolveRole(comparison.Relation, tieBreak);
        if (role is null ||
            !TryResolveExactSource(
                comparison,
                role.Value,
                out var cursor,
                out var head,
                out var appearance))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                    .InvalidComparison;
        }

        selection = new(
            comparison,
            tieBreak,
            role.Value,
            cursor,
            head,
            appearance);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRoleSelectionResult
                .Selected;
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole?
        ResolveRole(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                relation,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                tieBreak) =>
        relation switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .Exhausted =>
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.None,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .VideoOnly or
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .VideoBeforeAudio =>
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .AudioOnly or
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .AudioBeforeVideo =>
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                .SameMovieInterval =>
                tieBreak ==
                PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
                    .PreferVideo
                    ? PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Video
                    : PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Audio,
            _ => null,
        };

    private static bool TryResolveExactSource(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
            comparison,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        out PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor,
        out PlaybackIsoBmffTrackCodecPacketSequenceEntry? head,
        out PlaybackIsoBmffSampleEditMovieOverlap? appearance)
    {
        cursor = null;
        head = null;
        appearance = null;
        var snapshot = comparison.Snapshot;
        if (role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.None)
        {
            return snapshot.IsExhausted &&
                comparison.VideoAppearance is null &&
                comparison.AudioAppearance is null;
        }

        var isVideo = role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video;
        cursor = isVideo
            ? snapshot.CursorSet.VideoCursor
            : snapshot.CursorSet.AudioCursor;
        head = isVideo ? snapshot.VideoHead : snapshot.AudioHead;
        appearance = isVideo
            ? comparison.VideoAppearance
            : comparison.AudioAppearance;
        if (cursor is null || head is null || appearance is null ||
            cursor.IsExhausted || cursor.NextSequenceIndex < 0 ||
            cursor.NextSequenceIndex >= cursor.Sequence.Entries.Count ||
            !ReferenceEquals(
                cursor.Sequence.Entries[cursor.NextSequenceIndex],
                head) ||
            head.Packet.Appearances.Count == 0 ||
            head.Packet.Appearances[0] != appearance.Value)
        {
            cursor = null;
            head = null;
            appearance = null;
            return false;
        }

        return role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                comparison.Relation ==
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                        .VideoOnly
                    ? comparison.AudioAppearance is null
                    : comparison.AudioAppearance is not null,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                comparison.Relation ==
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                        .AudioOnly
                    ? comparison.VideoAppearance is null
                    : comparison.VideoAppearance is not null,
            _ => false,
        };
    }
}

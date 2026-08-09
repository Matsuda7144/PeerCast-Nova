namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
{
    Projected,
    MissingAdvance,
    InvalidMergeIndex,
    InvalidAdvance,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep(
        int mergeIndex,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance advance,
        PlaybackIsoBmffIndexedTrackCodecPacketSequence stream,
        PlaybackIsoBmffSampleEditMovieOverlap leadingAppearance)
    {
        MergeIndex = mergeIndex;
        Advance = advance;
        Stream = stream;
        LeadingAppearance = leadingAppearance;
    }

    public int MergeIndex { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance Advance
    { get; }

    public PlaybackIsoBmffIndexedTrackCodecPacketSequence Stream { get; }

    public PlaybackIsoBmffSampleEditMovieOverlap LeadingAppearance { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role =>
        Advance.Role;

    public int StreamIndex => Stream.StreamIndex;

    public uint TrackId => Stream.TrackId;

    public PlaybackIsoBmffTrackCodecPacketSequenceEntry Entry =>
        Advance.Entry;

    public int SequenceIndex => Entry.SequenceIndex;

    public ulong SampleNumber => Entry.SampleNumber;

    public PlaybackIsoBmffMovieTrackCodecPacket Packet => Entry.Packet;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSet
        SuccessorCursorSet => Advance.SuccessorCursorSet;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjector
{
    public const int MaximumMergeStepCount =
        PlaybackIsoBmffTrackCodecPacketSequenceFactory.MaximumPacketCount * 2;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance?
                    advance,
                int mergeIndex,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep?
                    step)
    {
        step = null;
        if (advance is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
                    .MissingAdvance;
        }

        if ((uint)mergeIndex >= (uint)MaximumMergeStepCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
                    .InvalidMergeIndex;
        }

        if (!TryResolveExactStream(
                advance,
                out var stream,
                out var leadingAppearance))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
                    .InvalidAdvance;
        }

        step = new(mergeIndex, advance, stream, leadingAppearance);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStepProjectionResult
                .Projected;
    }

    private static bool TryResolveExactStream(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetAdvance advance,
        out PlaybackIsoBmffIndexedTrackCodecPacketSequence stream,
        out PlaybackIsoBmffSampleEditMovieOverlap leadingAppearance)
    {
        stream = null!;
        leadingAppearance = default;
        var roleSelection = advance.RoleSelection;
        var source = advance.SourceCursorSet;
        var successor = advance.SuccessorCursorSet;
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? sourceCursor;
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? successorCursor;
        bool nonSelectedCursorMatches;
        if (advance.Role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video)
        {
            stream = source.Selection.VideoStream!;
            sourceCursor = source.VideoCursor;
            successorCursor = successor.VideoCursor;
            nonSelectedCursorMatches = ReferenceEquals(
                source.AudioCursor,
                successor.AudioCursor);
        }
        else if (advance.Role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio)
        {
            stream = source.Selection.AudioStream!;
            sourceCursor = source.AudioCursor;
            successorCursor = successor.AudioCursor;
            nonSelectedCursorMatches = ReferenceEquals(
                source.VideoCursor,
                successor.VideoCursor);
        }
        else
        {
            return false;
        }

        if (stream is null || sourceCursor is null || successorCursor is null ||
            !ReferenceEquals(
                roleSelection.Comparison.Snapshot.CursorSet,
                source) ||
            !ReferenceEquals(successor.Selection, source.Selection) ||
            !ReferenceEquals(roleSelection.Cursor, sourceCursor) ||
            !ReferenceEquals(roleSelection.Head, advance.Entry) ||
            !ReferenceEquals(advance.Transition.SourceCursor, sourceCursor) ||
            !ReferenceEquals(
                advance.Transition.SuccessorCursor,
                successorCursor) ||
            !ReferenceEquals(advance.Transition.Entry, advance.Entry) ||
            !ReferenceEquals(sourceCursor.Sequence, stream.Sequence) ||
            successorCursor.NextSequenceIndex !=
                sourceCursor.NextSequenceIndex + 1 ||
            !ReferenceEquals(successorCursor.Sequence, sourceCursor.Sequence) ||
            !nonSelectedCursorMatches ||
            roleSelection.LeadingAppearance is not { } appearance ||
            advance.Entry.Packet.Appearances.Count == 0 ||
            advance.Entry.Packet.Appearances[0] != appearance)
        {
            stream = null!;
            return false;
        }

        leadingAppearance = appearance;
        return true;
    }
}

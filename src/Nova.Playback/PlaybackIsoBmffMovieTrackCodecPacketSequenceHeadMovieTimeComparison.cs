using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
{
    Compared,
    MissingSnapshot,
    InvalidVideoHead,
    InvalidAudioHead,
}

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
{
    Exhausted,
    VideoOnly,
    AudioOnly,
    VideoBeforeAudio,
    SameMovieInterval,
    AudioBeforeVideo,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot snapshot,
            PlaybackIsoBmffSampleEditMovieOverlap? videoAppearance,
            PlaybackIsoBmffSampleEditMovieOverlap? audioAppearance,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                relation)
    {
        Snapshot = snapshot;
        VideoAppearance = videoAppearance;
        AudioAppearance = audioAppearance;
        Relation = relation;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot Snapshot
    { get; }

    public PlaybackIsoBmffSampleEditMovieOverlap? VideoAppearance { get; }

    public PlaybackIsoBmffSampleEditMovieOverlap? AudioAppearance { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
            Relation
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparer
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
            Compare(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadSnapshot?
                    snapshot,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparison?
                    comparison)
    {
        comparison = null;
        if (snapshot is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                    .MissingSnapshot;
        }

        if (!TryGetLeadingAppearance(
                snapshot.CursorSet.VideoCursor,
                snapshot.VideoHead,
                out var videoAppearance))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                    .InvalidVideoHead;
        }

        if (!TryGetLeadingAppearance(
                snapshot.CursorSet.AudioCursor,
                snapshot.AudioHead,
                out var audioAppearance))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                    .InvalidAudioHead;
        }

        var relation = Compare(videoAppearance, audioAppearance);
        comparison = new(
            snapshot,
            videoAppearance,
            audioAppearance,
            relation);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeComparisonResult
                .Compared;
    }

    private static bool TryGetLeadingAppearance(
        PlaybackIsoBmffTrackCodecPacketSequenceCursor? cursor,
        PlaybackIsoBmffTrackCodecPacketSequenceEntry? head,
        out PlaybackIsoBmffSampleEditMovieOverlap? appearance)
    {
        appearance = null;
        if (cursor is null || cursor.IsExhausted)
        {
            return head is null;
        }

        if (cursor.NextSequenceIndex < 0 ||
            cursor.NextSequenceIndex >= cursor.Sequence.Entries.Count ||
            !ReferenceEquals(
                cursor.Sequence.Entries[cursor.NextSequenceIndex],
                head))
        {
            return false;
        }

        var appearances = head.Packet.Appearances;
        if (appearances.Count == 0)
        {
            return false;
        }

        var candidate = appearances[0];
        if (candidate.Denominator == 0 ||
            candidate.Denominator != head.Packet.MediaTimescale ||
            candidate.MovieStartNumerator < 0 ||
            candidate.MovieStartNumerator >=
                candidate.MovieEndNumeratorExclusive ||
            !candidate.ContainsCompleteSample)
        {
            return false;
        }

        appearance = candidate;
        return true;
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
            Compare(
                PlaybackIsoBmffSampleEditMovieOverlap? video,
                PlaybackIsoBmffSampleEditMovieOverlap? audio)
    {
        if (video is null)
        {
            return audio is null
                ? PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                    .Exhausted
                : PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                    .AudioOnly;
        }

        if (audio is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                    .VideoOnly;
        }

        var result = CompareRational(
            video.Value.MovieStartNumerator,
            video.Value.Denominator,
            audio.Value.MovieStartNumerator,
            audio.Value.Denominator);
        if (result == 0)
        {
            result = CompareRational(
                video.Value.MovieEndNumeratorExclusive,
                video.Value.Denominator,
                audio.Value.MovieEndNumeratorExclusive,
                audio.Value.Denominator);
        }

        return result switch
        {
            < 0 =>
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                    .VideoBeforeAudio,
            > 0 =>
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                    .AudioBeforeVideo,
            _ =>
                PlaybackIsoBmffMovieTrackCodecPacketSequenceHeadMovieTimeRelation
                    .SameMovieInterval,
        };
    }

    private static int CompareRational(
        Int128 leftNumerator,
        uint leftDenominator,
        Int128 rightNumerator,
        uint rightDenominator) =>
        ((BigInteger)leftNumerator * rightDenominator).CompareTo(
            (BigInteger)rightNumerator * leftDenominator);
}

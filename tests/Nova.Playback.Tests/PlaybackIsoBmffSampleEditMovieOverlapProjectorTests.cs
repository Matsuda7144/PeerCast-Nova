using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleEditMovieOverlapProjectorTests
{
    [Fact]
    public void TryProject_ReturnsExactFractionalMovieBounds()
    {
        var editList = ParseEditList((4UL, 10L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(2);
        var mediaTiming = ParseMediaTiming(4);
        var overlaps = Lookup(Sample(11, 13), editList, movieTiming, mediaTiming);

        var result = PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
            overlaps,
            editList,
            movieTiming,
            mediaTiming,
            out var movieOverlaps);

        Assert.Equal(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult.Projected,
            result);
        var overlap = Assert.Single(Assert.IsType<
            PlaybackIsoBmffSampleEditMovieOverlapSet>(movieOverlaps).Overlaps);
        Assert.Equal(
            new PlaybackIsoBmffSampleEditMovieOverlap(0, 2, 6, 4, true),
            overlap);
        Assert.False(overlap.HasIntegralStart);
        Assert.False(overlap.HasIntegralEnd);
    }

    [Fact]
    public void TryProject_IncludesPrecedingEmptyEditInMovieOrigin()
    {
        var editList = ParseEditList(
            (3UL, -1L, (short)1, (short)0),
            (4UL, 10L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);
        var overlaps = Lookup(Sample(11, 12), editList, movieTiming, mediaTiming);

        var result = PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
            overlaps,
            editList,
            movieTiming,
            mediaTiming,
            out var movieOverlaps);

        Assert.Equal(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult.Projected,
            result);
        var overlap = Assert.Single(movieOverlaps!.Overlaps);
        Assert.Equal(1, overlap.EntryIndex);
        Assert.Equal((Int128)4, overlap.MovieStartNumerator);
        Assert.Equal((Int128)5, overlap.MovieEndNumeratorExclusive);
        Assert.True(overlap.HasIntegralStart);
        Assert.True(overlap.HasIntegralEnd);
    }

    [Fact]
    public void TryProject_PreservesRepeatedEditAppearances()
    {
        var editList = ParseEditList(
            (2UL, 0L, (short)1, (short)0),
            (2UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);
        var overlaps = Lookup(Sample(0, 1), editList, movieTiming, mediaTiming);

        Assert.Equal(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult.Projected,
            PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
                overlaps,
                editList,
                movieTiming,
                mediaTiming,
                out var movieOverlaps));
        Assert.Equal(
            [
                new PlaybackIsoBmffSampleEditMovieOverlap(0, 0, 1, 1, true),
                new PlaybackIsoBmffSampleEditMovieOverlap(1, 2, 3, 1, true),
            ],
            movieOverlaps!.Overlaps);
    }

    [Fact]
    public void TryProject_MismatchedEditInputsFailClosed()
    {
        var sourceList = ParseEditList((2UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);
        var overlaps = Lookup(
            Sample(0, 1),
            sourceList,
            movieTiming,
            mediaTiming);

        var result = PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
            overlaps,
            ParseEditList((2UL, -1L, (short)1, (short)0)),
            movieTiming,
            mediaTiming,
            out var movieOverlaps);

        Assert.Equal(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult.InvalidOverlap,
            result);
        Assert.Null(movieOverlaps);
    }

    [Fact]
    public void TryProject_MissingInputsAreDistinct()
    {
        var editList = ParseEditList((1UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);
        var overlaps = Lookup(Sample(0, 1), editList, movieTiming, mediaTiming);

        AssertMissing(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingOverlapSet,
            null,
            editList,
            movieTiming,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingEditList,
            overlaps,
            null,
            movieTiming,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingMovieTiming,
            overlaps,
            editList,
            null,
            mediaTiming);
        AssertMissing(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                .MissingMediaTiming,
            overlaps,
            editList,
            movieTiming,
            null);
    }

    private static void AssertMissing(
        PlaybackIsoBmffSampleEditMovieOverlapProjectionResult expected,
        PlaybackIsoBmffSampleEditOverlapSet? overlaps,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming)
    {
        var result = PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
            overlaps,
            editList,
            movieTiming,
            mediaTiming,
            out var projected);
        Assert.Equal(expected, result);
        Assert.Null(projected);
    }

    private static PlaybackIsoBmffSampleEditOverlapSet Lookup(
        PlaybackIsoBmffSamplePresentationTime sample,
        PlaybackIsoBmffEditListBox editList,
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox mediaTiming)
    {
        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.Found,
            PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
                sample,
                editList,
                movieTiming,
                mediaTiming,
                out var overlaps));
        return Assert.IsType<PlaybackIsoBmffSampleEditOverlapSet>(overlaps);
    }

    private static PlaybackIsoBmffSamplePresentationTime Sample(
        long start,
        long end) => new(start, 0, end, 0, false);

    private static PlaybackIsoBmffEditListBox ParseEditList(
        params (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries)
    {
        var box = new byte[16 + (entries.Length * 20)];
        WriteBoxHeader(box, 0x656C7374);
        box[8] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 20);
            var entry = entries[index];
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(offset, 8),
                entry.Duration);
            BinaryPrimitives.WriteInt64BigEndian(box.AsSpan(offset + 8, 8),
                entry.MediaTime);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 16, 2),
                entry.RateInteger);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 18, 2),
                entry.RateFraction);
        }

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(box, out var editList));
        return Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
    }

    private static PlaybackIsoBmffMovieHeaderTimingBox ParseMovieTiming(
        uint timescale)
    {
        var box = new byte[108];
        WriteBoxHeader(box, 0x6D766864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMovieHeaderTimingBox>(timing);
    }

    private static PlaybackIsoBmffMediaHeaderTimingBox ParseMediaTiming(
        uint timescale)
    {
        var box = new byte[32];
        WriteBoxHeader(box, 0x6D646864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(28, 2), 0x55C4);
        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
    }

    private static void WriteBoxHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}

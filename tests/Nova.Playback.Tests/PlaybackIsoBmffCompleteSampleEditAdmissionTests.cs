using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffCompleteSampleEditAdmissionTests
{
    [Fact]
    public void Admit_RetainsOnlyCompleteAppearances()
    {
        var overlaps = Project(
            Sample(5, 15),
            (10UL, 0L, (short)1, (short)0),
            (10UL, 5L, (short)1, (short)0));

        var result = PlaybackIsoBmffCompleteSampleEditAdmissionPolicy.Admit(
            overlaps,
            out var admission);

        Assert.Equal(
            PlaybackIsoBmffCompleteSampleEditAdmissionResult.Admitted,
            result);
        var admitted = Assert.IsType<
            PlaybackIsoBmffCompleteSampleEditAdmission>(admission);
        Assert.Equal(overlaps.Sample, admitted.Sample);
        var appearance = Assert.Single(admitted.Appearances);
        Assert.Equal(1, appearance.EntryIndex);
        Assert.True(appearance.ContainsCompleteSample);
    }

    [Fact]
    public void Admit_PreservesRepeatedCompleteAppearances()
    {
        var overlaps = Project(
            Sample(0, 1),
            (2UL, 0L, (short)1, (short)0),
            (2UL, 0L, (short)1, (short)0));

        Assert.Equal(
            PlaybackIsoBmffCompleteSampleEditAdmissionResult.Admitted,
            PlaybackIsoBmffCompleteSampleEditAdmissionPolicy.Admit(
                overlaps,
                out var admission));
        Assert.Equal(2, admission!.Appearances.Count);
        Assert.All(admission.Appearances,
            appearance => Assert.True(appearance.ContainsCompleteSample));
    }

    [Fact]
    public void Admit_PartialOnlySampleIsRejected()
    {
        var overlaps = Project(
            Sample(5, 15),
            (10UL, 0L, (short)1, (short)0));

        var result = PlaybackIsoBmffCompleteSampleEditAdmissionPolicy.Admit(
            overlaps,
            out var admission);

        Assert.Equal(
            PlaybackIsoBmffCompleteSampleEditAdmissionResult
                .PartialSampleRejected,
            result);
        Assert.Null(admission);
    }

    [Fact]
    public void Admit_MissingSetFailsClosed()
    {
        var result = PlaybackIsoBmffCompleteSampleEditAdmissionPolicy.Admit(
            null,
            out var admission);

        Assert.Equal(
            PlaybackIsoBmffCompleteSampleEditAdmissionResult
                .MissingMovieOverlapSet,
            result);
        Assert.Null(admission);
    }

    private static PlaybackIsoBmffSampleEditMovieOverlapSet Project(
        PlaybackIsoBmffSamplePresentationTime sample,
        params (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries)
    {
        var editList = ParseEditList(entries);
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);
        Assert.Equal(
            PlaybackIsoBmffSampleEditOverlapLookupResult.Found,
            PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
                sample,
                editList,
                movieTiming,
                mediaTiming,
                out var mediaOverlaps));
        Assert.Equal(
            PlaybackIsoBmffSampleEditMovieOverlapProjectionResult.Projected,
            PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
                mediaOverlaps,
                editList,
                movieTiming,
                mediaTiming,
                out var movieOverlaps));
        return Assert.IsType<PlaybackIsoBmffSampleEditMovieOverlapSet>(
            movieOverlaps);
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

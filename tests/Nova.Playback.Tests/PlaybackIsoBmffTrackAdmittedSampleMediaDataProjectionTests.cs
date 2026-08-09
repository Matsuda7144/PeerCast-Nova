using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionTests
{
    [Fact]
    public void Project_RetainsExactRangesAndContainingMediaDataBox()
    {
        var file = File();
        var source = ByteRanges();

        var projection = Project(file, source);
        Assert.Same(source, projection.Source);
        Assert.Equal(file.Length, projection.FileLength);
        Assert.Equal(
            PlaybackIsoBmffSampleMediaDataLookupOptions.DefaultMaximumBoxCount,
            projection.MaximumBoxCount);
        Assert.Equal(source.Entries.Count, projection.Entries.Count);
        for (var index = 0; index < projection.Entries.Count; index++)
        {
            var entry = projection.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Same(
                source.Entries[index].EditedRange,
                entry.MediaData.EditedRange);
            Assert.Equal(source.Entries[index].SampleNumber,
                entry.SampleNumber);
            Assert.Equal(source.Entries[index].Range, entry.Range);
            Assert.Equal(92, entry.MediaDataBox.Offset);
            Assert.Equal(100, entry.MediaDataBox.PayloadOffset);
        }
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        AssertResult(Result.MissingByteRangeProjection, File(), null);
    }

    [Fact]
    public void Project_RejectsSampleOutsideFileWithoutPartialProjection()
    {
        AssertResult(
            Result.SampleOutsideFile,
            PlaybackIsoBmffEditedSampleMediaDataLookupTests.CreateFile(
                (0x66726565, 92),
                (0x6D646174, 12)),
            ByteRanges());
    }

    [Fact]
    public void Project_PreservesBoxTraversalLimit()
    {
        var result = PlaybackIsoBmffTrackAdmittedSampleMediaDataProjector
            .Project(
                File(),
                ByteRanges(),
                out var projection,
                new() { MaximumBoxCount = 1 });

        Assert.Equal(Result.BoxLimitExceeded, result);
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection
        Projection(byte[] file) => Project(file, ByteRanges());

    internal static byte[] File()
    {
        var file = PlaybackIsoBmffEditedSampleMediaDataLookupTests.CreateFile(
            (0x66726565, 92),
            (0x6D646174, 20));
        new byte[]
        {
            1, 2, 1, 5,
            1, 6, 1, 7,
            9, 10, 11, 12,
        }.CopyTo(file, 100);

        return file;
    }

    private static PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection
        Project(
            byte[] file,
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackAdmittedSampleMediaDataProjector.Project(
                file,
                source,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection>(projection);
    }

    private static void AssertResult(
        Result expected,
        byte[] file,
        PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackAdmittedSampleMediaDataProjector.Project(
                file,
                source,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection
        ByteRanges() =>
        PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionTests.Projection();
}

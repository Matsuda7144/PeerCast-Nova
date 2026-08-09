using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackOwnedSamplePayloadProjectionTests
{
    [Fact]
    public void Project_OwnsExactSourceOrderedPayloads()
    {
        var file = File();
        var source = MediaData(file);

        var projection = Project(file, source);
        Assert.Same(source, projection.Source);
        Assert.Equal(file.Length, projection.FileLength);
        Assert.Equal(
            PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes,
            projection.MaximumPayloadBytes);
        Assert.Equal(8UL, projection.TotalPayloadBytes);
        Assert.Collection(
            projection.Entries,
            first => AssertEntry(first, source.Entries[0], [1, 2, 1, 5]),
            second => AssertEntry(second, source.Entries[1], [1, 6, 1, 7]));

        file[100] = 99;
        Assert.Equal(1, projection.Entries[0].Payload.Span[0]);
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        AssertResult(Result.MissingMediaDataProjection, File(), null);
    }

    [Fact]
    public void Project_RejectsPayloadBeyondCallerLimitWithoutPartialProjection()
    {
        var file = File();
        var result = PlaybackIsoBmffTrackOwnedSamplePayloadProjector.Project(
            file,
            MediaData(file),
            out var projection,
            new() { MaximumPayloadBytes = 3 });

        Assert.Equal(Result.PayloadLimitExceeded, result);
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackOwnedSamplePayloadProjection
        Projection()
    {
        var file = File();
        return Project(file, MediaData(file));
    }

    private static void AssertEntry(
        PlaybackIsoBmffTrackOwnedSamplePayload entry,
        PlaybackIsoBmffTrackAdmittedSampleMediaData source,
        byte[] expectedPayload)
    {
        Assert.Same(source, entry.Source);
        Assert.Same(source.MediaData, entry.OwnedPayload.MediaData);
        Assert.Equal(source.SampleNumber, entry.SampleNumber);
        Assert.Equal(expectedPayload, entry.Payload.ToArray());
    }

    private static PlaybackIsoBmffTrackOwnedSamplePayloadProjection Project(
        byte[] file,
        PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackOwnedSamplePayloadProjector.Project(
                file,
                source,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackOwnedSamplePayloadProjection>(projection);
    }

    private static void AssertResult(
        Result expected,
        byte[] file,
        PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackOwnedSamplePayloadProjector.Project(
                file,
                source,
                out var projection));
        Assert.Null(projection);
    }

    private static byte[] File() =>
        PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionTests.File();

    private static PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection
        MediaData(byte[] file) =>
        PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionTests
            .Projection(file);
}

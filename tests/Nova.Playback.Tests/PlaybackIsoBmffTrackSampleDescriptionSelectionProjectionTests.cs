using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionTests
{
    [Theory]
    [InlineData(0x61766331U, 0x76696465U,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
        PlaybackIsoBmffHandlerKind.Video)]
    [InlineData(0x61766333U, 0x76696465U,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc3,
        PlaybackIsoBmffHandlerKind.Video)]
    [InlineData(0x6D703461U, 0x736F756EU,
        PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
        PlaybackIsoBmffHandlerKind.Audio)]
    public void Project_RetainsExactCandidateDescriptionAndHandler(
        uint entryType,
        uint handlerType,
        PlaybackIsoBmffSampleEntryCodecCandidate codecCandidate,
        PlaybackIsoBmffHandlerKind mediaKind)
    {
        var source = Candidates();
        var sampleDescription = SampleDescription(entryType);
        var handler = Handler(handlerType);

        var projection = Project(source, sampleDescription, handler);
        Assert.Same(source, projection.Source);
        Assert.Same(sampleDescription, projection.SampleDescription);
        Assert.Same(handler, projection.Handler);
        Assert.Equal(source.Entries.Count, projection.Entries.Count);
        for (var index = 0; index < projection.Entries.Count; index++)
        {
            var entry = projection.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Equal(source.Entries[index].SampleNumber,
                entry.SampleNumber);
            Assert.Equal(1U, entry.SampleDescriptionIndex);
            Assert.Equal(codecCandidate, entry.CodecCandidate);
            Assert.Equal(mediaKind, entry.MediaKind);
            Assert.Same(sampleDescription.Entries[0],
                entry.Selection.Entry);
        }
    }

    [Fact]
    public void Project_RejectsMissingInputs()
    {
        AssertResult(Result.MissingCandidateProjection, null, null, null);
        var source = Candidates();
        AssertResult(
            Result.MissingSampleDescription,
            source,
            null,
            Handler(0x736F756E));
        AssertResult(
            Result.MissingHandler,
            source,
            SampleDescription(0x6D703461),
            null);
    }

    [Fact]
    public void Project_RejectsHandlerMismatch()
    {
        AssertResult(
            Result.HandlerMismatch,
            Candidates(),
            SampleDescription(0x6D703461),
            Handler(0x76696465));
    }

    [Fact]
    public void Project_RejectsDescriptionIndexOutsideTable()
    {
        AssertResult(
            Result.SampleDescriptionIndexOutOfRange,
            Candidates(),
            PlaybackIsoBmffEditedSampleDescriptionSelectorTests
                .ParseSampleDescription(),
            Handler(0x736F756E));
    }

    internal static PlaybackIsoBmffTrackSampleDescriptionSelectionProjection
        AudioProjection() =>
        Project(
            Candidates(),
            SampleDescription(0x6D703461),
            Handler(0x736F756E));

    internal static PlaybackIsoBmffTrackSampleDescriptionSelectionProjection
        Project(
            PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection source,
            PlaybackIsoBmffSampleDescriptionBox sampleDescription,
            PlaybackIsoBmffHandlerReferenceBox handler)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackSampleDescriptionSelectionProjector.Project(
                source,
                sampleDescription,
                handler,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackSampleDescriptionSelectionProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection? source,
        PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
        PlaybackIsoBmffHandlerReferenceBox? handler)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackSampleDescriptionSelectionProjector.Project(
                source,
                sampleDescription,
                handler,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection
        Candidates() =>
        PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionTests
            .Projection();

    private static PlaybackIsoBmffSampleDescriptionBox SampleDescription(
        params uint[] entryTypes) =>
        PlaybackIsoBmffEditedSampleDescriptionSelectorTests
            .ParseSampleDescription(entryTypes);

    private static PlaybackIsoBmffHandlerReferenceBox Handler(uint type) =>
        PlaybackIsoBmffEditedSampleDescriptionSelectorTests.ParseHandler(type);
}

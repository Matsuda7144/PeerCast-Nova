using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionTests
{
    [Fact]
    public void Project_RetainsExactOwnedPayloadTimingAndAppearances()
    {
        var source = OwnedPayloads();
        var mediaTiming = MediaTiming(source);

        var projection = Project(source, mediaTiming);
        Assert.Same(source, projection.Source);
        Assert.Same(mediaTiming, projection.MediaTiming);
        Assert.Equal(source.Entries.Count, projection.Entries.Count);
        for (var index = 0; index < projection.Entries.Count; index++)
        {
            var entry = projection.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Same(
                source.Entries[index].OwnedPayload,
                entry.Candidate.OwnedPayload);
            Assert.Equal(source.Entries[index].SampleNumber,
                entry.SampleNumber);
            Assert.True(entry.Payload.Span.SequenceEqual(
                source.Entries[index].Payload.Span));
            Assert.Equal(mediaTiming.Timescale,
                entry.Candidate.MediaTimescale);
            Assert.NotEmpty(entry.Candidate.Appearances);
        }
    }

    [Fact]
    public void Project_RejectsMissingInputs()
    {
        AssertResult(Result.MissingOwnedPayloadProjection, null, null);
        var source = OwnedPayloads();
        AssertResult(Result.MissingMediaTiming, source, null);
    }

    [Fact]
    public void Project_RejectsForeignMediaTiming()
    {
        AssertResult(
            Result.MediaTimingMismatch,
            OwnedPayloads(),
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseMediaTiming(1));
    }

    internal static
        PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection Projection()
    {
        var source = OwnedPayloads();
        return Project(source, MediaTiming(source));
    }

    private static
        PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection Project(
            PlaybackIsoBmffTrackOwnedSamplePayloadProjection source,
            PlaybackIsoBmffMediaHeaderTimingBox mediaTiming)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackEditedSamplePacketCandidateProjector.Project(
                source,
                mediaTiming,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackOwnedSamplePayloadProjection? source,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackEditedSamplePacketCandidateProjector.Project(
                source,
                mediaTiming,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffTrackOwnedSamplePayloadProjection
        OwnedPayloads() =>
        PlaybackIsoBmffTrackOwnedSamplePayloadProjectionTests.Projection();

    private static PlaybackIsoBmffMediaHeaderTimingBox MediaTiming(
        PlaybackIsoBmffTrackOwnedSamplePayloadProjection source) =>
        source.Source.Source.Source.Source.MediaTiming;
}

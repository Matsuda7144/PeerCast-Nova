using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffTrackSampleEditAdmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackSampleEditAdmissionProjectionTests
{
    [Fact]
    public void Project_ClassifiesExactPlanEntriesInSourceOrder()
    {
        var source = Sources(10);
        var plan = Assert.IsType<PlaybackIsoBmffTrackSampleSequencePlan>(
            source.Plan);

        var projection = Project(source);
        Assert.Same(plan, projection.Plan);
        Assert.Same(source.TimeToSample, projection.TimeToSample);
        Assert.Same(source.EditList, projection.EditList);
        Assert.Same(source.MovieTiming, projection.MovieTiming);
        Assert.Same(source.MediaTiming, projection.MediaTiming);
        Assert.Collection(
            projection.Entries,
            first => AssertEntry(
                first,
                plan.Entries[0],
                Disposition.Admitted),
            second => AssertEntry(
                second,
                plan.Entries[1],
                Disposition.Admitted),
            third => AssertEntry(
                third,
                plan.Entries[2],
                Disposition.NotPresented));
    }

    [Fact]
    public void Project_DistinguishesPartialFromNotPresentedSamples()
    {
        var projection = Project(Sources(3));

        Assert.Equal(Disposition.PartialSampleRejected,
            projection.Entries[0].Disposition);
        Assert.Equal(Disposition.NotPresented,
            projection.Entries[1].Disposition);
        Assert.Equal(Disposition.NotPresented,
            projection.Entries[2].Disposition);
        Assert.All(projection.Entries,
            entry => Assert.Null(entry.Admission));
    }

    [Fact]
    public void Project_RejectsMissingInputs()
    {
        var source = Sources(10);
        AssertResult(
            Result.MissingPlan,
            source with { Plan = null });
        AssertResult(
            Result.MissingTimeToSample,
            source with { TimeToSample = null });
        AssertResult(
            Result.MissingEditList,
            source with { EditList = null });
        AssertResult(
            Result.MissingMovieTiming,
            source with { MovieTiming = null });
        AssertResult(
            Result.MissingMediaTiming,
            source with { MediaTiming = null });
    }

    [Fact]
    public void Project_RejectsInvalidOverlapLimit()
    {
        var source = Sources(10);
        Assert.Equal(
            Result.InvalidConfiguration,
            PlaybackIsoBmffTrackSampleEditAdmissionProjector.Project(
                source.Plan,
                source.TimeToSample,
                null,
                source.EditList,
                source.MovieTiming,
                source.MediaTiming,
                out var projection,
                new() { MaximumOverlapCount = -1 }));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackSampleEditAdmissionProjection
        Projection(ulong editDuration = 10) => Project(Sources(editDuration));

    private static PlaybackIsoBmffTrackSampleEditAdmissionProjection Project(
        SourcesRecord source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackSampleEditAdmissionProjector.Project(
                source.Plan,
                source.TimeToSample,
                null,
                source.EditList,
                source.MovieTiming,
                source.MediaTiming,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackSampleEditAdmissionProjection>(projection);
    }

    private static void AssertResult(Result expected, SourcesRecord source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackSampleEditAdmissionProjector.Project(
                source.Plan,
                source.TimeToSample,
                null,
                source.EditList,
                source.MovieTiming,
                source.MediaTiming,
                out var projection));
        Assert.Null(projection);
    }

    private static void AssertEntry(
        PlaybackIsoBmffTrackSampleEditAdmission entry,
        PlaybackIsoBmffTrackSampleSequencePlanEntry planEntry,
        Disposition disposition)
    {
        Assert.Same(planEntry, entry.PlanEntry);
        Assert.Equal(planEntry.SampleNumber, entry.SampleNumber);
        Assert.Equal(disposition, entry.Disposition);
        Assert.Equal(disposition == Disposition.Admitted, entry.IsAdmitted);
        Assert.Equal(disposition == Disposition.Admitted,
            entry.Admission is not null);
        if (entry.Admission is not null)
        {
            Assert.Equal(entry.SampleNumber, entry.Admission.SampleNumber);
        }
    }

    private static SourcesRecord Sources(ulong editDuration)
    {
        var reconciliation = new PlaybackIsoBmffReconciledSampleCount(
            3,
            12,
            PlaybackIsoBmffSampleSizeTableKind.Standard);
        Assert.Equal(
            PlaybackIsoBmffTrackSampleSequencePlanCreationResult.Created,
            PlaybackIsoBmffTrackSampleSequencePlanFactory.TryCreate(
                reconciliation,
                1,
                3,
                out var plan));
        return new(
            Assert.IsType<PlaybackIsoBmffTrackSampleSequencePlan>(plan),
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseTimeToSample(
                (3U, 5U)),
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseEditList(
                (editDuration, 0L, (short)1, (short)0)),
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseMovieTiming(1),
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseMediaTiming(1));
    }

    private sealed record SourcesRecord(
        PlaybackIsoBmffTrackSampleSequencePlan? Plan,
        PlaybackIsoBmffTimeToSampleBox? TimeToSample,
        PlaybackIsoBmffEditListBox? EditList,
        PlaybackIsoBmffMovieHeaderTimingBox? MovieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? MediaTiming);
}

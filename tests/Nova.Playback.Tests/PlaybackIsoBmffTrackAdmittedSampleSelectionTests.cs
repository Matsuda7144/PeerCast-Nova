using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffTrackSampleEditAdmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackAdmittedSampleSelectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackAdmittedSampleSelectionTests
{
    [Fact]
    public void Select_RetainsOnlyAdmittedSamplesInSourceOrder()
    {
        var source = Projection();

        var selection = Select(source, 3);
        Assert.Same(source, selection.Source);
        Assert.Equal(3, selection.MaximumSelectionCount);
        Assert.Equal(2, selection.TotalAdmittedCount);
        Assert.False(selection.IsTruncated);
        Assert.Equal(2, selection.Entries.Count);
        Assert.Same(source.Entries[0], selection.Entries[0]);
        Assert.Same(source.Entries[1], selection.Entries[1]);
    }

    [Fact]
    public void Select_ReportsCallerBoundedTruncation()
    {
        var source = Projection();

        var selection = Select(source, 1);
        Assert.Equal(2, selection.TotalAdmittedCount);
        Assert.True(selection.IsTruncated);
        Assert.Single(selection.Entries);
        Assert.Same(source.Entries[0], selection.Entries[0]);
    }

    [Fact]
    public void Select_RejectsMissingSourceAndInvalidBound()
    {
        AssertResult(Result.MissingAdmissionProjection, null, 1);
        AssertResult(Result.InvalidMaximumSelectionCount, Projection(), 0);
    }

    [Fact]
    public void Select_RejectsInvalidDispositionState()
    {
        var source = Projection();
        var entries = source.Entries.ToArray();
        entries[0] = new(
            entries[0].PlanEntry,
            Disposition.NotPresented,
            entries[0].Admission);
        var invalid = new PlaybackIsoBmffTrackSampleEditAdmissionProjection(
            source.Plan,
            source.TimeToSample,
            source.CompositionTimeToSample,
            source.EditList,
            source.MovieTiming,
            source.MediaTiming,
            Array.AsReadOnly(entries));

        AssertResult(Result.InvalidDisposition, invalid, 3);
    }

    private static PlaybackIsoBmffTrackAdmittedSampleSelection Select(
        PlaybackIsoBmffTrackSampleEditAdmissionProjection source,
        int maximumSelectionCount)
    {
        Assert.Equal(
            Result.Selected,
            PlaybackIsoBmffTrackAdmittedSampleSelector.Select(
                source,
                maximumSelectionCount,
                out var selection));
        return Assert.IsType<PlaybackIsoBmffTrackAdmittedSampleSelection>(
            selection);
    }

    internal static PlaybackIsoBmffTrackAdmittedSampleSelection Selection(
        int maximumSelectionCount = 3) =>
        Select(Projection(), maximumSelectionCount);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackSampleEditAdmissionProjection? source,
        int maximumSelectionCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackAdmittedSampleSelector.Select(
                source,
                maximumSelectionCount,
                out var selection));
        Assert.Null(selection);
    }

    private static PlaybackIsoBmffTrackSampleEditAdmissionProjection
        Projection() =>
        PlaybackIsoBmffTrackSampleEditAdmissionProjectionTests.Projection();
}

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionTests
{
    [Fact]
    public void Select_RetainsOnlyAdmittedEntriesInSourceOrder()
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionTests
                .AdmissionProjection(
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Denied,
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Admitted);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                .Selected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelector
                .Select(source, 2, out var selection));
        var selected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelection>(
                selection);
        Assert.Same(source, selected.Source);
        Assert.Equal(2, selected.MaximumSelectionCount);
        Assert.Equal(1, selected.TotalAdmittedCount);
        Assert.False(selected.IsTruncated);
        Assert.Single(selected.Admissions);
        Assert.Same(source.Admissions[1], selected.Admissions[0]);
    }

    [Fact]
    public void Select_ReportsCallerBoundedTruncation()
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionTests
                .AdmissionProjection(
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Admitted,
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Admitted);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                .Selected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelector
                .Select(source, 1, out var selection));
        var selected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelection>(
                selection);
        Assert.Equal(2, selected.TotalAdmittedCount);
        Assert.True(selected.IsTruncated);
        Assert.Single(selected.Admissions);
        Assert.Same(source.Admissions[0], selected.Admissions[0]);
    }

    [Fact]
    public void Select_RejectsMissingSourceAndInvalidBound()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                .MissingAdmissionProjection,
            null,
            1);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                .InvalidMaximumSelectionCount,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionTests
                .AdmissionProjection(
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Admitted,
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Denied),
            0);
    }

    [Fact]
    public void Select_RejectsForeignAdmissionOrder()
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionTests
                .AdmissionProjection(
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Admitted,
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Denied);
        var foreign = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection(
                source.Source,
                Array.AsReadOnly(source.Admissions.Reverse().ToArray()));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
                .SourceMismatch,
            foreign,
            2);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelectionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection?
            source,
        int maximumSelectionCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchAdmittedInvocationSelector
                .Select(source, maximumSelectionCount, out var selection));
        Assert.Null(selection);
    }
}

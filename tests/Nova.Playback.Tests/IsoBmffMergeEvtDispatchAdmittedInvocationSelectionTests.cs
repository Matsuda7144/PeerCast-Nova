using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelectionTests
{
    [Fact]
    public void Select_RetainsOnlyAdmittedEntriesInSourceOrder()
    {
        var source = AdmissionProjection(index => index % 2 == 0
            ? Disposition.Denied
            : Disposition.Admitted);
        var expected = source.Admissions.Where(admission => admission.IsAdmitted)
            .ToArray();

        var selection = Select(source, source.Admissions.Count);
        Assert.Same(source, selection.Source);
        Assert.Equal(source.Admissions.Count, selection.MaximumSelectionCount);
        Assert.Equal(expected.Length, selection.TotalAdmittedCount);
        Assert.False(selection.IsTruncated);
        Assert.Equal(expected.Length, selection.Admissions.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index], selection.Admissions[index]);
        }
    }

    [Fact]
    public void Select_ReportsCallerBoundedTruncation()
    {
        var source = AdmissionProjection(_ => Disposition.Admitted);

        var selection = Select(source, 1);
        Assert.Equal(source.Admissions.Count, selection.TotalAdmittedCount);
        Assert.True(selection.IsTruncated);
        Assert.Single(selection.Admissions);
        Assert.Same(source.Admissions[0], selection.Admissions[0]);
    }

    [Fact]
    public void Select_RejectsMissingSourceAndInvalidBound()
    {
        AssertResult(Result.MissingAdmissionProjection, null, 1);
        AssertResult(
            Result.InvalidMaximumSelectionCount,
            AdmissionProjection(_ => Disposition.Admitted),
            0);
    }

    [Fact]
    public void Select_RejectsForeignAdmissionOrder()
    {
        var source = AdmissionProjection(index => index % 2 == 0
            ? Disposition.Admitted
            : Disposition.Denied);
        var foreign = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection(
                source.Source,
                Array.AsReadOnly(source.Admissions.Reverse().ToArray()));

        AssertResult(Result.SourceMismatch, foreign, source.Admissions.Count);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection
            Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
                    source,
                int maximumSelectionCount)
    {
        Assert.Equal(
            Result.Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelector
                .Select(source, maximumSelectionCount, out var selection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelection>(
                selection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection?
            source,
        int maximumSelectionCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchAdmittedInvocationSelector
                .Select(source, maximumSelectionCount, out var selection));
        Assert.Null(selection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
            AdmissionProjection(Func<int, Disposition> dispositionSelector) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionTests
            .AdmissionProjection(dispositionSelector);
}

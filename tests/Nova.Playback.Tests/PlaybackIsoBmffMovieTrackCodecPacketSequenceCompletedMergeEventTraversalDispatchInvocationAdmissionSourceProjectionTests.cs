using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjectionTests
{
    [Fact]
    public void Project_RetainsIntentSourceAndCallerDispositions()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionTests
                .Projection();
        var dispositions = Dispositions(source, index => index % 2 == 0
            ? Disposition.Admitted
            : Disposition.Denied);

        var projection = Project(source, dispositions);
        Assert.Same(source, projection.Source);
        Assert.Same(
            source.IntentProjection,
            projection.AdmissionProjection.Source);
        for (var index = 0;
            index < projection.AdmissionProjection.Admissions.Count;
            index++)
        {
            Assert.Equal(
                dispositions[index],
                projection.AdmissionProjection.Admissions[index].Disposition);
        }
    }

    [Fact]
    public void Project_RejectsMissingSourceAndDisposition()
    {
        AssertResult(Result.MissingIntentSourceProjection, null, []);
        AssertResult(
            Result.MissingDisposition,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionTests
                .Projection(),
            null);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection
            Projection(Func<int, Disposition> selector)
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjectionTests
                .Projection();
        return Project(source, Dispositions(source, selector));
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
                    source,
                IReadOnlyList<Disposition> dispositions)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjector
                .Project(source, dispositions, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjection>(
                projection);
    }

    private static Disposition[] Dispositions(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection
            source,
        Func<int, Disposition> selector) =>
        Enumerable.Range(0, source.IntentProjection.Intents.Count)
            .Select(selector)
            .ToArray();

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentSourceProjection?
            source,
        IReadOnlyList<Disposition>? dispositions)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionSourceProjector
                .Project(source, dispositions, out var projection));
        Assert.Null(projection);
    }
}

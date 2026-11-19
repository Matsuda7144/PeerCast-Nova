using Disposition = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionDisposition;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionTests
{
    [Fact]
    public void Project_RetainsExactIntentsAndCallerDispositions()
    {
        var source = IntentProjection();
        var dispositions = Enumerable.Range(0, source.Intents.Count)
            .Select(index => index % 2 == 0
                ? Disposition.Admitted
                : Disposition.Denied)
            .ToArray();

        var projection = Project(source, dispositions);
        Assert.Same(source, projection.Source);
        Assert.Equal(source.Intents.Count, projection.Admissions.Count);
        for (var index = 0; index < projection.Admissions.Count; index++)
        {
            var admission = projection.Admissions[index];
            Assert.Same(source.Intents[index], admission.Intent);
            Assert.Equal(dispositions[index], admission.Disposition);
            Assert.Equal(
                dispositions[index] == Disposition.Admitted,
                admission.IsAdmitted);
        }
    }

    [Fact]
    public void Project_RejectsMissingAndMismatchedDispositions()
    {
        AssertResult(Result.MissingInvocationIntentProjection, null, []);
        var source = IntentProjection();
        AssertResult(Result.MissingDisposition, source, null);
        AssertResult(
            Result.DispositionCountMismatch,
            source,
            Enumerable.Repeat(
                Disposition.Admitted,
                source.Intents.Count - 1).ToArray());
    }

    [Fact]
    public void Project_RejectsInvalidDisposition()
    {
        var source = IntentProjection();
        var dispositions = Enumerable.Repeat(
            Disposition.Denied,
            source.Intents.Count).ToArray();
        dispositions[0] = (Disposition)2;
        AssertResult(Result.InvalidDisposition, source, dispositions);
    }

    [Fact]
    public void Project_RejectsForeignIntentOrder()
    {
        var source = IntentProjection();
        var foreign = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection(
                source.Source,
                source.InvocationId,
                Array.AsReadOnly(source.Intents.Reverse().ToArray()));

        AssertResult(
            Result.SourceMismatch,
            foreign,
            Enumerable.Repeat(
                Disposition.Admitted,
                source.Intents.Count).ToArray());
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
            AdmissionProjection(Func<int, Disposition> dispositionSelector)
    {
        var source = IntentProjection();
        var dispositions = Enumerable.Range(0, source.Intents.Count)
            .Select(dispositionSelector)
            .ToArray();
        return Project(source, dispositions);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection
                    source,
                IReadOnlyList<Disposition> dispositions)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjector
                .Project(source, dispositions, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection?
            source,
        IReadOnlyList<Disposition>? dispositions)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationAdmissionProjector
                .Project(source, dispositions, out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection
            IntentProjection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentTests
            .IntentProjection();
}

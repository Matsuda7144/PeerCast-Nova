namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionTests
{
    [Fact]
    public void Project_RetainsExactIntentsAndCallerDispositions()
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
                .IntentProjection();
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition[]
            dispositions =
            [
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                    .Admitted,
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                    .Denied,
            ];

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjector
                .Project(source, dispositions, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(source.Intents.Count, projected.Admissions.Count);
        for (var index = 0; index < projected.Admissions.Count; index++)
        {
            Assert.Same(
                source.Intents[index],
                projected.Admissions[index].Intent);
            Assert.Equal(
                dispositions[index],
                projected.Admissions[index].Disposition);
            Assert.Equal(
                dispositions[index] ==
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                        .Admitted,
                projected.Admissions[index].IsAdmitted);
        }
    }

    [Fact]
    public void Project_RejectsMissingAndMismatchedDispositions()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .MissingInvocationIntentProjection,
            null,
            []);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .MissingDisposition,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
                .IntentProjection(),
            null);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .DispositionCountMismatch,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
                .IntentProjection(),
            [
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                    .Admitted,
            ]);
    }

    [Fact]
    public void Project_RejectsInvalidDisposition()
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
                .IntentProjection();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .InvalidDisposition,
            source,
            [
                (PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition)
                    2,
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                    .Denied,
            ]);
    }

    [Fact]
    public void Project_RejectsForeignIntentOrder()
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
                .IntentProjection();
        var foreign = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection(
                source.Source,
                source.InvocationId,
                Array.AsReadOnly(source.Intents.Reverse().ToArray()));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .SourceMismatch,
            foreign,
            [
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                    .Admitted,
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                    .Admitted,
            ]);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection
        AdmissionProjection(
            params PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition[]
                dispositions)
    {
        var source =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentTests
                .IntentProjection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjector
                .Project(source, dispositions, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection>(
                projection);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection?
            source,
        IReadOnlyList<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition>?
            dispositions)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjector
                .Project(source, dispositions, out var projection));
        Assert.Null(projection);
    }
}

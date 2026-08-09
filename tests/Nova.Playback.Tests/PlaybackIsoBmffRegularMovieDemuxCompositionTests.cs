using Result = PeerCast.Nova.Playback.PlaybackIsoBmffRegularMovieDemuxCompositionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffRegularMovieDemuxCompositionTests
{
    [Fact]
    public void Compose_OwnsSelectedAvcAndAacTracksWithExactMergeOrder()
    {
        var (file, requests) = MovieFixture();
        var selectionRequest = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(0, 1);
        var options = new PlaybackIsoBmffRegularMovieDemuxCompositionOptions
        {
            MaximumTrackCount = 2,
            MaximumMergeStepCount = 4,
        };

        Assert.Equal(
            Result.Composed,
            PlaybackIsoBmffRegularMovieDemuxComposer.Compose(
                file,
                requests,
                selectionRequest,
                out var composition,
                options));
        var composed = Assert.IsType<
            PlaybackIsoBmffRegularMovieDemuxComposition>(composition);

        Assert.Same(options, composed.Options);
        Assert.Equal(selectionRequest, composed.SelectionRequest);
        Assert.Equal(2, composed.Tracks.Count);
        Assert.Same(
            composed.Tracks[0].SequenceProjection,
            composed.SequenceSet.Sources[0]);
        Assert.Same(
            composed.Tracks[1].SequenceProjection,
            composed.SequenceSet.Sources[1]);
        Assert.Same(
            composed.Tracks[0].SequenceProjection,
            composed.Selection.VideoSource);
        Assert.Same(
            composed.Tracks[1].SequenceProjection,
            composed.Selection.AudioSource);
        Assert.True(composed.IsMergeExhausted);
        Assert.False(composed.IsMergeTruncated);
        Assert.Equal(
            [
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
            ],
            composed.MergeTraversal.Traversal.Steps.Select(step => step.Role));
        Assert.Equal(
            [7U, 8U, 7U, 8U],
            composed.MergeTraversal.Traversal.Steps.Select(
                step => step.TrackId));
        Assert.Equal(
            [1UL, 1UL, 2UL, 2UL],
            composed.MergeTraversal.Traversal.Steps.Select(
                step => step.SampleNumber));

        file.AsSpan().Clear();
        Assert.All(
            composed.Tracks.SelectMany(track => track.Sequence.Entries),
            entry => Assert.Equal(
                4,
                entry.Packet.TrackPacket.Packet.Payload.Length));
    }

    [Fact]
    public void Compose_ReportsBoundedMergeTruncationWithoutLosingCursorState()
    {
        var (file, requests) = MovieFixture();

        Assert.Equal(
            Result.Composed,
            PlaybackIsoBmffRegularMovieDemuxComposer.Compose(
                file,
                requests,
                new(0, 1),
                out var composition,
                new()
                {
                    MaximumTrackCount = 2,
                    MaximumMergeStepCount = 2,
                }));
        var composed = Assert.IsType<
            PlaybackIsoBmffRegularMovieDemuxComposition>(composition);

        Assert.False(composed.IsMergeExhausted);
        Assert.True(composed.IsMergeTruncated);
        Assert.Equal(2, composed.MergeTraversal.Traversal.StepCount);
        Assert.Same(
            composed.MergeTraversal.Traversal.FinalCursorSet,
            composed.MergeTraversal.Traversal.Steps[^1].SuccessorCursorSet);
    }

    [Fact]
    public void Compose_FailsClosedAtTheRejectedMovieStage()
    {
        var (file, requests) = MovieFixture();
        AssertResult(Result.MissingTrackRequests, file, null, new(0, 1));
        AssertResult(Result.EmptyTrackRequests, file, [], new(0, 1));
        AssertResult(
            Result.InvalidConfiguration,
            file,
            requests,
            new(0, 1),
            new() { MaximumTrackCount = 0 });
        AssertResult(
            Result.TrackLimitExceeded,
            file,
            requests,
            new(0, 1),
            new() { MaximumTrackCount = 1 });

        var missingSources = requests.ToArray();
        missingSources[1] = missingSources[1] with { Sources = null };
        AssertResult(
            Result.TrackCompositionFailed,
            file,
            missingSources,
            new(0, 1));

        var mismatchedMovie = requests.ToArray();
        var audioSources = Assert.IsType<
            PlaybackIsoBmffRegularTrackDemuxCompositionSources>(
                mismatchedMovie[1].Sources);
        mismatchedMovie[1] = mismatchedMovie[1] with
        {
            Sources = audioSources with
            {
                MovieTiming = new(0, 0, 0, 1, null),
            },
        };
        AssertResult(
            Result.SequenceSetProjectionFailed,
            file,
            mismatchedMovie,
            new(0, 1));
        AssertResult(
            Result.SelectionProjectionFailed,
            file,
            requests,
            new(1, 0));
    }

    private static void AssertResult(
        Result expected,
        byte[] file,
        IReadOnlyList<PlaybackIsoBmffRegularMovieDemuxTrackRequest>? requests,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest selection,
        PlaybackIsoBmffRegularMovieDemuxCompositionOptions? options = null)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffRegularMovieDemuxComposer.Compose(
                file,
                requests,
                selection,
                out var composition,
                options));
        Assert.Null(composition);
    }

    private static (
        byte[] File,
        PlaybackIsoBmffRegularMovieDemuxTrackRequest[] Requests) MovieFixture()
    {
        var video = PlaybackIsoBmffRegularTrackDemuxCompositionTests
            .VideoFixture();
        var audio = PlaybackIsoBmffRegularTrackDemuxCompositionTests
            .AudioFixture();
        audio = audio with
        {
            Sources = audio.Sources with
            {
                MovieTiming = video.Sources.MovieTiming,
                TrackIdentity = PlaybackIsoBmffTrackCodecPacketTests
                    .TrackIdentity(8),
            },
        };

        return (
            video.File,
            [
                new(
                    video.SampleDescriptionBox,
                    video.Sources,
                    new()
                    {
                        MaximumSampleCount = 2,
                        AvcRequirements = new(),
                    }),
                new(
                    audio.SampleDescriptionBox,
                    audio.Sources,
                    new() { MaximumSampleCount = 2 }),
            ]);
    }
}

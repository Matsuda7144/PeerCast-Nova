using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionTests
{
    [Fact]
    public void Project_RetainsCallerOrderAndExactSequenceProjections()
    {
        var movieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            1_000,
            null);
        var video = Sequence(
            7,
            PlaybackIsoBmffHandlerKind.Video,
            movieTiming);
        var audio = Sequence(
            9,
            PlaybackIsoBmffHandlerKind.Audio,
            movieTiming);
        var sources = new[] { video, audio };

        var projection = Project(sources);
        sources[0] = audio;
        Assert.Collection(
            projection.Sources,
            source => Assert.Same(video, source),
            source => Assert.Same(audio, source));
        Assert.Same(movieTiming, projection.SequenceSet.MovieTiming);
        Assert.Collection(
            projection.SequenceSet.Streams,
            stream =>
            {
                Assert.Equal(0, stream.StreamIndex);
                Assert.Same(video.Sequence, stream.Sequence);
            },
            stream =>
            {
                Assert.Equal(1, stream.StreamIndex);
                Assert.Same(audio.Sequence, stream.Sequence);
            });
    }

    [Fact]
    public void Project_RejectsMissingEmptyAndDuplicateTrackSources()
    {
        AssertResult(Result.MissingSequenceProjections, null);
        AssertResult(Result.EmptySequenceSet, []);
        var source = Sequence(
            7,
            PlaybackIsoBmffHandlerKind.Audio,
            new(0, 0, 0, 1_000, null));
        AssertResult(Result.DuplicateTrackId, [source, source]);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection Project(
            IReadOnlyList<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection>
                    sources)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjector.Project(
                sources,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection>(
                projection);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection
        Sequence(
            uint trackId,
            PlaybackIsoBmffHandlerKind mediaKind,
            PlaybackIsoBmffMovieHeaderTimingBox movieTiming)
    {
        byte[] box;
        PlaybackIsoBmffAvcPayloadRequirements? requirements;
        uint handlerType;
        if (mediaKind == PlaybackIsoBmffHandlerKind.Video)
        {
            var record = PlaybackIsoBmffAvcDecoderConfigurationParserTests
                .BaselineRecord();
            record[4] = 0xFC;
            (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
                .CreateVisualEntry(record);
            requirements = new();
            handlerType = 0x76696465;
        }
        else
        {
            (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
                .CreateAudioEntry(
                    PlaybackIsoBmffElementaryStreamDescriptorParserTests
                        .ElementaryStreamBox(
                            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                                .BaselineElementaryStream()));
            requirements = null;
            handlerType = 0x736F756E;
        }

        var configurations =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests
                .Configurations(box, handlerType);
        var packets =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests.Compose(
                configurations,
                requirements);
        Assert.Equal(
            PlaybackIsoBmffTrackCodecPacketBindingProjectionResult.Bound,
            PlaybackIsoBmffTrackCodecPacketBindingProjector.Bind(
                packets,
                PlaybackIsoBmffTrackCodecPacketTests.TrackIdentity(trackId),
                out var trackBinding));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult.Bound,
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjector.Bind(
                Assert.IsType<PlaybackIsoBmffTrackCodecPacketBindingProjection>(
                    trackBinding),
                movieTiming,
                out var movieBinding));
        return PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionTests
            .Project(
                Assert.IsType<
                    PlaybackIsoBmffMovieTrackCodecPacketBindingProjection>(
                        movieBinding));
    }

    private static void AssertResult(
        Result expected,
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection>? sources)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjector.Project(
                sources,
                out var projection));
        Assert.Null(projection);
    }
}

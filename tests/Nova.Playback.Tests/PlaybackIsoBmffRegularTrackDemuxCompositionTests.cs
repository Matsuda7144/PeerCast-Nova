using Result = PeerCast.Nova.Playback.PlaybackIsoBmffRegularTrackDemuxCompositionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffRegularTrackDemuxCompositionTests
{
    [Fact]
    public void Compose_OwnsBoundedAacPacketsThroughExactTrackAndMovieSources()
    {
        var fixture = AudioFixture();
        var options = new PlaybackIsoBmffRegularTrackDemuxCompositionOptions
        {
            MaximumSampleCount = 3,
        };

        Assert.Equal(
            Result.Composed,
            PlaybackIsoBmffRegularTrackDemuxComposer.Compose(
                fixture.File,
                fixture.SampleDescriptionBox,
                fixture.Sources,
                out var composition,
                options));
        var composed = Assert.IsType<
            PlaybackIsoBmffRegularTrackDemuxComposition>(composition);

        Assert.Same(fixture.Sources, composed.Sources);
        Assert.Same(options, composed.Options);
        Assert.Same(fixture.Sources.MovieTiming, composed.Sequence.MovieTiming);
        Assert.Same(
            fixture.Sources.TrackIdentity,
            composed.Sequence.TrackIdentity);
        Assert.Same(fixture.Sources.Handler, composed.Sequence.Handler);
        Assert.False(composed.IsSampleWindowTruncated);
        Assert.Equal(8UL, composed.TotalPayloadBytes);
        Assert.Equal([1UL, 2UL], composed.Sequence.Entries.Select(
            entry => entry.SampleNumber));
        Assert.Equal(
            [new byte[] { 1, 2, 1, 5 }, new byte[] { 1, 6, 1, 7 }],
            composed.Sequence.Entries.Select(
                entry => entry.Packet.TrackPacket.Packet.Payload.ToArray()));

        fixture.File.AsSpan().Clear();
        Assert.Equal(
            [new byte[] { 1, 2, 1, 5 }, new byte[] { 1, 6, 1, 7 }],
            composed.Sequence.Entries.Select(
                entry => entry.Packet.TrackPacket.Packet.Payload.ToArray()));
    }

    [Fact]
    public void Compose_ComposesCallerSelectedAvcPayloadForm()
    {
        var fixture = VideoFixture();
        var requirements = new PlaybackIsoBmffAvcPayloadRequirements();

        Assert.Equal(
            Result.Composed,
            PlaybackIsoBmffRegularTrackDemuxComposer.Compose(
                fixture.File,
                fixture.SampleDescriptionBox,
                fixture.Sources,
                out var composition,
                new()
                {
                    MaximumSampleCount = 1,
                    AvcRequirements = requirements,
                }));
        var composed = Assert.IsType<
            PlaybackIsoBmffRegularTrackDemuxComposition>(composition);

        Assert.True(composed.IsSampleWindowTruncated);
        var packet = Assert.Single(composed.Sequence.Entries).Packet.TrackPacket
            .Packet;
        Assert.Same(requirements, composed.PacketComposition.AvcRequirements);
        Assert.NotNull(packet.AvcPayloadComposition);
        Assert.Equal(new byte[] { 1, 2, 1, 5 }, packet.Payload.ToArray());
    }

    [Fact]
    public void Compose_FailsClosedAtTheRejectedStage()
    {
        var fixture = AudioFixture();
        AssertResult(Result.MissingSources, fixture, null, null);
        AssertResult(
            Result.InvalidConfiguration,
            fixture,
            fixture.Sources,
            new() { MaximumSampleCount = 0 });
        AssertResult(
            Result.MediaDataProjectionFailed,
            fixture with { File = [] },
            fixture.Sources,
            new() { MaximumSampleCount = 3 });
        AssertResult(
            Result.PacketCompositionFailed,
            fixture,
            fixture.Sources,
            new()
            {
                MaximumSampleCount = 3,
                AvcRequirements = new(),
            });
        AssertResult(
            Result.TrackBindingFailed,
            fixture,
            fixture.Sources with { TrackIdentity = null },
            new() { MaximumSampleCount = 3 });
    }

    private static void AssertResult(
        Result expected,
        Fixture fixture,
        PlaybackIsoBmffRegularTrackDemuxCompositionSources? sources,
        PlaybackIsoBmffRegularTrackDemuxCompositionOptions? options)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffRegularTrackDemuxComposer.Compose(
                fixture.File,
                fixture.SampleDescriptionBox,
                sources,
                out var composition,
                options));
        Assert.Null(composition);
    }

    internal static Fixture AudioFixture()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        return FixtureFor(box, 0x736F756E);
    }

    internal static Fixture VideoFixture()
    {
        var record = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .BaselineRecord();
        record[4] = 0xFC;
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(record);
        return FixtureFor(box, 0x76696465);
    }

    internal static Fixture FixtureFor(byte[] box, uint handlerType)
    {
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var sampleDescription));
        var movieTiming = new PlaybackIsoBmffMovieHeaderTimingBox(
            0,
            0,
            0,
            1,
            null);
        var mediaTiming = PlaybackIsoBmffEditedSampleAdmissionLookupTests
            .ParseMediaTiming(1);
        var sources = new PlaybackIsoBmffRegularTrackDemuxCompositionSources(
            new(3, 12, PlaybackIsoBmffSampleSizeTableKind.Standard),
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseTimeToSample(
                (3, 5)),
            null,
            PlaybackIsoBmffEditedSampleAdmissionLookupTests.ParseEditList(
                (10, 0, (short)1, (short)0)),
            movieTiming,
            mediaTiming,
            PlaybackIsoBmffEditedSampleByteRangeLookupTests.ParseSampleToChunk(
                (1, 3, 1)),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3),
            PlaybackIsoBmffEditedSampleByteRangeLookupTests.ParseChunkOffset(
                100),
            null,
            PlaybackIsoBmffEditedSampleByteRangeLookupTests
                .ParseUniformSampleSize(4, 3),
            null,
            Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(
                sampleDescription),
            PlaybackIsoBmffEditedSampleDescriptionSelectorTests.ParseHandler(
                handlerType),
            PlaybackIsoBmffTrackCodecPacketTests.TrackIdentity(7));
        return new(
            PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionTests.File(),
            box,
            sources);
    }

    internal sealed record Fixture(
        byte[] File,
        byte[] SampleDescriptionBox,
        PlaybackIsoBmffRegularTrackDemuxCompositionSources Sources);
}

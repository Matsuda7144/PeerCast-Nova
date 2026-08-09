using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests
{
    [Fact]
    public void Compose_ProjectsExactAvcPacketsInSourceOrder()
    {
        var record = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .BaselineRecord();
        record[4] = 0xFC;
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(record);
        var source = Configurations(box, 0x76696465);
        var requirements = new PlaybackIsoBmffAvcPayloadRequirements();

        var projection = Compose(source, requirements);
        Assert.Same(source, projection.Source);
        Assert.Same(requirements, projection.AvcRequirements);
        Assert.Equal(8UL, projection.TotalPayloadBytes);
        for (var index = 0; index < projection.Entries.Count; index++)
        {
            var entry = projection.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Same(
                entry.Source.Source.Source.Source.Candidate,
                entry.Packet.Candidate);
            Assert.Same(entry.Source.AvcConfiguration,
                entry.Packet.AvcPayloadComposition!.Selection.Classification
                    .Frame.DecoderConfiguration);
            Assert.True(entry.Packet.Payload.Span.SequenceEqual(
                entry.Packet.Candidate.Payload.Span));
        }
    }

    [Fact]
    public void Compose_ProjectsExactAacOriginalPacketsInSourceOrder()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        var source = Configurations(box, 0x736F756E);

        var projection = Compose(source, null);
        Assert.Null(projection.AvcRequirements);
        Assert.Equal(8UL, projection.TotalPayloadBytes);
        Assert.All(projection.Entries, entry =>
        {
            Assert.Null(entry.Packet.AvcPayloadComposition);
            Assert.Same(entry.Source.Source.AudioEntry, entry.Packet.AudioEntry);
            Assert.True(entry.Packet.Payload.Span.SequenceEqual(
                entry.Packet.Candidate.Payload.Span));
        });
    }

    [Fact]
    public void Compose_RequiresCodecSpecificCallerRequirements()
    {
        var record = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .BaselineRecord();
        record[4] = 0xFC;
        var (videoBox, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(record);
        AssertResult(
            Result.MissingAvcRequirements,
            Configurations(videoBox, 0x76696465),
            null);

        var (audioBox, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                .CreateAudioEntry(
                    PlaybackIsoBmffElementaryStreamDescriptorParserTests
                        .ElementaryStreamBox(
                            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                                .BaselineElementaryStream()));
        AssertResult(
            Result.UnexpectedAvcRequirements,
            Configurations(audioBox, 0x736F756E),
            new());
    }

    [Fact]
    public void Compose_RejectsMissingSourceAndInvalidLimits()
    {
        AssertResult(Result.MissingCodecConfigurationProjection, null, null);
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        var source = Configurations(box, 0x736F756E);
        Assert.Equal(
            Result.InvalidConfiguration,
            PlaybackIsoBmffTrackCodecPacketCompositionProjector.Compose(
                source,
                null,
                out var projection,
                new() { MaximumPayloadBytes = 0 }));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackCodecPacketCompositionProjection Compose(
        PlaybackIsoBmffTrackCodecConfigurationProjection source,
        PlaybackIsoBmffAvcPayloadRequirements? requirements)
    {
        Assert.Equal(
            Result.Composed,
            PlaybackIsoBmffTrackCodecPacketCompositionProjector.Compose(
                source,
                requirements,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketCompositionProjection>(projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackCodecConfigurationProjection? source,
        PlaybackIsoBmffAvcPayloadRequirements? requirements)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackCodecPacketCompositionProjector.Compose(
                source,
                requirements,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackCodecConfigurationProjection
        Configurations(byte[] box, uint handlerType)
    {
        var sampleEntries =
            PlaybackIsoBmffTrackCodecConfigurationProjectionTests
                .SampleEntries(box, handlerType);
        return PlaybackIsoBmffTrackCodecConfigurationProjectionTests.Project(
            box,
            sampleEntries);
    }
}

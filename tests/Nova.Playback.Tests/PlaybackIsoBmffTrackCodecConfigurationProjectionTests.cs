using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackCodecConfigurationProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackCodecConfigurationProjectionTests
{
    [Fact]
    public void Project_ParsesExactAvcConfigurationsInSourceOrder()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        var source = SampleEntries(box, 0x76696465);

        var projection = Project(box, source);
        Assert.Same(source, projection.Source);
        Assert.Equal(box.Length, projection.SampleDescriptionBoxLength);
        Assert.All(projection.Entries, entry =>
        {
            Assert.Same(source.Entries[(int)entry.SampleNumber - 1],
                entry.Source);
            Assert.Null(entry.ElementaryStreamDescriptor);
            Assert.Same(entry.Source.VisualEntry,
                Assert.IsType<PlaybackIsoBmffAvcDecoderConfiguration>(
                    entry.AvcConfiguration).VisualEntry);
        });
    }

    [Fact]
    public void Project_ParsesExactElementaryStreamsInSourceOrder()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        var source = SampleEntries(box, 0x736F756E);

        var projection = Project(box, source);
        Assert.All(projection.Entries, entry =>
        {
            Assert.Null(entry.AvcConfiguration);
            Assert.Same(entry.Source.AudioEntry,
                Assert.IsType<PlaybackIsoBmffElementaryStreamDescriptor>(
                    entry.ElementaryStreamDescriptor).AudioEntry);
        });
    }

    [Fact]
    public void Project_RejectsMissingSourceAndStaleConfigurationBytes()
    {
        AssertResult(Result.MissingSampleEntryProjection, [], null);
        var (box, visualEntry) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        var changed = box.ToArray();
        changed[visualEntry.Children[0].PayloadOffset] = 0;
        AssertResult(
            Result.InvalidConfigurationPayload,
            changed,
            SampleEntries(box, 0x76696465));
    }

    [Fact]
    public void Project_RejectsInvalidLimits()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        Assert.Equal(
            Result.InvalidConfiguration,
            PlaybackIsoBmffTrackCodecConfigurationProjector.Project(
                box,
                SampleEntries(box, 0x76696465),
                out var projection,
                new() { MaximumParameterSetCount = 0 }));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackCodecConfigurationProjection Project(
        byte[] box,
        PlaybackIsoBmffTrackSampleEntryParseProjection source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackCodecConfigurationProjector.Project(
                box,
                source,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackCodecConfigurationProjection>(projection);
    }

    private static void AssertResult(
        Result expected,
        ReadOnlySpan<byte> box,
        PlaybackIsoBmffTrackSampleEntryParseProjection? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackCodecConfigurationProjector.Project(
                box,
                source,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackSampleEntryParseProjection SampleEntries(
        byte[] box,
        uint handlerType)
    {
        var descriptions =
            PlaybackIsoBmffTrackSampleEntryParseProjectionTests.Source(
                box,
                handlerType);
        return PlaybackIsoBmffTrackSampleEntryParseProjectionTests.Project(
            box,
            descriptions);
    }
}

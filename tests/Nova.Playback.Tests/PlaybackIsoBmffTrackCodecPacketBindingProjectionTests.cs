using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackCodecPacketBindingProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackCodecPacketBindingProjectionTests
{
    [Fact]
    public void Bind_RetainsExactPacketsAndTrackSourcesInOrder()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        var configurations =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests
                .Configurations(box, 0x736F756E);
        var source =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests.Compose(
                configurations,
                null);
        var trackIdentity = PlaybackIsoBmffTrackCodecPacketTests
            .TrackIdentity(7);

        Assert.Equal(
            Result.Bound,
            PlaybackIsoBmffTrackCodecPacketBindingProjector.Bind(
                source,
                trackIdentity,
                out var projection));
        var bound = Assert.IsType<
            PlaybackIsoBmffTrackCodecPacketBindingProjection>(projection);
        Assert.Same(source, bound.Source);
        Assert.Same(trackIdentity, bound.TrackIdentity);
        Assert.Same(
            configurations.Source.Source.Source.MediaTiming,
            bound.MediaTiming);
        Assert.Same(configurations.Source.Source.Handler, bound.Handler);
        for (var index = 0; index < bound.Entries.Count; index++)
        {
            var entry = bound.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Same(entry.Source.Packet, entry.TrackPacket.Packet);
            Assert.Same(trackIdentity, entry.TrackPacket.TrackIdentity);
            Assert.Equal(7U, entry.TrackPacket.TrackId);
        }
    }

    [Fact]
    public void Bind_RejectsMissingInputsAndInvalidTrackIdentity()
    {
        AssertResult(Result.MissingPacketCompositionProjection, null, null);
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        var source =
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests.Compose(
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionTests
                    .Configurations(box, 0x736F756E),
                null);
        AssertResult(Result.MissingTrackIdentity, source, null);
        AssertResult(
            Result.InvalidTrackIdentity,
            source,
            PlaybackIsoBmffTrackCodecPacketTests.TrackIdentity(0));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackCodecPacketCompositionProjection? source,
        PlaybackIsoBmffTrackHeaderIdentityBox? trackIdentity)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackCodecPacketBindingProjector.Bind(
                source,
                trackIdentity,
                out var projection));
        Assert.Null(projection);
    }
}

using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class ObsFlvPlaybackParserTests
{
    [Fact]
    public async Task Ingest_generated_HEAD_DATA_pass_existing_FLV_parser_with_exact_sample_timing()
    {
        using var consumer = new ParserConsumer();
        var publishing = new ObsFlvPublishingSession(
            new ChannelId(Guid.Parse("9C7D5D7C-02EA-4FF1-82C0-59E4B43BB587")),
            new ChannelSessionId(Guid.Parse("04279298-C2DB-4F84-B27F-A04985562CBD")),
            consumer,
            new ObsRtmpIngestOptions { MaximumBootstrapBytes = 128, MaximumPacketBytes = 128 });
        byte[] videoConfig = new byte[] { 0x17, 0, 0, 0, 0 }
            .Concat(PlaybackBoundedFlvParserTests.BuildAvcConfiguration()).ToArray();
        await publishing.AcceptAsync(new(9, 1, 0, videoConfig), CancellationToken.None);
        await publishing.AcceptAsync(new(8, 1, 0, [0xAF, 0, 0x11, 0x90]), CancellationToken.None);
        Assert.Equal(320, consumer.Parser.Session!.Video.Width);
        Assert.Equal(240, consumer.Parser.Session.Video.Height);
        Assert.Equal(48_000, consumer.Parser.Session.Audio.SampleRate);
        byte[] sample = [0, 0, 0, 2, 0x65, 0x88];
        byte[] video = new byte[] { 0x17, 1, 0xFF, 0xFF, 0xFE }.Concat(sample).ToArray();
        // Exercise RTMP's extended timestamp while staying within the existing
        // parser's default timestamp-magnitude policy.
        const uint timestamp = 0x01000040;
        await publishing.AcceptAsync(new(9, 1, timestamp, video), CancellationToken.None);
        // A large audio tag exercises the existing parser's carry across PCP DATA slices.
        var audio = Enumerable.Repeat((byte)0x22, 300).ToArray();
        byte[] audioMessage = new byte[] { 0xAF, 1 }.Concat(audio).ToArray();
        await publishing.AcceptAsync(new(8, 1, timestamp + 1, audioMessage), CancellationToken.None);
        Assert.Equal(2, consumer.Events.Count);
        var actualVideo = consumer.Events[0];
        Assert.Equal(sample, actualVideo.Bytes);
        Assert.Equal(TimeSpan.FromMilliseconds(timestamp), actualVideo.Dts);
        Assert.Equal(TimeSpan.FromMilliseconds(timestamp - 2), actualVideo.Pts);
        Assert.Equal(PlaybackBoundedDemuxKeyframeState.Keyframe, actualVideo.Keyframe);
        Assert.Equal(audio, consumer.Events[1].Bytes);
        Assert.Equal(TimeSpan.FromMilliseconds(timestamp + 1), consumer.Events[1].Dts);
        Assert.Equal(5, publishing.PacketCount);
    }

    private sealed class ParserConsumer : IPcpChannelPacketConsumer, IDisposable
    {
        public PlaybackBoundedFlvParser Parser { get; } = new();

        public List<(byte[] Bytes, TimeSpan Dts, TimeSpan Pts, PlaybackBoundedDemuxKeyframeState Keyframe)> Events { get; } = [];

        public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = packet.Payload;
            while (!remaining.IsEmpty)
            {
                var operation = Parser.Parse(remaining, cancellationToken: cancellationToken);
                Assert.True(operation.ConsumedByteCount > 0);
                Assert.Contains(operation.Result, new[]
                {
                    PlaybackBoundedFlvParserResult.NeedMoreInput,
                    PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
                    PlaybackBoundedFlvParserResult.PacketsAvailable,
                });
                using var completion = operation.Completion;
                if (completion is not null)
                {
                    foreach (var media in completion.Events)
                    {
                        Events.Add((media.Payload.ToArray(), media.DecodeTimestamp.Value,
                            media.PresentationTimestamp.Value, media.KeyframeState));
                    }
                }

                remaining = remaining[operation.ConsumedByteCount..];
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => Parser.Dispose();
    }
}

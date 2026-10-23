using System.Collections.Concurrent;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsFlvPublishingSessionTests
{
    internal static readonly ChannelId Channel = new(Guid.Parse("EA16119E-4D48-4BC1-A76B-85E27D65BD62"));
    internal static readonly ChannelSessionId Session = new(Guid.Parse("99E27638-8B9A-478C-9B39-1057F56A791D"));
    internal static readonly byte[] AudioConfiguration = [0xAF, 0, 0x12, 0x10];
    internal static readonly byte[] VideoConfiguration =
        [0x17, 0, 0, 0, 0, 1, 0x42, 0, 0x1E, 0xFF, 0xE1, 0, 2, 0x67, 0x42, 1, 0, 2, 0x68, 0xCE];
    internal static readonly byte[] Idr = [0x17, 1, 0xFF, 0xFF, 0xFE, 0, 0, 0, 2, 0x65, 0x88];
    internal static readonly byte[] Audio = [0xAF, 1, 0x11, 0x22, 0x33];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bootstrap_waits_for_both_configs_and_is_compatible_with_existing_flv_retention(bool audioFirst)
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        await publisher.AcceptAsync(Message(audioFirst ? (byte)8 : (byte)9,
            audioFirst ? AudioConfiguration : VideoConfiguration), CancellationToken.None);
        Assert.Empty(consumer.Packets);
        await publisher.AcceptAsync(Message(audioFirst ? (byte)9 : (byte)8,
            audioFirst ? VideoConfiguration : AudioConfiguration), CancellationToken.None);
        var head = Assert.Single(consumer.Packets);
        Assert.Equal(0x64616568U, head.Type);
        Assert.Equal(Channel, head.ChannelId);
        Assert.Equal(Session, head.SessionId);
        var retained = new PcpFlvBootstrapRetention();
        retained.Observe(head.Payload);
        Assert.True(retained.StatusSnapshot.HeaderRetained);
        Assert.True(retained.StatusSnapshot.AudioConfigurationRetained);
        Assert.True(retained.StatusSnapshot.VideoConfigurationRetained);
        Assert.Equal(AudioConfiguration, retained.Snapshot().AudioConfiguration.Span[11..^4].ToArray());
        Assert.Equal(VideoConfiguration, retained.Snapshot().VideoConfiguration.Span[11..^4].ToArray());
    }

    [Fact]
    public async Task Extended_DTS_negative_composition_time_and_encoded_payloads_are_preserved_byte_for_byte()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        await ConfigureAsync(publisher);
        await publisher.AcceptAsync(Message(9, Idr, 0xFE123456), CancellationToken.None);
        await publisher.AcceptAsync(Message(8, Audio, 0xFE123457), CancellationToken.None);
        var packets = consumer.Packets.ToArray();
        byte[] expectedVideo =
            [9, 0, 0, 11, 0x12, 0x34, 0x56, 0xFE, 0, 0, 0,
             0x17, 1, 0xFF, 0xFF, 0xFE, 0, 0, 0, 2, 0x65, 0x88, 0, 0, 0, 22];
        Assert.Equal(expectedVideo, packets[1].Payload);
        byte[] expectedAudio =
            [8, 0, 0, 5, 0x12, 0x34, 0x57, 0xFE, 0, 0, 0, 0xAF, 1, 0x11, 0x22, 0x33, 0, 0, 0, 16];
        Assert.Equal(expectedAudio, packets[2].Payload);
        long position = 0;
        for (var i = 0; i < packets.Length; i++)
        {
            Assert.Equal(i, packets[i].Sequence);
            Assert.Equal(position, packets[i].StreamPosition);
            Assert.Equal((uint)position, packets[i].WirePosition);
            position += packets[i].Payload.Length;
        }
    }

    [Fact]
    public async Task Initial_audio_and_interframes_are_dropped_until_a_real_IDR_after_bootstrap()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        await ConfigureAsync(publisher);
        await publisher.AcceptAsync(Message(8, Audio), CancellationToken.None);
        byte[] interframe = [0x27, 1, 0, 0, 0, 0, 0, 0, 1, 0x41];
        await publisher.AcceptAsync(Message(9, interframe), CancellationToken.None);
        var mislabeled = interframe.ToArray();
        mislabeled[0] = 0x17;
        await publisher.AcceptAsync(Message(9, mislabeled), CancellationToken.None);
        Assert.Single(consumer.Packets);
        await publisher.AcceptAsync(Message(9, Idr), CancellationToken.None);
        await publisher.AcceptAsync(Message(8, Audio), CancellationToken.None);
        Assert.Equal(3, consumer.Packets.Count);
    }

    [Fact]
    public async Task Metadata_removes_RTMP_wrapper_and_retains_original_AMF_encoding()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        var metadata = ObsRtmpTestClient.Command("@setDataFrame", "onMetaData",
            new Dictionary<string, object?> { ["width"] = 320.0 });
        await publisher.AcceptAsync(Message(18, metadata), CancellationToken.None);
        await ConfigureAsync(publisher);
        var retained = new PcpFlvBootstrapRetention();
        retained.Observe(Assert.Single(consumer.Packets).Payload);
        Assert.Equal(metadata[16..], retained.Snapshot().Metadata.Span[11..^4].ToArray());
    }

    [Fact]
    public async Task Duplicate_configuration_is_allowed_but_replacement_is_terminal_to_the_protocol()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        await ConfigureAsync(publisher);
        await publisher.AcceptAsync(Message(8, AudioConfiguration, 100), CancellationToken.None);
        await publisher.AcceptAsync(Message(9, VideoConfiguration, 100), CancellationToken.None);
        Assert.Single(consumer.Packets);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(Message(8, [0xAF, 0, 0x11, 0x90]), CancellationToken.None));
        var changed = VideoConfiguration.ToArray();
        changed[^1] = 0xCF;
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(Message(9, changed), CancellationToken.None));
        Assert.Single(consumer.Packets);
    }

    [Theory]
    [InlineData(8, "2F001210")]
    [InlineData(8, "AF001238")]
    [InlineData(8, "AF001211")]
    [InlineData(8, "AF001690")]
    [InlineData(8, "AF005A10")]
    [InlineData(9, "1C000000000142001EFFE10002674201000268CE")]
    [InlineData(9, "17000000000242001EFFE10002674201000268CE")]
    [InlineData(9, "17000000000142001EFFE10002674201001068CE")]
    public async Task Unsupported_or_malformed_codec_configuration_never_publishes(int type, string hex)
    {
        var consumer = new ObsRtmpTestConsumer();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Create(consumer).AcceptAsync(Message((byte)type, Convert.FromHexString(hex)), CancellationToken.None));
        Assert.Empty(consumer.Packets);
    }

    [Theory]
    [InlineData(8, "AF0111")]
    [InlineData(9, "17010000000000000165")]
    public async Task Media_requires_its_sequence_header(int type, string hex)
    {
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Create(new()).AcceptAsync(Message((byte)type, Convert.FromHexString(hex)), CancellationToken.None));
    }

    [Theory]
    [InlineData("17010000000000001065")]
    [InlineData("170100000000000000")]
    [InlineData("170100000000000001E5")]
    [InlineData("1701000000")]
    public async Task Malformed_NALU_is_rejected_after_configuration(string hex)
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        await ConfigureAsync(publisher);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(Message(9, Convert.FromHexString(hex)), CancellationToken.None));
        Assert.Single(consumer.Packets);
    }

    [Fact]
    public async Task Bootstrap_budget_rejects_before_any_HEAD_delivery()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer, new ObsRtmpIngestOptions { MaximumBootstrapBytes = 64 });
        var metadata = ObsRtmpTestClient.Command("onMetaData",
            new Dictionary<string, object?> { ["description"] = new string('a', 80) });
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(Message(18, metadata), CancellationToken.None));
        Assert.Empty(consumer.Packets);
    }

    [Fact]
    public async Task Large_tag_is_sliced_into_bounded_DATA_packets_without_retaining_borrowed_memory()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer, new ObsRtmpIngestOptions
        {
            MaximumBootstrapBytes = 128,
            MaximumPacketBytes = 128,
        });
        await ConfigureAsync(publisher);
        await publisher.AcceptAsync(Message(9, Idr), CancellationToken.None);
        var body = Enumerable.Repeat((byte)0x22, 300).Prepend((byte)1).Prepend((byte)0xAF).ToArray();
        await publisher.AcceptAsync(Message(8, body), CancellationToken.None);
        var packets = consumer.Packets.Skip(2).ToArray();
        Assert.Equal(3, packets.Length);
        Assert.All(packets, packet => Assert.InRange(packet.Payload.Length, 1, 128));
        var tag = packets.SelectMany(packet => packet.Payload).ToArray();
        Assert.Equal(body, tag[11..^4]);
        Array.Fill(body, (byte)0xEE);
        Assert.Equal(0x22, tag[13]);
    }

    internal static ObsFlvPublishingSession Create(ObsRtmpTestConsumer consumer, ObsRtmpIngestOptions? options = null) =>
        new(Channel, Session, consumer, options ?? new());

    [Fact]
    public async Task Cancellation_after_an_accepted_DATA_slice_prevents_the_next_consumer_call()
    {
        using var cancellation = new CancellationTokenSource();
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = (packet, _) =>
            {
                if (packet.Sequence == 2)
                {
                    cancellation.Cancel();
                }

                return ValueTask.CompletedTask;
            },
        };
        var publisher = Create(consumer, new ObsRtmpIngestOptions
        {
            MaximumBootstrapBytes = 128,
            MaximumPacketBytes = 128,
        });
        await ConfigureAsync(publisher);
        await publisher.AcceptAsync(Message(9, Idr), cancellation.Token);
        var body = new byte[] { 0xAF, 1 }.Concat(new byte[300]).ToArray();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await publisher.AcceptAsync(Message(8, body), cancellation.Token));
        Assert.Equal(3, consumer.Packets.Count);
        Assert.Equal(128, consumer.Packets.Last().Payload.Length);
        Assert.Equal(3, publisher.PacketCount);
    }

    [Fact]
    public async Task AVC_end_marker_is_preserved_and_further_video_is_rejected()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = Create(consumer);
        await ConfigureAsync(publisher);
        await publisher.AcceptAsync(Message(9, Idr), CancellationToken.None);
        byte[] end = [0x17, 2, 0, 0, 0];
        await publisher.AcceptAsync(Message(9, end), CancellationToken.None);
        Assert.Equal(end, consumer.Packets.Last().Payload[11..^4]);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(Message(9, Idr), CancellationToken.None));
    }

    [Theory]
    [InlineData("170200000100")]
    [InlineData("2702000000")]
    [InlineData("1702000001")]
    public async Task Malformed_AVC_end_marker_is_rejected(string hex)
    {
        var publisher = Create(new());
        await ConfigureAsync(publisher);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(Message(9, Convert.FromHexString(hex)), CancellationToken.None));
    }

    [Fact]
    public async Task Existing_relay_fanout_replays_the_ingest_HEAD_then_forwards_owned_live_DATA()
    {
        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        var publishing = new ObsFlvPublishingSession(Channel, Session, fanout, new());
        await ConfigureAsync(publishing);
        var sink = new RelaySink();
        await using var child = fanout.AddChild(sink);
        await sink.WaitForCountAsync(1);
        await publishing.AcceptAsync(Message(9, Idr), CancellationToken.None);
        await publishing.AcceptAsync(Message(8, Audio), CancellationToken.None);
        await sink.WaitForCountAsync(3);
        var packets = sink.Packets.ToArray();
        Assert.True(packets[0].Replay);
        Assert.Equal(0x64616568U, packets[0].Type);
        Assert.Equal(new long[] { 0, 1, 2 }, packets.Select(packet => packet.Sequence));
        Assert.Equal(Idr, packets[1].Payload[11..^4]);
        Assert.Equal(Audio, packets[2].Payload[11..^4]);
    }

    private sealed class RelaySink : IPcpRelayChildSink
    {
        public ConcurrentQueue<(long Sequence, uint Type, bool Replay, byte[] Payload)> Packets { get; } = new();

        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Packets.Enqueue((packet.Sequence, packet.Type, packet.IsRetainedHeadReplay, packet.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Packets.Count < count)
            {
                await Task.Delay(5, deadline.Token);
            }
        }
    }

    internal static RtmpMessage Message(byte type, byte[] bytes, uint timestamp = 0) => new(type, 1, timestamp, bytes);

    internal static async Task ConfigureAsync(ObsFlvPublishingSession publisher)
    {
        await publisher.AcceptAsync(Message(9, VideoConfiguration), CancellationToken.None);
        await publisher.AcceptAsync(Message(8, AudioConfiguration), CancellationToken.None);
    }
}

internal sealed class ObsRtmpTestConsumer : IPcpChannelPacketConsumer
{
    public ConcurrentQueue<RecordedPacket> Packets { get; } = new();

    public Func<PcpChannelPacket, CancellationToken, ValueTask>? BeforeAccept { get; init; }

    public async ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
    {
        if (BeforeAccept is not null)
        {
            await BeforeAccept(packet, cancellationToken);
        }

        Packets.Enqueue(new(packet.ChannelId, packet.SessionId, packet.Sequence, packet.Type,
            packet.WirePosition, packet.StreamPosition, packet.Payload.ToArray()));
    }

    public async Task WaitForCountAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Packets.Count < count)
        {
            await Task.Delay(5, deadline.Token);
        }
    }

    internal sealed record RecordedPacket(
        ChannelId ChannelId, ChannelSessionId SessionId, long Sequence, uint Type,
        uint WirePosition, long? StreamPosition, byte[] Payload);
}

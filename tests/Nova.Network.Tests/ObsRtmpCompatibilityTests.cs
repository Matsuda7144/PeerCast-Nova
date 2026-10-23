using System.Buffers.Binary;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

/// <summary>
/// Nova-owned vectors for pinned OBS classic RTMP behavior; no OBS source is copied.
/// These tests establish managed compatibility, not actual OBS interoperability.
/// </summary>
public sealed class ObsRtmpCompatibilityTests
{
    [Fact]
    public async Task OBS_shape_pipeline_metadata_audio_first_and_shared_chunk_stream_preserve_media()
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        var metadata = Metadata();
        await client.SendAsync(18, 1, 0, metadata, id: 4);
        await ConfigureAsync(client);
        await client.SendCompressedAsync(9, 37, ObsFlvPublishingSessionTests.Idr);
        await client.SendCompressedAsync(8, 3, ObsFlvPublishingSessionTests.Audio);
        byte[] interframe = [0x27, 1, 0, 0, 4, 0, 0, 0, 1, 0x41];
        await client.SendCompressedAsync(9, 8, interframe);
        await client.SendCompressedAsync(8, 2, ObsFlvPublishingSessionTests.Audio);
        await StopAsync(client, host);

        var packets = consumer.Packets.ToArray();
        Assert.Equal(5, packets.Length);
        var retained = new PcpFlvBootstrapRetention();
        retained.Observe(packets[0].Payload);
        Assert.Equal(metadata[16..], retained.Snapshot().Metadata.Span[11..^4].ToArray());
        Assert.Equal(ObsFlvPublishingSessionTests.AudioConfiguration,
            retained.Snapshot().AudioConfiguration.Span[11..^4].ToArray());
        Assert.Equal(ObsFlvPublishingSessionTests.VideoConfiguration,
            retained.Snapshot().VideoConfiguration.Span[11..^4].ToArray());
        AssertTag(packets[1].Payload, 9, 37, ObsFlvPublishingSessionTests.Idr);
        AssertTag(packets[2].Payload, 8, 40, ObsFlvPublishingSessionTests.Audio);
        AssertTag(packets[3].Payload, 9, 48, interframe);
        AssertTag(packets[4].Payload, 8, 50, ObsFlvPublishingSessionTests.Audio);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFE }, packets[1].Payload[13..16]);
        Assert.Equal(0x17, packets[1].Payload[11]);
        Assert.Equal(0x27, packets[3].Payload[11]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Valid_unknown_metadata_is_ignored_before_and_after_bootstrap(int shape)
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer, maximumBootstrapBytes: 128);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        var ignored = shape switch
        {
            0 => ObsRtmpTestClient.Command("novaTelemetry",
                new Dictionary<string, object?> { ["detail"] = new string('x', 512) }),
            1 => ObsRtmpTestClient.Command("@setDataFrame", "novaTelemetry",
                new Dictionary<string, object?> { ["enabled"] = true }),
            2 => ObsRtmpTestClient.Command("onMetaData", null),
            _ => ObsRtmpTestClient.Command("onMetaData", new Dictionary<string, object?>(), "extra"),
        };
        await client.SendAsync(18, 1, 0, ignored, id: 4);
        await ConfigureAsync(client);
        await client.SendAsync(18, 1, 1, ignored, id: 4);
        await client.SendAsync(9, 1, 2, ObsFlvPublishingSessionTests.Idr, id: 4);
        await StopAsync(client, host);
        Assert.Equal(2, consumer.Packets.Count);
        var retained = new PcpFlvBootstrapRetention();
        retained.Observe(consumer.Packets.First().Payload);
        Assert.Empty(retained.Snapshot().Metadata.ToArray());
    }

    [Theory]
    [InlineData("02000A6F6E4D657461446174610800000001")]
    [InlineData("02000D40736574446174614672616D65")]
    [InlineData("003FF0000000000000")]
    [InlineData("020004746573740201")]
    [InlineData("02000474657374010102")]
    public async Task Malformed_metadata_fails_closed_even_when_the_event_is_unknown(string hex)
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        await client.SendAsync(18, 1, 0, Convert.FromHexString(hex), id: 4);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
        Assert.Empty(consumer.Packets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_metadata_is_rejected_by_AMF_or_message_budget(bool messageBudget)
    {
        var consumer = new ObsRtmpTestConsumer();
        var options = new ObsRtmpIngestOptions { Port = 0, MaximumMessageBytes = messageBudget ? 1024 : 100_000 };
        await using var host = new ObsRtmpIngestHost(
            ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, consumer, options);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        // Many individually valid, bounded strings exceed the aggregate AMF payload budget.
        var payload = ObsRtmpTestClient.Command(Enumerable.Repeat<object?>(new string('x', 4000), 17).ToArray());
        var wire = ObsRtmpTestClient.WireMessage(18, 1, 0, payload, id: 4, chunkBytes: 4096);
        // For message-length rejection no body is necessary; avoid writes racing the close.
        await client.SendWireAsync(messageBudget ? wire[..12] : wire);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected,
            (await host.Completion.WaitAsync(TimeSpan.FromSeconds(10))).EndReason);
        Assert.Empty(consumer.Packets);
    }

    [Fact]
    public async Task Shared_chunk_stream_compression_extended_delta_and_continuations_are_preserved()
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        await ConfigureAsync(client);
        await client.SendCompressedAsync(9, 0x01000001, ObsFlvPublishingSessionTests.Idr);
        var audio = new byte[5002];
        audio[0] = 0xAF;
        audio[1] = 1;
        Array.Fill(audio, (byte)0x33, 2, audio.Length - 2);
        await client.SendCompressedAsync(8, 0x01000002, audio);
        await client.SendCompressedAsync(8, 6, audio, format: 2);
        await client.SendCompressedAsync(8, 6, audio, format: 3);
        await StopAsync(client, host);
        var packets = consumer.Packets.ToArray();
        Assert.Equal(5, packets.Length);
        AssertTag(packets[1].Payload, 9, 0x01000001, ObsFlvPublishingSessionTests.Idr);
        AssertTag(packets[2].Payload, 8, 0x02000003, audio);
        AssertTag(packets[3].Payload, 8, 0x02000009, audio);
        AssertTag(packets[4].Payload, 8, 0x0200000F, audio);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EOF_discards_incomplete_body_without_fabricating_DATA_or_decoder_EOS(bool pendingBody)
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        await ConfigureAsync(client);
        await consumer.WaitForCountAsync(1);
        if (pendingBody)
        {
            var wire = ObsRtmpTestClient.WireMessage(9, 1, 37, new byte[5000], id: 4, chunkBytes: 4096);
            await client.SendWireAsync(wire[..4108]);
        }

        client.EndInput();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.Disconnected, result.EndReason);
        Assert.Equal(1, result.AcceptedPacketCount);
        Assert.Single(consumer.Packets);
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Explicit_stop_discards_other_incomplete_chunk_contexts_and_joins_session_tasks()
    {
        var consumer = new ObsRtmpTestConsumer();
        await using var host = Create(consumer);
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.LocalEndPoint);
        await PublishObsShapeAsync(client, host.LocalEndPoint.Port);
        await ConfigureAsync(client);
        var wire = ObsRtmpTestClient.WireMessage(9, 1, 37, new byte[5000], id: 4, chunkBytes: 4096);
        await client.SendWireAsync(wire[..4108]);
        await StopAsync(client, host);
        Assert.Single(consumer.Packets);
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Sender_baseline_and_backward_DTS_are_preserved_without_inventing_discontinuity()
    {
        var consumer = new ObsRtmpTestConsumer
        {
            BeforeAccept = (packet, _) =>
            {
                Assert.Null(packet.Discontinuity);
                return ValueTask.CompletedTask;
            },
        };
        var publisher = ObsFlvPublishingSessionTests.Create(consumer);
        await ObsFlvPublishingSessionTests.ConfigureAsync(publisher);
        await publisher.AcceptAsync(new(9, 1, 37, ObsFlvPublishingSessionTests.Idr), CancellationToken.None);
        await publisher.AcceptAsync(new(8, 1, 2, ObsFlvPublishingSessionTests.Audio), CancellationToken.None);
        var packets = consumer.Packets.ToArray();
        AssertTag(packets[1].Payload, 9, 37, ObsFlvPublishingSessionTests.Idr);
        AssertTag(packets[2].Payload, 8, 2, ObsFlvPublishingSessionTests.Audio);
        // Timestamp policy and downstream decoder resets belong to the channel owner.
    }

    private static async Task PublishObsShapeAsync(ObsRtmpTestClient client, int port)
    {
        await client.SetChunkBytesAsync(4096);
        await client.SendCommandAsync(0, "connect", 1.0, new Dictionary<string, object?>
        {
            ["app"] = "live",
            ["type"] = "nonprivate",
            ["flashVer"] = "FMLE/3.0 (compatible; Nova test)",
            ["swfUrl"] = $"rtmp://127.0.0.1:{port}/live",
            ["tcUrl"] = $"rtmp://127.0.0.1:{port}/live",
        });
        var window = await client.ReadAsync();
        Assert.Equal(5, window.Type);
        Assert.Equal(0U, window.StreamId);
        Assert.Equal(2_500_000U, BinaryPrimitives.ReadUInt32BigEndian(window.Payload));
        var bandwidth = await client.ReadAsync();
        Assert.Equal(6, bandwidth.Type);
        Assert.Equal(new byte[] { 0, 0x26, 0x25, 0xA0, 2 }, bandwidth.Payload);
        AssertResult(await client.ReadAsync(), 1.0, "NetConnection.Connect.Success");
        // OBS queues neither preliminary call and sends createStream immediately.
        await client.SendCompressedAsync(20, 0, ObsRtmpTestClient.Command("releaseStream", 2.0, null, "nova"), id: 3);
        await client.SendCompressedAsync(20, 0, ObsRtmpTestClient.Command("FCPublish", 3.0, null, "nova"), id: 3);
        await client.SendCompressedAsync(20, 0, ObsRtmpTestClient.Command("createStream", 4.0, null), id: 3);
        AssertResult(await client.ReadAsync(), 2.0, null);
        AssertResult(await client.ReadAsync(), 3.0, null);
        var created = await client.ReadAsync();
        AssertResult(created, 4.0, null);
        Assert.Equal(1.0, RtmpAmf0.Decode(created.Payload)[3]);
        await client.SendAsync(20, 1, 0, ObsRtmpTestClient.Command("publish", 5.0, null, "nova", "live"), id: 4);
        var begin = await client.ReadAsync();
        Assert.Equal(4, begin.Type);
        Assert.Equal(0U, begin.StreamId);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 1 }, begin.Payload);
        var status = await client.ReadAsync();
        Assert.Equal(20, status.Type);
        Assert.Equal(1U, status.StreamId);
        var values = RtmpAmf0.Decode(status.Payload);
        Assert.Equal("onStatus", values[0]);
        Assert.Equal(0.0, values[1]);
        Assert.Null(values[2]);
        var properties = Assert.IsType<Dictionary<string, object?>>(values[3]);
        Assert.Equal("status", properties["level"]);
        Assert.Equal("NetStream.Publish.Start", properties["code"]);
    }

    private static void AssertResult(RtmpMessage message, double transaction, string? status)
    {
        Assert.Equal(20, message.Type);
        Assert.Equal(0U, message.StreamId);
        var values = RtmpAmf0.Decode(message.Payload);
        Assert.Equal(4, values.Count);
        Assert.Equal("_result", values[0]);
        Assert.Equal(transaction, values[1]);
        if (status is not null)
        {
            var properties = Assert.IsType<Dictionary<string, object?>>(values[3]);
            Assert.Equal(status, properties["code"]);
            Assert.Equal("status", properties["level"]);
            Assert.Equal(0.0, properties["objectEncoding"]);
        }
        else
        {
            Assert.Null(values[2]);
        }
    }

    private static async Task ConfigureAsync(ObsRtmpTestClient client)
    {
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration, id: 4);
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration, id: 4);
    }

    private static async Task StopAsync(ObsRtmpTestClient client, ObsRtmpIngestHost host)
    {
        // One write avoids racing Nova's first terminal command with the second send.
        var unpublish = ObsRtmpTestClient.WireCompressed(20, 0,
            ObsRtmpTestClient.Command("FCUnpublish", 6.0, null, "nova"), 1, 3, 4096);
        var delete = ObsRtmpTestClient.WireCompressed(20, 0,
            ObsRtmpTestClient.Command("deleteStream", 7.0, null, 1.0), 1, 3, 4096);
        await client.SendWireAsync(unpublish.Concat(delete).ToArray());
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsRtmpIngestEndReason.PublisherStopped, result.EndReason);
    }

    private static byte[] Metadata()
    {
        var properties = new Dictionary<string, object?>
        {
            ["duration"] = 0.0,
            ["fileSize"] = 0.0,
            ["width"] = 640.0,
            ["height"] = 360.0,
            ["videocodecid"] = 7.0,
            ["videodatarate"] = 600.0,
            ["framerate"] = 24.0,
            ["audiocodecid"] = 10.0,
            ["audiodatarate"] = 96.0,
            ["audiosamplerate"] = 44100.0,
            ["audiosamplesize"] = 16.0,
            ["audiochannels"] = 2.0,
            ["stereo"] = true,
            ["2.1"] = false,
            ["3.1"] = false,
            ["4.0"] = false,
            ["4.1"] = false,
            ["5.1"] = false,
            ["7.1"] = false,
            ["encoder"] = "Nova managed compatibility vector",
        };
        var names = ObsRtmpTestClient.Command("@setDataFrame", "onMetaData");
        var body = ObsRtmpTestClient.Command(properties);
        using var output = new MemoryStream();
        output.Write(names);
        output.WriteByte(8); // ECMA array, terminated by object-end rather than its advisory count.
        output.Write(new byte[] { 0, 0, 0, 20 });
        output.Write(body.AsSpan(1));
        return output.ToArray();
    }

    private static void AssertTag(byte[] tag, byte type, uint timestamp, byte[] payload)
    {
        Assert.Equal(type, tag[0]);
        var actualTime = (uint)((tag[7] << 24) | (tag[4] << 16) | (tag[5] << 8) | tag[6]);
        Assert.Equal(timestamp, actualTime);
        Assert.Equal(payload.Length, (tag[1] << 16) | (tag[2] << 8) | tag[3]);
        Assert.Equal(new byte[] { 0, 0, 0 }, tag[8..11]);
        Assert.Equal(payload, tag[11..^4]);
        Assert.Equal((uint)(payload.Length + 11), BinaryPrimitives.ReadUInt32BigEndian(tag.AsSpan(tag.Length - 4)));
    }

    private static ObsRtmpIngestHost Create(ObsRtmpTestConsumer consumer, int maximumBootstrapBytes = 65536) =>
        new(ObsFlvPublishingSessionTests.Channel, ObsFlvPublishingSessionTests.Session, consumer,
            new ObsRtmpIngestOptions { Port = 0, MaximumBootstrapBytes = maximumBootstrapBytes });
}

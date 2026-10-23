using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Tests;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class HeadlessPublishRoundtripTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RTMP_owner_existing_PCP_relay_HTTP_and_parser_roundtrip_with_early_or_late_viewer(
        bool lateJoin, bool sbrAbsentExtension)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        await using var http = new PcpHttpFlvPlaybackHost();
        http.Start();
        var registry = new RecordingStreamSourceLeaseRegistry();
        await using var host = new ObsPublishChannelHost(
            new ObsPublishChannelOptions { Ingest = new() { Port = 0 } },
            httpHost: http, recordingRegistry: registry);
        host.Start(token);
        Assert.True(host.IsRegistered);
        Assert.Equal(1, http.ActiveSourceCount);
        Assert.Equal(1, registry.Count);
        using var publisher = new ObsRtmpTestClient();
        await publisher.ConnectAsync(host.IngestEndPoint);
        await publisher.SetChunkBytesAsync(4096);
        await publisher.PublishAsync();
        byte[] videoConfig = new byte[] { 0x17, 0, 0, 0, 0 }
            .Concat(PlaybackBoundedFlvParserTests.BuildAvcConfiguration()).ToArray();
        // Synthetic managed 5-byte ASC compatibility fixture, not retained real OBS bytes.
        byte[] audioConfig = sbrAbsentExtension
            ? [0xAF, 0, 0x11, 0x90, 0x56, 0xE5, 0]
            : [0xAF, 0, 0x11, 0x90];
        var metadata = ObsRtmpTestClient.Command("@setDataFrame", "onMetaData",
            new Dictionary<string, object?> { ["width"] = 320.0 });
        var expectedHead = new byte[] { 0x46, 0x4C, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0 }
            .Concat(Tag(18, 0, metadata[16..])).Concat(Tag(9, 0, videoConfig)).Concat(Tag(8, 0, audioConfig)).ToArray();
        byte[] video = [0x17, 1, 0xFF, 0xFF, 0xFE, 0, 0, 0, 2, 0x65, 0x88];
        byte[] audio = [0xAF, 1, 0x11, 0x22, 0x33];

        if (lateJoin)
        {
            await ConfigureAsync();
            await publisher.SendAsync(9, 1, 0, video, id: 4);
            await publisher.SendAsync(8, 1, 1, audio, id: 4);
            var previous = expectedHead.Length + Tag(9, 0, video).Length + Tag(8, 1, audio).Length;
            while ((host.Snapshot.StreamPosition ?? -1) < previous) await Task.Delay(5, token);
        }

        await using var relayConnection = await new TcpTransportConnector().ConnectAsync(
            host.RelayEndPoint.Address.ToString(), host.RelayEndPoint.Port, token);
        var relayReader = PipeReader.Create(relayConnection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var handshake = await PcpRelayHandshakeClient.NegotiateAsync(
                relayConnection, relayReader, host.RelayEndPoint.Address.ToString(), host.RelayEndPoint.Port,
                host.ChannelId.Value, Guid.NewGuid(), 0, 1,
                new PcpRelayHandshakeSettings(32 * 1024, 64 * 1024, 16, "Nova/PublishTest",
                    PcpHeloCodec.LegacyServantVersion), token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);
            using var viewer = new TcpClient();
            var httpEndpoint = new Uri(host.HttpStreamUrl);
            await viewer.ConnectAsync(httpEndpoint.Host, httpEndpoint.Port, token);
            var stream = viewer.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"GET {httpEndpoint.AbsolutePath} HTTP/1.1\r\nHost: localhost\r\n\r\n"), token);
            if (!lateJoin) await ConfigureAsync();
            Assert.True(await host.HeadAvailable.WaitAsync(token));
            var header = new List<byte>();
            while (header.Count < 4 || !header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 }))
            {
                var one = new byte[1];
                await stream.ReadExactlyAsync(one, token);
                header.Add(one[0]);
            }
            Assert.StartsWith("HTTP/1.1 200 OK", Encoding.ASCII.GetString(header.ToArray()));
            var receivedHead = new byte[expectedHead.Length];
            await stream.ReadExactlyAsync(receivedHead, token);
            Assert.Equal(expectedHead, receivedHead);
            await using var atoms = new PcpAtomPipeMessageReader(relayReader, maxBufferedBytes: 1024 * 1024);
            await AssertRelayAsync(0x64616568, expectedHead);
            const uint timestamp = 0x01000040;
            await publisher.SendAsync(9, 1, timestamp, video, id: 4);
            await publisher.SendAsync(8, 1, timestamp + 1, audio, id: 4);
            var videoTag = Tag(9, timestamp, video);
            var audioTag = Tag(8, timestamp + 1, audio);
            await AssertRelayAsync(0x61746164, videoTag);
            await AssertRelayAsync(0x61746164, audioTag);
            await publisher.SendCommandAsync(0, "FCUnpublish", 6.0, null, "nova");
            using var body = new MemoryStream();
            body.Write(receivedHead);
            await stream.CopyToAsync(body, token); // Publisher stop must terminate the HTTP source.
            var expectedBody = expectedHead.Concat(videoTag).Concat(audioTag).ToArray();
            Assert.Equal(expectedBody, body.ToArray());
            var result = await host.Completion.WaitAsync(token);
            Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
            Assert.Equal(lateJoin ? 5 : 3, result.AcceptedPacketCount);
            Assert.Equal(1, result.HeadCount);
            Assert.Equal(lateJoin ? 4 : 2, result.DataCount);
            Assert.Equal(PcpRelayDrainOutcome.Completed, result.RelayDrain);
            Assert.True(result.HttpDrained);
            Assert.True(result.ShutdownCompleted);
            Assert.False(host.IsRegistered);
            Assert.Equal(0, registry.Count);
            Assert.Equal(0, http.ActiveSourceCount);
            using var missing = new HttpClient();
            Assert.Equal(System.Net.HttpStatusCode.NotFound,
                (await missing.GetAsync(host.HttpStreamUrl, token)).StatusCode);
            using var parser = new PlaybackBoundedFlvParser();
            var bootstrap = parser.Parse(body.ToArray());
            using var bootstrapCompletion = bootstrap.Completion;
            Assert.Equal(PlaybackBoundedFlvParserResult.StreamDescriptionAvailable, bootstrap.Result);
            Assert.Equal(320, parser.Session!.Video.Width);
            Assert.Equal(240, parser.Session.Video.Height);
            Assert.Equal(videoConfig[5..], parser.Session.Video.CodecExtradata.ToArray());
            Assert.Equal(audioConfig[2..], parser.Session.Audio.CodecExtradata.ToArray());
            Assert.Equal(48_000, parser.Session.Audio.SampleRate);
            Assert.Equal(2, parser.Session.Audio.ChannelCount);
            var remaining = body.ToArray().AsMemory(bootstrap.ConsumedByteCount);
            var events = new List<(byte[] Bytes, TimeSpan Dts, TimeSpan Pts, PlaybackBoundedDemuxKeyframeState Keyframe)>();
            while (!remaining.IsEmpty)
            {
                var parsed = parser.Parse(remaining);
                Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, parsed.Result);
                Assert.True(parsed.ConsumedByteCount > 0);
                using var completion = parsed.Completion;
                foreach (var media in completion!.Events)
                    events.Add((media.Payload.ToArray(), media.DecodeTimestamp.Value,
                        media.PresentationTimestamp.Value, media.KeyframeState));
                remaining = remaining[parsed.ConsumedByteCount..];
            }
            Assert.Equal(2, events.Count);
            Assert.Equal(video[5..], events[0].Bytes);
            Assert.Equal(audio[2..], events[1].Bytes);
            Assert.Equal(TimeSpan.FromMilliseconds(timestamp), events[0].Dts);
            Assert.Equal(TimeSpan.FromMilliseconds(timestamp - 2), events[0].Pts);
            Assert.Equal(PlaybackBoundedDemuxKeyframeState.Keyframe, events[0].Keyframe);
            Assert.Equal(TimeSpan.FromMilliseconds(timestamp + 1), events[1].Dts);
            output.WriteLine($"lateJoin={lateJoin}; ASC={audioConfig.Length - 2}; accepted={result.AcceptedPacketCount}; HEAD={result.HeadCount}; " +
                $"DATA={result.DataCount}; bytes={result.AcceptedByteCount}; HTTP={body.Length}; " +
                $"relayDelivered={result.Relay!.Snapshot.DeliveredPacketCount}; parser=2 exact packets; " +
                $"completion={result.EndReason}; unregister=True; REAL OBS INTEROPERABILITY NOT YET PROVEN");

            async Task AssertRelayAsync(uint type, byte[] expected)
            {
                var channel = await atoms.ReadNextAsync(token);
                Assert.Equal(host.ChannelId.Value,
                    PcpWireValueCodec.DecodeGuid(channel.FindFirst(PcpKnownAtomNames.ChannelId)!.Content.Span));
                var packet = channel.FindFirst(PcpKnownAtomNames.ChannelPacket)!;
                Assert.Equal(type, PcpWireValueCodec.DecodeUInt32(packet.FindFirst(PcpKnownAtomNames.Type)!.Content.Span));
                Assert.Equal(expected, packet.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
            }
        }
        finally
        {
            await relayReader.CompleteAsync();
        }

        async Task ConfigureAsync()
        {
            await publisher.SendAsync(18, 1, 0, metadata, id: 4);
            await publisher.SendAsync(8, 1, 0, audioConfig, id: 4);
            await publisher.SendAsync(9, 1, 0, videoConfig, id: 4);
        }
    }

    private static byte[] Tag(byte type, uint timestamp, byte[] payload)
    {
        var bytes = new byte[15 + payload.Length];
        bytes[0] = type;
        bytes[1] = (byte)(payload.Length >> 16);
        bytes[2] = (byte)(payload.Length >> 8);
        bytes[3] = (byte)payload.Length;
        bytes[4] = (byte)(timestamp >> 16);
        bytes[5] = (byte)(timestamp >> 8);
        bytes[6] = (byte)timestamp;
        bytes[7] = (byte)(timestamp >> 24);
        payload.CopyTo(bytes, 11);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 4), (uint)(payload.Length + 11));
        return bytes;
    }
}

using System.Net.Sockets;
using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsRtmpRejectionDiagnosticTests
{
    private const string Secret = "PRIVATE-STREAM-KEY-EXAMPLE";

    [Theory]
    [InlineData("missing-context", 0, ObsRtmpRejectionStage.RtmpChunkHeader, ObsRtmpRejectionReason.MissingChunkStreamContext)]
    [InlineData("chunk-limit", 0, ObsRtmpRejectionStage.RtmpChunkHeader, ObsRtmpRejectionReason.ChunkStreamLimit)]
    [InlineData("interrupted", 0, ObsRtmpRejectionStage.RtmpChunkAssembly, ObsRtmpRejectionReason.MessageInterrupted)]
    [InlineData("extended-time", 0, ObsRtmpRejectionStage.RtmpChunkHeader, ObsRtmpRejectionReason.ExtendedTimestampMismatch)]
    [InlineData("message-limit", 0, ObsRtmpRejectionStage.RtmpChunkAssembly, ObsRtmpRejectionReason.MessageSizeLimit)]
    [InlineData("zero-length", 0, ObsRtmpRejectionStage.RtmpChunkAssembly, ObsRtmpRejectionReason.InvalidMessageLength)]
    [InlineData("buffer-limit", 0, ObsRtmpRejectionStage.RtmpChunkAssembly, ObsRtmpRejectionReason.BufferedBytesLimit)]
    [InlineData("chunk-size", 0, ObsRtmpRejectionStage.RtmpControlMessage, ObsRtmpRejectionReason.UnsupportedChunkSize)]
    [InlineData("control-stream", 0, ObsRtmpRejectionStage.RtmpControlMessage, ObsRtmpRejectionReason.InvalidMessageStreamId)]
    [InlineData("control-shape", 0, ObsRtmpRejectionStage.RtmpControlMessage, ObsRtmpRejectionReason.MalformedControlMessage)]
    [InlineData("window-zero", 0, ObsRtmpRejectionStage.RtmpControlMessage, ObsRtmpRejectionReason.ZeroAcknowledgementWindow)]
    [InlineData("user-control", 0, ObsRtmpRejectionStage.RtmpControlMessage, ObsRtmpRejectionReason.UnsupportedUserControlEvent)]
    [InlineData("amf-unsupported", 0, ObsRtmpRejectionStage.AmfDecode, ObsRtmpRejectionReason.UnsupportedAmfType)]
    [InlineData("amf-truncated", 0, ObsRtmpRejectionStage.AmfDecode, ObsRtmpRejectionReason.TruncatedAmfValue)]
    [InlineData("transaction", 0, ObsRtmpRejectionStage.AmfCommand, ObsRtmpRejectionReason.MalformedCommandEnvelope)]
    [InlineData("connect", 0, ObsRtmpRejectionStage.ConnectCommand, ObsRtmpRejectionReason.InvalidConnect)]
    [InlineData("create", 1, ObsRtmpRejectionStage.CreateStreamCommand, ObsRtmpRejectionReason.InvalidCreateStream)]
    [InlineData("publish-mode", 2, ObsRtmpRejectionStage.PublishCommand, ObsRtmpRejectionReason.InvalidPublish)]
    [InlineData("publish-route", 2, ObsRtmpRejectionStage.PublishCommand, ObsRtmpRejectionReason.InvalidPublishRoute)]
    [InlineData("unknown-command", 1, ObsRtmpRejectionStage.AmfCommand, ObsRtmpRejectionReason.UnsupportedCommand)]
    [InlineData("command-sequence", 0, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.CommandOutOfSequence)]
    [InlineData("duplicate-create", 3, ObsRtmpRejectionStage.CreateStreamCommand, ObsRtmpRejectionReason.InvalidCreateStream)]
    [InlineData("duplicate-publish", 3, ObsRtmpRejectionStage.PublishCommand, ObsRtmpRejectionReason.InvalidPublish)]
    [InlineData("release-after-publish", 3, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.DuplicatePublish)]
    [InlineData("invalid-delete", 3, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.InvalidDeleteStream)]
    [InlineData("invalid-close", 3, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.InvalidCloseStream)]
    [InlineData("inactive-unpublish", 1, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.PublisherNotActive)]
    [InlineData("bandwidth-check", 1, ObsRtmpRejectionStage.AmfCommand, ObsRtmpRejectionReason.InvalidBandwidthCheck)]
    [InlineData("media-stream", 3, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.MediaOutsidePublishedStream)]
    [InlineData("message-type", 0, ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.UnsupportedMessageType)]
    [InlineData("metadata-envelope", 3, ObsRtmpRejectionStage.Metadata, ObsRtmpRejectionReason.MalformedMetadataEnvelope)]
    [InlineData("metadata-secret-envelope", 3, ObsRtmpRejectionStage.Metadata, ObsRtmpRejectionReason.MalformedMetadataEnvelope)]
    [InlineData("metadata-amf", 3, ObsRtmpRejectionStage.AmfDecode, ObsRtmpRejectionReason.TruncatedAmfValue)]
    [InlineData("metadata-oversize", 3, ObsRtmpRejectionStage.AmfDecode, ObsRtmpRejectionReason.AmfPayloadLimit)]
    [InlineData("metadata-bootstrap", 3, ObsRtmpRejectionStage.FlvBootstrap, ObsRtmpRejectionReason.BootstrapSizeLimit)]
    [InlineData("avc-envelope", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.UnsupportedVideoPacket)]
    [InlineData("avc-sequence", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.MalformedAvcSequenceHeader)]
    [InlineData("avc-config", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.InvalidAvcConfiguration)]
    [InlineData("avc-extension", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.UnsupportedAvcConfigurationExtension)]
    [InlineData("avc-missing-sets", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.MissingAvcParameterSets)]
    [InlineData("avc-truncated-set", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.TruncatedAvcParameterSet)]
    [InlineData("avc-invalid-set", 3, ObsRtmpRejectionStage.AvcSequenceHeader, ObsRtmpRejectionReason.InvalidAvcParameterSet)]
    [InlineData("aac-envelope", 3, ObsRtmpRejectionStage.AacSequenceHeader, ObsRtmpRejectionReason.UnsupportedAudioPacket)]
    [InlineData("aac-length", 3, ObsRtmpRejectionStage.AacSequenceHeader, ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig)]
    [InlineData("aac-object", 3, ObsRtmpRejectionStage.AacSequenceHeader, ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig)]
    [InlineData("aac-frequency", 3, ObsRtmpRejectionStage.AacSequenceHeader, ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig)]
    [InlineData("aac-channels", 3, ObsRtmpRejectionStage.AacSequenceHeader, ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig)]
    [InlineData("audio-before-config", 3, ObsRtmpRejectionStage.MediaPacket, ObsRtmpRejectionReason.MissingAudioConfiguration)]
    [InlineData("video-before-config", 3, ObsRtmpRejectionStage.MediaPacket, ObsRtmpRejectionReason.MissingVideoConfiguration)]
    [InlineData("replacement", 4, ObsRtmpRejectionStage.FlvBootstrap, ObsRtmpRejectionReason.CodecReplacement)]
    [InlineData("nalu-length", 4, ObsRtmpRejectionStage.MediaPacket, ObsRtmpRejectionReason.InvalidNaluLength)]
    [InlineData("nalu-body", 4, ObsRtmpRejectionStage.MediaPacket, ObsRtmpRejectionReason.InvalidNaluBody)]
    [InlineData("nalu-empty", 4, ObsRtmpRejectionStage.MediaPacket, ObsRtmpRejectionReason.EmptyNaluMessage)]
    [InlineData("avc-end", 4, ObsRtmpRejectionStage.MediaPacket, ObsRtmpRejectionReason.MalformedAvcEndMarker)]
    public async Task Managed_rejection_preserves_stage_reason_through_owner_completion(
        string fixture, int preparation, ObsRtmpRejectionStage stage, ObsRtmpRejectionReason reason)
    {
        var ingest = new ObsRtmpIngestOptions { Port = 0 };
        if (fixture == "chunk-limit") ingest = ingest with { MaximumChunkStreams = 1 };
        if (fixture == "buffer-limit") ingest = ingest with { MaximumMessageBytes = 160, MaximumBufferedBytes = 200 };
        if (fixture == "metadata-bootstrap") ingest = ingest with { MaximumBootstrapBytes = 64 };
        await using var host = new ObsPublishChannelHost(new() { Ingest = ingest });
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.IngestEndPoint);
        if (preparation >= 3)
        {
            await client.PublishAsync();
            if (preparation == 4)
            {
                await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration);
                await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration);
                Assert.True(await host.HeadAvailable.WaitAsync(TimeSpan.FromSeconds(10)));
            }
        }
        else if (preparation >= 1)
        {
            await ConnectCommandAsync(client);
            if (preparation == 2)
            {
                await client.SendCommandAsync(0, "createStream", 2.0, null);
                await client.ReadAsync();
            }
        }

        await RejectAsync(client, fixture);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.ProtocolRejected, result.EndReason);
        Assert.Equal(ObsRtmpIngestEndReason.ProtocolRejected, result.Ingest!.EndReason);
        var diagnostic = Assert.IsType<ObsRtmpRejectionDiagnostic>(result.Rejection);
        Assert.Same(result.Ingest.Rejection, diagnostic);
        Assert.Equal(stage, diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.True(result.ShutdownCompleted);
        Assert.False(host.IsRegistered);
        Assert.Equal(ChannelSessionState.Stopped, host.Snapshot.State);
        if (fixture is "missing-context" or "message-limit" or "buffer-limit")
        {
            Assert.Equal(6 + (fixture == "buffer-limit" ? 1 : 0), diagnostic.Context.ChunkStreamId);
            Assert.Equal(0, diagnostic.Context.AssembledPayloadLength);
        }
        if (fixture == "aac-length")
        {
            Assert.Equal((byte)8, diagnostic.Context.MessageType);
            Assert.Equal(6, diagnostic.Context.ChunkStreamId);
            Assert.Equal(1U, diagnostic.Context.MessageStreamId);
            Assert.Equal(5, diagnostic.Context.ConfigLength);
            Assert.Equal(7, diagnostic.Context.DeclaredPayloadLength);
            Assert.Equal(7, diagnostic.Context.AssembledPayloadLength);
            Assert.Equal(ObsFlvBootstrapState.None, diagnostic.Context.BootstrapState);
        }
        if (fixture == "unknown-command") Assert.Equal(ObsRtmpDiagnosticToken.Unknown, diagnostic.Context.CommandOrEvent);
        if (fixture == "metadata-secret-envelope") Assert.Equal(ObsRtmpDiagnosticToken.SetDataFrame, diagnostic.Context.CommandOrEvent);
        if (fixture == "connect") Assert.Equal(ObsRtmpDiagnosticToken.Connect, diagnostic.Context.CommandOrEvent);
        if (fixture == "replacement")
            Assert.True(diagnostic.Context.BootstrapState!.Value.HasFlag(ObsFlvBootstrapState.HeadPublished));
        var json = JsonSerializer.Serialize(result);
        Assert.Contains(stage.ToString(), json, StringComparison.Ordinal);
        Assert.Contains(reason.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Payload\":", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, ObsRtmpRejectionReason.UnsupportedHandshakeVersion)]
    [InlineData(true, ObsRtmpRejectionReason.InvalidHandshakeEcho)]
    public async Task Handshake_rejection_is_retained_without_handshake_bytes(
        bool corruptEcho, ObsRtmpRejectionReason expected)
    {
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } });
        host.Start();
        using var client = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(host.IngestEndPoint, deadline.Token);
        var stream = client.GetStream();
        var hello = new byte[1537];
        hello[0] = corruptEcho ? (byte)3 : (byte)2;
        await stream.WriteAsync(hello, deadline.Token);
        if (corruptEcho)
        {
            var reply = new byte[3073];
            await stream.ReadExactlyAsync(reply, deadline.Token);
            var echo = reply.AsSpan(1, 1536).ToArray();
            echo[8] ^= 0xFF;
            await stream.WriteAsync(echo, deadline.Token);
        }
        var result = await host.Completion.WaitAsync(deadline.Token);
        Assert.Equal(ObsPublishChannelEndReason.ProtocolRejected, result.EndReason);
        Assert.Equal(ObsRtmpRejectionStage.RtmpHandshake, result.Rejection!.Stage);
        Assert.Equal(expected, result.Rejection.Reason);
        Assert.Null(result.Rejection.Context.MessageType);
        Assert.Same(result.Rejection, result.Ingest!.Rejection);
    }

    [Theory]
    [InlineData("00FFF0000000000000", ObsRtmpRejectionReason.NonFiniteAmfNumber)]
    [InlineData("0102", ObsRtmpRejectionReason.InvalidAmfBoolean)]
    [InlineData("0A00000081", ObsRtmpRejectionReason.AmfArrayLimit)]
    [InlineData("021001", ObsRtmpRejectionReason.AmfStringLimit)]
    [InlineData("020001FF", ObsRtmpRejectionReason.InvalidAmfUtf8)]
    [InlineData("030001610500016105000009", ObsRtmpRejectionReason.DuplicateAmfProperty)]
    [InlineData("1100", ObsRtmpRejectionReason.UnsupportedAmfType)]
    [InlineData("02000261", ObsRtmpRejectionReason.TruncatedAmfValue)]
    public void AMF_decode_sites_have_stable_identifiers(string hex, ObsRtmpRejectionReason expected)
    {
        var exception = Assert.Throws<InvalidDataException>(() => RtmpAmf0.Decode(Convert.FromHexString(hex)));
        var diagnostic = ObsRtmpRejection.Get(exception);
        Assert.Equal(ObsRtmpRejectionStage.AmfDecode, diagnostic.Stage);
        Assert.Equal(expected, diagnostic.Reason);
        Assert.Equal(hex.Length / 2, diagnostic.Context.AssembledPayloadLength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMF_depth_and_value_limits_remain_bounded(bool depth)
    {
        byte[] body = depth ? Convert.FromHexString("030001610300016103000161030001610300016105") :
            Enumerable.Repeat((byte)5, 257).ToArray();
        var exception = Assert.Throws<InvalidDataException>(() => RtmpAmf0.Decode(body));
        Assert.Equal(ObsRtmpRejectionReason.AmfValueOrDepthLimit, ObsRtmpRejection.Get(exception).Reason);
    }

    [Fact]
    public void First_rejection_wins_and_context_enrichment_never_replaces_known_values()
    {
        var exception = ObsRtmpRejection.Create(ObsRtmpRejectionStage.AacSequenceHeader,
            ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig, "fixed validator text",
            new() { ConfigLength = 5 });
        var first = ObsRtmpRejection.Get(exception);
        ObsRtmpRejection.Enrich(exception, new() { ConfigLength = 2, ChunkStreamId = 4 });
        var last = ObsRtmpRejection.Get(exception);
        Assert.Equal(first.Stage, last.Stage);
        Assert.Equal(first.Reason, last.Reason);
        Assert.Equal(5, last.Context.ConfigLength);
        Assert.Equal(4, last.Context.ChunkStreamId);
        Assert.Null(first.Context.ChunkStreamId);
        Assert.Single(exception.Data);
    }

    [Fact]
    public async Task Valid_OBS_shaped_input_and_clean_stop_have_no_rejection()
    {
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } });
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.IngestEndPoint);
        await client.PublishAsync();
        await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.AudioConfiguration);
        await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration);
        await client.SendAsync(9, 1, 42, ObsFlvPublishingSessionTests.Idr);
        await client.SendCommandAsync(0, "FCUnpublish", 5.0, null, "nova");
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.PublisherStopped, result.EndReason);
        Assert.Null(result.Rejection);
        Assert.Null(result.Ingest!.Rejection);
        Assert.Equal(1, result.HeadCount);
        Assert.Equal(1, result.DataCount);
    }

    private static async Task ConnectCommandAsync(ObsRtmpTestClient client)
    {
        await client.SendCommandAsync(0, "connect", 1.0,
            new Dictionary<string, object?> { ["app"] = "live" });
        await client.ReadAsync();
        await client.ReadAsync();
        await client.ReadAsync();
    }

    private static async Task RejectAsync(ObsRtmpTestClient client, string fixture)
    {
        var video = ObsFlvPublishingSessionTests.VideoConfiguration.ToArray();
        switch (fixture)
        {
            case "missing-context": await client.SendWireAsync([0x46]); break;
            case "chunk-limit":
                await client.SendAsync(3, 0, 0, [0, 0, 0, 0], id: 2);
                await client.SendAsync(3, 0, 0, [0, 0, 0, 0], id: 3);
                break;
            case "interrupted":
                await client.SendWireAsync(ObsRtmpTestClient.WireMessage(9, 1, 0, new byte[130])[..140]
                    .Concat(ObsRtmpTestClient.WireMessage(9, 1, 0, [1])).ToArray());
                break;
            case "extended-time":
                var wire = ObsRtmpTestClient.WireMessage(9, 1, 0x01000000, new byte[130]);
                wire[148] = 1;
                await client.SendWireAsync(wire);
                break;
            case "buffer-limit":
                await client.SendWireAsync(ObsRtmpTestClient.WireMessage(9, 1, 0, new byte[130])[..140]
                    .Concat(ObsRtmpTestClient.WireMessage(9, 1, 0, new byte[130], id: 7)[..12]).ToArray());
                break;
            case "message-limit":
            case "zero-length":
                var header = ObsRtmpTestClient.WireMessage(9, 1, 0, [1])[..12];
                header[4] = fixture == "message-limit" ? (byte)0x10 : (byte)0;
                header[6] = fixture == "message-limit" ? (byte)1 : (byte)0;
                await client.SendWireAsync(header);
                break;
            case "chunk-size": await client.SendAsync(1, 0, 0, [0, 0, 0, 0], id: 2); break;
            case "control-stream": await client.SendAsync(1, 1, 0, [0, 0, 0, 128], id: 2); break;
            case "control-shape": await client.SendAsync(1, 0, 0, [0], id: 2); break;
            case "window-zero": await client.SendAsync(5, 0, 0, [0, 0, 0, 0], id: 2); break;
            case "user-control": await client.SendAsync(4, 0, 0, [0, 1], id: 2); break;
            case "amf-unsupported": await client.SendAsync(20, 0, 0, [17]); break;
            case "amf-truncated": await client.SendAsync(20, 0, 0, [2, 0, 2, 0x61]); break;
            case "transaction": await client.SendCommandAsync(0, "connect", -1.0, null); break;
            case "connect": await client.SendCommandAsync(0, "connect", 1.0, new Dictionary<string, object?> { ["app"] = Secret, ["password"] = Secret }); break;
            case "create": await client.SendCommandAsync(1, "createStream", 2.0, null); break;
            case "publish-mode": await client.SendCommandAsync(1, "publish", 3.0, null, "nova", "record"); break;
            case "publish-route": await client.SendCommandAsync(1, "publish", 3.0, null, Secret, "live"); break;
            case "unknown-command": await client.SendCommandAsync(0, Secret, 2.0, null); break;
            case "command-sequence": await client.SendCommandAsync(0, "createStream", 2.0, null); break;
            case "duplicate-create": await client.SendCommandAsync(0, "createStream", 2.0, null); break;
            case "duplicate-publish": await client.SendCommandAsync(1, "publish", 2.0, null, "nova", "live"); break;
            case "release-after-publish": await client.SendCommandAsync(0, "releaseStream", 2.0, null, "nova"); break;
            case "invalid-delete": await client.SendCommandAsync(1, "deleteStream", 2.0, null, 1.0); break;
            case "invalid-close": await client.SendCommandAsync(0, "closeStream", 2.0, null); break;
            case "inactive-unpublish": await client.SendCommandAsync(0, "FCUnpublish", 2.0, null, "nova"); break;
            case "bandwidth-check": await client.SendCommandAsync(1, "_checkbw", 2.0, null); break;
            case "media-stream": await client.SendAsync(8, 2, 0, ObsFlvPublishingSessionTests.AudioConfiguration); break;
            case "message-type": await client.SendAsync(19, 0, 0, [0]); break;
            case "metadata-envelope": await client.SendAsync(18, 1, 0, [5]); break;
            case "metadata-secret-envelope": await client.SendAsync(18, 1, 0, ObsRtmpTestClient.Command("@setDataFrame", null, Secret)); break;
            case "metadata-amf": await client.SendAsync(18, 1, 0, [2, 0, 2, 0x61]); break;
            case "metadata-oversize": await client.SendAsync(18, 1, 0, new byte[65537]); break;
            case "metadata-bootstrap":
                await client.SendAsync(18, 1, 0, ObsRtmpTestClient.Command("onMetaData",
                    new Dictionary<string, object?> { ["description"] = new string('a', 80) }));
                break;
            case "avc-envelope": await client.SendAsync(9, 1, 0, [0x17, 0]); break;
            case "avc-sequence": await client.SendAsync(9, 1, 0, [0x17, 0, 1, 0, 0]); break;
            case "avc-config": video[5] = 2; await client.SendAsync(9, 1, 0, video); break;
            case "avc-extension": await client.SendAsync(9, 1, 0, video.Concat(new byte[4]).ToArray()); break;
            case "avc-missing-sets": video[10] = 0xE0; await client.SendAsync(9, 1, 0, video); break;
            case "avc-truncated-set": await client.SendAsync(9, 1, 0, video[..12]); break;
            case "avc-invalid-set": video[13] = 0xE7; await client.SendAsync(9, 1, 0, video); break;
            case "aac-envelope": await client.SendAsync(8, 1, 0, [0xAF, 0]); break;
            case "aac-length": await client.SendAsync(8, 1, 0, [0xAF, 0, 0x12, 0x10, 0, 0, 0]); break;
            case "aac-object": await client.SendAsync(8, 1, 0, [0xAF, 0, 0x5A, 0x10]); break;
            case "aac-frequency": await client.SendAsync(8, 1, 0, [0xAF, 0, 0x17, 0x90]); break;
            case "aac-channels": await client.SendAsync(8, 1, 0, [0xAF, 0, 0x12, 0x38]); break;
            case "audio-before-config": await client.SendAsync(8, 1, 0, ObsFlvPublishingSessionTests.Audio); break;
            case "video-before-config": await client.SendAsync(9, 1, 0, ObsFlvPublishingSessionTests.Idr); break;
            case "replacement": await client.SendAsync(8, 1, 0, [0xAF, 0, 0x11, 0x90]); break;
            case "nalu-length": await client.SendAsync(9, 1, 0, [0x17, 1, 0, 0, 0, 0, 0, 0, 0]); break;
            case "nalu-body": await client.SendAsync(9, 1, 0, [0x17, 1, 0, 0, 0, 0, 0, 0, 1, 0xE5]); break;
            case "nalu-empty": await client.SendAsync(9, 1, 0, [0x17, 1, 0, 0, 0]); break;
            case "avc-end": await client.SendAsync(9, 1, 0, [0x17, 2, 0, 0, 1]); break;
            default: throw new InvalidOperationException("Unknown test fixture.");
        }
    }
}

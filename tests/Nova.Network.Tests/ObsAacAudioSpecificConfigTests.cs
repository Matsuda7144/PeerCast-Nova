using PeerCast.Nova.Core.Media;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

public sealed class ObsAacAudioSpecificConfigTests
{
    [Theory]
    [InlineData(0, 96000)]
    [InlineData(1, 88200)]
    [InlineData(2, 64000)]
    [InlineData(3, 48000)]
    [InlineData(4, 44100)]
    [InlineData(5, 32000)]
    [InlineData(6, 24000)]
    [InlineData(7, 22050)]
    [InlineData(8, 16000)]
    [InlineData(9, 12000)]
    [InlineData(10, 11025)]
    [InlineData(11, 8000)]
    [InlineData(12, 7350)]
    public async Task Every_previous_indexed_rate_and_mono_stereo_control_remains_valid(int index, int rate)
    {
        foreach (var channels in new[] { 1, 2 })
        {
            byte[] control = [(byte)(0x10 | (index >> 1)), (byte)((index << 7) | (channels << 3))];
            foreach (var extension in new[] { false, true })
            {
                var asc = extension ? control.Concat(new byte[] { 0x56, 0xE5, 0 }).ToArray() : control;
                Assert.True(AacLcAudioSpecificConfigParser.TryParse(asc, out var actualRate, out var actualChannels, out var shortFrame));
                Assert.Equal(rate, actualRate);
                Assert.Equal(channels, actualChannels);
                Assert.False(shortFrame);
                var publisher = ObsFlvPublishingSessionTests.Create(new());
                await publisher.AcceptAsync(new(8, 1, 0, new byte[] { 0xAF, 0 }.Concat(asc).ToArray()), CancellationToken.None);
            }
        }
    }

    [Theory]
    [InlineData("1210")]
    [InlineData("1190")]
    [InlineData("1188")]
    [InlineData("119056E500")]
    [InlineData("17805DC010")]
    [InlineData("17805DC01056E500")]
    public async Task Supported_ASC_and_identical_duplicates_preserve_full_original_bytes(string hex)
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = ObsFlvPublishingSessionTests.Create(consumer);
        byte[] config = new byte[] { 0xAF, 0 }.Concat(Convert.FromHexString(hex)).ToArray();
        await publisher.AcceptAsync(new(8, 1, 0, config), CancellationToken.None);
        await publisher.AcceptAsync(new(8, 1, 100, config), CancellationToken.None);
        await publisher.AcceptAsync(new(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration), CancellationToken.None);
        await publisher.AcceptAsync(new(8, 1, 200, config), CancellationToken.None);
        var retention = new PcpFlvBootstrapRetention();
        retention.Observe(Assert.Single(consumer.Packets).Payload);
        Assert.Equal(config, retention.Snapshot().AudioConfiguration.Span[11..^4].ToArray());
        // Even semantically equivalent re-encoding remains a byte replacement.
        byte[] replacement = [0xAF, 0, 0x11, 0x90, 0x56, 0xE5, 0];
        if (config.SequenceEqual(replacement)) replacement = [0xAF, 0, 0x11, 0x90];
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await publisher.AcceptAsync(new(8, 1, 201, replacement), CancellationToken.None));
        Assert.Equal(ObsRtmpRejectionStage.FlvBootstrap, ObsRtmpRejection.Get(error).Stage);
        Assert.Equal(ObsRtmpRejectionReason.CodecReplacement, ObsRtmpRejection.Get(error).Reason);
        Assert.Single(consumer.Packets);
    }

    [Theory]
    [InlineData("11")] // Truncated core.
    [InlineData("119056E5")] // Truncated sync extension.
    [InlineData("1190000000")] // Arbitrary variable-length suffix.
    [InlineData("119056E501")] // Nonzero alignment.
    [InlineData("119000")] // Unpermitted extra zero byte.
    [InlineData("0990")] // AAC Main.
    [InlineData("2990")] // SBR object type.
    [InlineData("E990")] // PS object type.
    [InlineData("F990")] // Escaped object type.
    [InlineData("1690")] // Reserved frequency index 13.
    [InlineData("1710")] // Reserved frequency index 14.
    [InlineData("1780000010")] // Explicit frequency zero.
    [InlineData("1780181C90")] // Explicit 12345 Hz outside ingest profile.
    [InlineData("17805D")] // Truncated explicit frequency.
    [InlineData("1180")] // PCE/channelConfiguration 0.
    [InlineData("1198")] // Three channels.
    [InlineData("1194")] // Short frame unsupported by ingest.
    [InlineData("1192")] // Core coder dependency.
    [InlineData("1191")] // GA extension flag.
    [InlineData("119056E598")] // Syntactically valid SBR-present extension (48 kHz).
    [InlineData("119056E50000000000")] // Exceeds 8-byte grammar bound.
    public async Task Unsupported_ASC_retains_structured_terminal_diagnostics_through_owner(string hex)
    {
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } });
        host.Start();
        using var client = new ObsRtmpTestClient();
        await client.ConnectAsync(host.IngestEndPoint);
        await client.PublishAsync();
        await client.SendAsync(18, 1, 0, ObsRtmpTestClient.Command("@setDataFrame", "onMetaData",
            new Dictionary<string, object?> { ["audiosamplerate"] = 48000.0 }), id: 4);
        var asc = Convert.FromHexString(hex);
        await client.SendAsync(8, 1, 0, new byte[] { 0xAF, 0 }.Concat(asc).ToArray(), id: 4);
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ObsPublishChannelEndReason.ProtocolRejected, result.EndReason);
        var diagnostic = Assert.IsType<ObsRtmpRejectionDiagnostic>(result.Rejection);
        Assert.Equal(ObsRtmpRejectionStage.AacSequenceHeader, diagnostic.Stage);
        Assert.Equal(ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig, diagnostic.Reason);
        Assert.Equal(asc.Length, diagnostic.Context.ConfigLength);
        Assert.Equal(asc.Length + 2, diagnostic.Context.DeclaredPayloadLength);
        Assert.Equal(asc.Length + 2, diagnostic.Context.AssembledPayloadLength);
        Assert.Equal((byte)8, diagnostic.Context.MessageType);
        Assert.Equal((byte)0, diagnostic.Context.PacketType);
        Assert.Equal(4, diagnostic.Context.ChunkStreamId);
        Assert.Equal(1U, diagnostic.Context.MessageStreamId);
        Assert.Equal(ObsFlvBootstrapState.Metadata, diagnostic.Context.BootstrapState);
        Assert.Equal(0, result.HeadCount);
        Assert.Equal(0, result.DataCount);
        Assert.True(result.ShutdownCompleted);
        Assert.False(host.IsRegistered);
    }

    // Synthetic managed 5-byte ASC compatibility fixture; original OBS bytes were not retained.
    // AAC-LC, indexed 48 kHz, stereo, GASpecificConfig=000, sync=0x2b7,
    // extension AOT=5, sbrPresentFlag=0, seven zero alignment bits.
    [Fact]
    public async Task Synthetic_managed_five_byte_ASC_is_validated_and_retained()
    {
        var consumer = new ObsRtmpTestConsumer();
        var publisher = ObsFlvPublishingSessionTests.Create(consumer);
        byte[] config = [0xAF, 0, 0x11, 0x90, 0x56, 0xE5, 0];
        await publisher.AcceptAsync(new(8, 1, 0, config), CancellationToken.None);
        Assert.Empty(consumer.Packets);
        await publisher.AcceptAsync(new(9, 1, 0, ObsFlvPublishingSessionTests.VideoConfiguration), CancellationToken.None);
        var retention = new PcpFlvBootstrapRetention();
        retention.Observe(Assert.Single(consumer.Packets).Payload);
        Assert.Equal(config, retention.Snapshot().AudioConfiguration.Span[11..^4].ToArray());
    }
}

using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRecordingPacketStreamBridgeTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact(
            "3F2504E04F8911D39A0C0305E82C3301",
            "N"));

    [Fact]
    public async Task Consume_CopiesCallbackPayloadIntoReadableStream()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);
        await using var stream = bridge.OpenReadStream();

        var payload = new byte[]
        {
            0x10,
            0x20,
            0x30,
        };

        await bridge.ConsumeAsync(
            CreatePacket(
                sequence: 0,
                payload),
            CancellationToken.None);

        payload[0] = 0xFF;

        await bridge.CompleteAsync();

        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);

        Assert.Equal(
            new byte[]
            {
                0x10,
                0x20,
                0x30,
            },
            destination.ToArray());
        Assert.Equal(1, bridge.ExpectedSequence);
    }

    [Fact]
    public async Task Complete_AllowsBufferedBytesThenEndOfStream()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);
        await using var stream = bridge.OpenReadStream();

        await bridge.ConsumeAsync(
            CreatePacket(
                sequence: 0,
                [0x01, 0x02]),
            CancellationToken.None);
        await bridge.CompleteAsync();

        var buffer = new byte[2];
        await stream.ReadExactlyAsync(buffer);

        Assert.Equal(
            [0x01, 0x02],
            buffer);
        Assert.Equal(
            0,
            await stream.ReadAsync(
                new byte[1]));
    }

    [Fact]
    public async Task Consume_SequenceGap_IsRejectedAndFaultsReader()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);
        await using var stream = bridge.OpenReadStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await bridge.ConsumeAsync(
                CreatePacket(
                    sequence: 1,
                    [0x01]),
                CancellationToken.None));

        Assert.Contains(
            "sequence",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                using var destination = new MemoryStream();
                await stream.CopyToAsync(destination);
            });
    }

    [Fact]
    public async Task Consume_Discontinuity_IsRejected()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await bridge.ConsumeAsync(
                CreatePacket(
                    sequence: 0,
                    [0x01],
                    new PcpStreamDiscontinuity(
                        100,
                        50,
                        100)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Consume_DifferentChannel_IsRejected()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);

        var otherChannel = new ChannelId(
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await bridge.ConsumeAsync(
                CreatePacket(
                    sequence: 0,
                    [0x01],
                    channelId: otherChannel),
                CancellationToken.None));
    }

    [Fact]
    public async Task Consume_PreCancelled_DoesNotAdvanceSequence()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await bridge.ConsumeAsync(
                CreatePacket(
                    sequence: 0,
                    [0x01]),
                source.Token));

        Assert.Equal(0, bridge.ExpectedSequence);
    }

    [Fact]
    public async Task OpenReadStream_CanOnlyBeOpenedOnce()
    {
        await using var bridge =
            new PcpRecordingPacketStreamBridge(ChannelId);
        await using var stream = bridge.OpenReadStream();

        Assert.Throws<InvalidOperationException>(
            () => bridge.OpenReadStream());
    }

    [Fact]
    public void Options_RejectInvalidThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRecordingPacketStreamBridgeOptions
            {
                PauseWriterThresholdBytes = 0,
            }.Validate());

        Assert.Throws<InvalidOperationException>(
            () => new PcpRecordingPacketStreamBridgeOptions
            {
                PauseWriterThresholdBytes = 1024,
                ResumeWriterThresholdBytes = 1024,
            }.Validate());

        Assert.Throws<InvalidOperationException>(
            () => new PcpRecordingPacketStreamBridgeOptions
            {
                PauseWriterThresholdBytes = 1024,
                ResumeWriterThresholdBytes = 512,
                MinimumSegmentSizeBytes = 2048,
            }.Validate());
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        byte[] payload,
        PcpStreamDiscontinuity? discontinuity = null,
        ChannelId? channelId = null)
    {
        return new PcpChannelPacket(
            channelId ?? ChannelId,
            SessionId,
            sequence,
            type: 1,
            wirePosition: checked((uint)(100 + sequence)),
            streamPosition: discontinuity is null
                ? 100 + sequence
                : null,
            payload,
            discontinuity);
    }
}

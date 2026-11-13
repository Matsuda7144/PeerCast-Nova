using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpSmartRelayMediaTests
{
    private const uint Head = 0x64616568, Data = 0x61746164;
    [Fact]
    public async Task FirstHeadAfterHeaderlessDataStartsAnExplicitNewGeneration()
    {
        using var clock = new AnnounceManualTimeProvider();
        var packets = new List<PcpChannelPacket>(); var resets = 0;
        var media = Media(clock, packets, _ => { resets++; return Task.CompletedTask; });
        await media.ConsumeAsync(Packet(Data, 100, [1]), default);
        await media.ConsumeAsync(Packet(Head, 0, [9]), default);
        Assert.Equal(1, resets);
        Assert.Equal(2, media.Generation);
        Assert.Null(media.Cursor);
        Assert.NotNull(packets[^1].Discontinuity);
        await media.ConsumeAsync(Packet(Data, 0, [2]), default);
        Assert.Equal(1, media.Cursor);
    }

    [Fact]
    public async Task CursorIsAcceptedDataEndWithWrapAndNoHeadDoubleAddition()
    {
        using var clock = new AnnounceManualTimeProvider();
        var packets = new List<PcpChannelPacket>();
        var media = Media(clock, packets);
        await media.ConsumeAsync(Packet(Head, 0, [9, 8, 7]), default);
        Assert.Null(media.Cursor);
        await media.ConsumeAsync(Packet(Data, uint.MaxValue - 1, [1, 2, 3, 4]), default);
        Assert.Equal(4294967298L, media.Cursor);
        await media.ConsumeAsync(Packet(Data, 2, [5]), default);
        Assert.Equal(4294967299L, media.Cursor);
        Assert.Equal(4294967298L, packets[^1].StreamPosition);
    }

    [Fact]
    public async Task OnlyByteIdenticalDuplicateIsDroppedAndDoesNotProveStableProgress()
    {
        using var clock = new AnnounceManualTimeProvider();
        var packets = new List<PcpChannelPacket>();
        var media = Media(clock, packets);
        await media.ConsumeAsync(Packet(Data, 100, [1, 2, 3]), default);
        clock.Advance(TimeSpan.FromSeconds(31));
        await media.ConsumeAsync(Packet(Data, 100, [1, 2, 3]), default);
        Assert.Single(packets);
        Assert.False(media.Stable);
        await Assert.ThrowsAsync<PcpSmartRelayDiscontinuityException>(() => media.ConsumeAsync(Packet(Data, 100, [9, 2, 3]), default).AsTask());
        Assert.Single(packets);
    }

    [Theory]
    [InlineData(104U)]
    [InlineData(99U)]
    public async Task GapOrBackwardRequiresFreshHeadBeforeNewData(uint position)
    {
        using var clock = new AnnounceManualTimeProvider();
        var packets = new List<PcpChannelPacket>();
        var media = Media(clock, packets);
        await media.ConsumeAsync(Packet(Head, 0, [9]), default);
        await media.ConsumeAsync(Packet(Data, 100, [1, 2, 3]), default);
        await Assert.ThrowsAsync<PcpSmartRelayDiscontinuityException>(() => media.ConsumeAsync(Packet(Data, position, [4]), default).AsTask());
        await media.SuspendAsync(default);
        await Assert.ThrowsAsync<PcpSmartRelayDiscontinuityException>(() => media.ConsumeAsync(Packet(Data, 0, [4]), default).AsTask());
        await media.ConsumeAsync(Packet(Head, 0, [8]), default);
        await media.ConsumeAsync(Packet(Data, 0, [5]), default);
        Assert.Equal(2, media.Generation);
        Assert.Equal(1, media.Discontinuities);
        Assert.NotNull(packets[^2].Discontinuity);
        Assert.Equal(1, media.Cursor);
        Assert.Equal(4, packets.Count);
    }

    [Fact]
    public async Task HeadChangeClosesOldGenerationBeforeForwardingNewHead()
    {
        using var clock = new AnnounceManualTimeProvider();
        var packets = new List<PcpChannelPacket>(); var resets = 0;
        var media = Media(clock, packets, _ => { resets++; return Task.CompletedTask; });
        await media.ConsumeAsync(Packet(Head, 0, [9]), default);
        await media.ConsumeAsync(Packet(Data, 100, [1]), default);
        await media.ConsumeAsync(Packet(Head, 0, [8]), default);
        Assert.Equal(1, resets);
        Assert.Null(media.Cursor);
        Assert.NotNull(packets[^1].Discontinuity);
        await media.ConsumeAsync(Packet(Data, 1, [4]), default);
        Assert.Equal(2, media.Cursor);
    }

    [Fact]
    public async Task StableReceptionRequiresThirtySecondsOfAdvancingData()
    {
        using var clock = new AnnounceManualTimeProvider();
        var media = Media(clock, []);
        await media.ConsumeAsync(Packet(Data, 100, [1]), default);
        clock.Advance(TimeSpan.FromSeconds(29));
        await media.ConsumeAsync(Packet(Data, 101, [2]), default);
        Assert.False(media.Stable);
        clock.Advance(TimeSpan.FromSeconds(1));
        await media.ConsumeAsync(Packet(Data, 102, [3]), default);
        Assert.True(media.Stable);
    }

    private static PcpSmartRelayMediaConsumer Media(TimeProvider clock, List<PcpChannelPacket> packets, Func<CancellationToken, Task>? reset = null) =>
        new(new Sink(packets), clock, reset, null, () => { });
    private static PcpChannelPacket Packet(uint type, uint position, byte[] bytes) =>
        new(new(Guid.NewGuid()), new(Guid.NewGuid()), 0, type, position, position, bytes, null);
    private sealed class Sink(List<PcpChannelPacket> packets) : IPcpChannelPacketConsumer
    { public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken token) { packets.Add(packet); return ValueTask.CompletedTask; } }
}

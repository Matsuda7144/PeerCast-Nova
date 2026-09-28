using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRecordingSourceEndpointRegistryTests
{
    private static readonly ChannelId FirstChannelId = new(
        Guid.ParseExact(
            "11111111111111111111111111111111",
            "N"));

    private static readonly ChannelId SecondChannelId = new(
        Guid.ParseExact(
            "22222222222222222222222222222222",
            "N"));

    [Fact]
    public async Task Resolve_UsesCandidateChannelIdentity()
    {
        var registry = new PcpRecordingSourceEndpointRegistry();
        var endpoint = new PcpRecordingSourceEndpoint(
            "127.0.0.1",
            7144,
            initialStreamPosition: 123);
        registry.Upsert(
            FirstChannelId,
            endpoint);

        var resolved = await registry.ResolveAsync(
            CreateRequest(FirstChannelId),
            CancellationToken.None);

        Assert.Same(endpoint, resolved);
    }

    [Fact]
    public async Task Resolve_UnknownChannel_ReturnsNull()
    {
        var registry = new PcpRecordingSourceEndpointRegistry();

        var resolved = await registry.ResolveAsync(
            CreateRequest(FirstChannelId),
            CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public void Upsert_ReplacesEndpointWithoutGrowingRegistry()
    {
        var registry = new PcpRecordingSourceEndpointRegistry();
        var first = new PcpRecordingSourceEndpoint(
            "127.0.0.1",
            7144);
        var second = new PcpRecordingSourceEndpoint(
            "127.0.0.2",
            7145);

        registry.Upsert(
            FirstChannelId,
            first);
        registry.Upsert(
            FirstChannelId,
            second);

        Assert.Equal(1, registry.Count);
        Assert.True(
            registry.TryGet(
                FirstChannelId,
                out var resolved));
        Assert.Same(second, resolved);
    }

    [Fact]
    public void Remove_IsIdempotent()
    {
        var registry = new PcpRecordingSourceEndpointRegistry();
        registry.Upsert(
            FirstChannelId,
            new PcpRecordingSourceEndpoint(
                "127.0.0.1",
                7144));

        Assert.True(registry.Remove(FirstChannelId));
        Assert.False(registry.Remove(FirstChannelId));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Upsert_EndpointLimitIsFailClosed()
    {
        var registry = new PcpRecordingSourceEndpointRegistry(
            maximumEndpointCount: 1);

        registry.Upsert(
            FirstChannelId,
            new PcpRecordingSourceEndpoint(
                "127.0.0.1",
                7144));

        Assert.Throws<InvalidOperationException>(
            () => registry.Upsert(
                SecondChannelId,
                new PcpRecordingSourceEndpoint(
                    "127.0.0.2",
                    7145)));

        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task Resolve_PreCancelled_DoesNotReadRegistry()
    {
        var registry = new PcpRecordingSourceEndpointRegistry();
        registry.Upsert(
            FirstChannelId,
            new PcpRecordingSourceEndpoint(
                "127.0.0.1",
                7144));

        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await registry.ResolveAsync(
                CreateRequest(FirstChannelId),
                source.Token));
    }

    [Fact]
    public void Constructor_InvalidLimitIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRecordingSourceEndpointRegistry(
                maximumEndpointCount: 0));
    }

    private static RecordingSinkStartRequest CreateRequest(
        ChannelId channelId)
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                channelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "flv")),
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));
    }
}

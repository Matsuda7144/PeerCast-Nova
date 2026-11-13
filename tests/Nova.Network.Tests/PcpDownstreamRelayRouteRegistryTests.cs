using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamRelayRouteRegistryTests
{
    private static readonly ChannelId FirstChannel = new(
        Guid.ParseExact("531dc8dfc7fb42928ac2c0a626517a87", "N"));

    private static readonly ChannelId SecondChannel = new(
        Guid.ParseExact("102132435465768798a9babbdcddedef", "N"));

    [Fact]
    public async Task RegisterAcquiresIndependentExistingFanouts()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var firstFanout = new PcpRelayFanout();
        await using var secondFanout = new PcpRelayFanout();
        await using var firstRegistration = registry.Register(
            FirstChannel,
            firstFanout);
        await using var secondRegistration = registry.Register(
            SecondChannel,
            secondFanout);

        Assert.Equal(2, registry.Count);
        Assert.True(registry.TryAcquire(FirstChannel.Value, out var first));
        Assert.True(registry.TryAcquire(SecondChannel.Value, out var second));
        Assert.False(registry.TryAcquire(Guid.NewGuid(), out var missing));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(missing);

        using (first)
        using (second)
        {
            Assert.Same(firstFanout, first.Fanout);
            Assert.Same(secondFanout, second.Fanout);
        }
    }

    [Fact]
    public async Task RegistrationDisposeStopsNewRoutingAndWaitsForLease()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        var registration = registry.Register(FirstChannel, fanout);
        Assert.True(registry.TryAcquire(FirstChannel.Value, out var lease));
        Assert.NotNull(lease);

        var unregisterTask = registration.DisposeAsync().AsTask();

        Assert.Equal(0, registry.Count);
        Assert.False(registry.TryAcquire(FirstChannel.Value, out _));
        Assert.False(unregisterTask.IsCompleted);
        Assert.Same(unregisterTask, registration.DisposeAsync().AsTask());

        lease.Dispose();
        await unregisterTask;
        await registration.DisposeAsync();
    }

    [Fact]
    public async Task CompletedUnregisterAllowsReplacementWhileOldLeaseDrainsSafely()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var firstFanout = new PcpRelayFanout();
        await using var replacementFanout = new PcpRelayFanout();
        var firstRegistration = registry.Register(FirstChannel, firstFanout);
        Assert.True(registry.TryAcquire(FirstChannel.Value, out var oldLease));
        Assert.NotNull(oldLease);

        var firstUnregister = firstRegistration.DisposeAsync().AsTask();
        await using var replacementRegistration = registry.Register(
            FirstChannel,
            replacementFanout);
        Assert.True(registry.TryAcquire(FirstChannel.Value, out var replacementLease));
        Assert.NotNull(replacementLease);

        using (replacementLease)
        {
            Assert.Same(replacementFanout, replacementLease.Fanout);
        }

        Assert.False(firstUnregister.IsCompleted);
        oldLease.Dispose();
        await firstUnregister;
    }

    [Fact]
    public async Task RegisterRejectsConcurrentDuplicateChannelRoute()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var firstFanout = new PcpRelayFanout();
        await using var secondFanout = new PcpRelayFanout();
        await using var registration = registry.Register(
            FirstChannel,
            firstFanout);

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(FirstChannel, secondFanout));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task LeaseRejectsFanoutAccessAfterRelease()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        await using var registration = registry.Register(FirstChannel, fanout);
        Assert.True(registry.TryAcquire(FirstChannel.Value, out var lease));
        Assert.NotNull(lease);

        lease.Dispose();
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = lease.Fanout);
    }
}

using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisDownstreamRelayOwnerRegistryTests
{
    [Fact]
    public void CountsOnlyExplicitReservationsAndPreservesAllThreeLimits()
    {
        using var policy = new RelayAdmissionPolicy(new(3, 2, 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var broadcast = Register(registry, RelayChannelKind.Broadcast);
        using var viewing = Register(registry, RelayChannelKind.Viewing);
        using var other = Register(registry, RelayChannelKind.Viewing);
        using var first = Reserve(broadcast, 1);
        using var second = Reserve(broadcast, 2);
        Assert.False(broadcast.TryReserve(3, out _));
        using var third = Reserve(viewing, 4);
        Assert.False(other.TryReserve(5, out _));
        Assert.Equal(3, registry.ActiveRelayCount);
        Assert.Equal(3, policy.ReservedCount);
        first.Dispose();
        Assert.False(viewing.TryReserve(6, out _));
        using var replacement = Reserve(other, 7);
        Assert.False(registry.ContainsConnection(1));
        Assert.True(registry.ContainsConnection(7));
        Assert.Equal(3, registry.ActiveRelayCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitZeroAndUnlimitedKeepTheirMeaning(int field)
    {
        using var policy = new RelayAdmissionPolicy(field switch
        {
            0 => new(MaxRelaysTotal: 0),
            1 => new(MaxRelaysPerBroadcastChannel: 0),
            _ => new(MaxRelaysPerViewingChannel: 0),
        });
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var owner = Register(registry, field == 1 ? RelayChannelKind.Broadcast : RelayChannelKind.Viewing);
        Assert.False(owner.TryReserve(1, out var denied));
        Assert.Null(denied);
        Assert.Equal(0, registry.ActiveRelayCount);
        if (field != 0)
        {
            using var other = Register(registry, field == 1 ? RelayChannelKind.Viewing : RelayChannelKind.Broadcast);
            using var allowed = Reserve(other, 2);
        }
    }

    [Fact]
    public void DuplicateIdentityAndCloseCannotReleaseAReplacement()
    {
        using var policy = new RelayAdmissionPolicy();
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var first = Register(registry, RelayChannelKind.Viewing);
        using var second = Register(registry, RelayChannelKind.Broadcast);
        using var old = Reserve(first, 1);
        Assert.False(first.TryReserve(1, out _));
        Assert.False(second.TryReserve(1, out _));
        Assert.Equal(1, registry.ActiveRelayCount);
        old.Dispose();
        using var current = Reserve(second, 1);
        old.Dispose();
        old.Dispose();
        Assert.True(registry.ContainsConnection(1));
        Assert.Equal(1, policy.ReservedCount);
    }

    [Fact]
    public async Task GenerationBindingsNeverResolveAnOldFanoutToTheNewOwner()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysPerViewingChannel: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        var id = new ChannelId(Guid.NewGuid());
        await using var oldFanout = new PcpRelayFanout();
        await using var newFanout = new PcpRelayFanout();
        using var old = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, oldFanout);
        using var oldReservation = Reserve(old, 1);
        using var current = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, newFanout);
        Assert.Same(old, registry.Resolve(id, oldFanout));
        Assert.Same(current, registry.Resolve(id, newFanout));
        Assert.False(old.TryReserve(2, out _));
        Assert.False(current.TryReserve(2, out _));
        old.Dispose();
        Assert.Same(old, registry.Resolve(id, oldFanout));
        Assert.False(registry.Resolve(id, oldFanout)!.TryReserve(3, out _));
        oldReservation.Dispose();
        using var next = Reserve(current, 2);
        old.Dispose();
        oldReservation.Dispose();
        Assert.Same(current, registry.Resolve(id, newFanout));
        Assert.Equal(1, registry.ActiveRelayCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownClassificationIsExplicitAndNeverGuessedAsViewing(bool finite)
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysPerBroadcastChannel: finite ? 1 : null));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        await using var fanout = new PcpRelayFanout();
        var admission = registry.Resolve(new(Guid.NewGuid()), fanout);
        if (finite)
        {
            Assert.NotNull(admission);
            Assert.False(admission.TryReserve(1, out _));
        }
        else Assert.Null(admission);
        Assert.Equal(0, registry.ActiveRelayCount);
    }

    [Fact]
    public async Task InvalidRoleDuplicateGenerationAndReusedFanoutCannotMutateCurrentBinding()
    {
        using var policy = new RelayAdmissionPolicy();
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        await using var fanout = new PcpRelayFanout();
        var id = new ChannelId(Guid.NewGuid());
        var generation = new ChannelSessionId(Guid.NewGuid());
        using var owner = registry.Register(id, generation, RelayChannelKind.Broadcast, fanout);
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Register(id, new(Guid.NewGuid()), (RelayChannelKind)99));
        Assert.Throws<InvalidOperationException>(() => registry.Register(id, generation, RelayChannelKind.Viewing));
        Assert.Throws<ArgumentException>(() => registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, fanout));
        Assert.Same(owner, registry.Resolve(id, fanout));
        using var reservation = Reserve(owner, 1);
    }

    [Fact]
    public async Task RegistryDisposalRetiresAdmissionsWithoutOwningPolicyOrFanoutOrClearingUsage()
    {
        using var policy = new RelayAdmissionPolicy();
        var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        await using var fanout = new PcpRelayFanout();
        using var owner = registry.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing, fanout);
        using var reservation = Reserve(owner, 1);
        registry.Dispose();
        registry.Dispose();
        Assert.False(owner.TryReserve(2, out _));
        Assert.Equal(1, registry.ActiveRelayCount);
        Assert.Equal(1, policy.ReservedCount);
        Assert.Throws<ObjectDisposedException>(() => Register(registry, RelayChannelKind.Viewing));
        using var independent = policy.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        await using var local = fanout.AddChild(new NoopSink());
        Assert.Equal(1, fanout.ActiveChildCount);
        reservation.Dispose();
        Assert.Equal(0, registry.ActiveRelayCount);
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public async Task ConcurrentChannelsAdmissionAndDuplicateCleanupShareOneGlobalSlot()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var first = Register(registry, RelayChannelKind.Viewing);
        using var second = Register(registry, RelayChannelKind.Broadcast);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(1, 32).Select(async id =>
        {
            await start.Task;
            return (id % 2 == 0 ? first : second).TryReserve(id, out var reservation) ? reservation : null;
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);
        var admitted = Assert.Single(results, value => value is not null)!;
        Assert.Equal(1, registry.ActiveRelayCount);
        await Task.WhenAll(Task.Run(admitted.Dispose), Task.Run(admitted.Dispose));
        Assert.Equal(0, policy.ReservedCount);
        using var replacement = Reserve(first, 100);
        admitted.Dispose();
        Assert.Equal(1, registry.ActiveRelayCount);
    }

    [Fact]
    public async Task RetireRegistryDisposeAndDuplicateReleasePreserveOtherLiveReservation()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 2));
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var owner = Register(registry, RelayChannelKind.Viewing);
        using var released = Reserve(owner, 1);
        using var stillLive = Reserve(owner, 2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task AfterStart(Action action)
        {
            await start.Task;
            action();
        }
        var tasks = new[]
        {
            AfterStart(owner.Dispose),
            AfterStart(registry.Dispose),
            AfterStart(released.Dispose),
            AfterStart(released.Dispose),
        };
        start.SetResult();
        await Task.WhenAll(tasks);
        Assert.False(owner.TryReserve(3, out _));
        Assert.False(registry.ContainsConnection(1));
        Assert.True(registry.ContainsConnection(2));
        Assert.Equal(1, registry.ActiveRelayCount);
        Assert.Equal(1, policy.ReservedCount);
        stillLive.Dispose();
        Assert.Equal(0, registry.ActiveRelayCount);
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public void PreCanceledAdmissionDoesNotPublishAnIdentity()
    {
        using var policy = new RelayAdmissionPolicy();
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var owner = Register(registry, RelayChannelKind.Viewing);
        Assert.Throws<OperationCanceledException>(() => owner.TryReserve(1, out _, new CancellationToken(true)));
        Assert.False(registry.ContainsConnection(1));
        Assert.Equal(0, policy.ReservedCount);
    }

    private static GenesisDownstreamRelayOwnerRegistry.Owner Register(
        GenesisDownstreamRelayOwnerRegistry registry, RelayChannelKind kind) =>
        registry.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), kind);

    private static IDisposable Reserve(GenesisDownstreamRelayOwnerRegistry.Owner owner, long id)
    {
        Assert.True(owner.TryReserve(id, out var reservation));
        return Assert.IsAssignableFrom<IDisposable>(reservation);
    }

    private sealed class NoopSink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}

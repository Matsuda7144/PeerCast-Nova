using System.Net;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNetworkPoliciesTests
{
    [Fact]
    public async Task ApplicationLoadBuildsPoliciesFromAllFiveStartupLimits()
    {
        var value = new GenesisGuiNextConnectionPreferences
        {
            ListenPort = 17144,
            UseIpv6 = true,
            MaxRelaysTotal = 3,
            MaxRelaysPerBroadcastChannel = 2,
            MaxRelaysPerViewingChannel = 1,
            TotalUpstreamIpv4Kbps = 1000,
            TotalUpstreamIpv6Kbps = 2000,
        };
        await using var state = await CreateStateAsync(new MemoryStore(value));
        Assert.Equal(new RelayAdmissionLimits(3, 2, 1), state.NetworkPolicies.Relays.Limits);
        Assert.Equal(new UpstreamBandwidthLimits(1000, 2000), state.NetworkPolicies.Bandwidth.Limits);
        Assert.Equal(value, state.Connection.Startup);
        Assert.Equal(0, state.NetworkPolicies.Relays.ReservedCount);
    }

    [Fact]
    public async Task SavedChangesDoNotHotApplyEvenWhenASecondFoundationIsCreated()
    {
        var initial = new GenesisGuiNextConnectionPreferences
        {
            MaxRelaysTotal = 1,
            TotalUpstreamIpv4Kbps = 1000,
        };
        var store = new MemoryStore(initial);
        await using var state = await CreateStateAsync(store);
        var policies = state.NetworkPolicies;
        using var owner = policies.Relays.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        using var reservation = owner.TryReserve(1).Reservation;
        Assert.NotNull(reservation);
        var next = new GenesisGuiNextConnectionPreferences
        {
            MaxRelaysTotal = 0,
            MaxRelaysPerBroadcastChannel = 0,
            MaxRelaysPerViewingChannel = 0,
            TotalUpstreamIpv4Kbps = 0,
            TotalUpstreamIpv6Kbps = 0,
        };
        Assert.True((await state.Connection.SaveAsync(GenesisGuiNextConnectionInput.From(next))).Saved);
        Assert.Same(policies, state.NetworkPolicies);
        Assert.Equal(1, policies.Relays.Limits.MaxRelaysTotal);
        Assert.Equal(1000, policies.Bandwidth.Limits.Ipv4Kbps);
        using var secondFoundation = GenesisGuiNextNetworkPolicies.Create(state.Connection);
        Assert.Equal(policies.Relays.Limits, secondFoundation.Relays.Limits);
        Assert.Equal(policies.Bandwidth.Limits, secondFoundation.Bandwidth.Limits);
        Assert.Equal(RelayAdmissionDecision.TotalLimitReached, owner.TryReserve(2).Decision);
        await using var restarted = await CreateStateAsync(store);
        Assert.Equal(new RelayAdmissionLimits(0, 0, 0), restarted.NetworkPolicies.Relays.Limits);
        Assert.Equal(BandwidthAdmissionDecision.LimitReached,
            restarted.NetworkPolicies.Bandwidth.Evaluate(IPAddress.Loopback, new(null, null), null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrFailedLoadProducesUnlimitedSafeFoundation(bool fail)
    {
        await using var state = await CreateStateAsync(new MemoryStore(GenesisGuiNextConnectionPreferences.Default, fail));
        Assert.Equal(fail, state.Connection.LoadFailed);
        Assert.Equal(new RelayAdmissionLimits(), state.NetworkPolicies.Relays.Limits);
        Assert.Equal(new UpstreamBandwidthLimits(), state.NetworkPolicies.Bandwidth.Limits);
        Assert.Equal(0, state.NetworkPolicies.Relays.ReservedCount);
    }

    [Fact]
    public async Task JsonRestartRetainsUnlimitedAndExplicitZeroAndPositivePolicies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaNetworkPolicyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonGenesisGuiNextConnectionPreferencesStore(Path.Combine(directory, "connection.json"));
            await using (var state = await CreateStateAsync(store))
            {
                var next = new GenesisGuiNextConnectionPreferences
                {
                    MaxRelaysTotal = null,
                    MaxRelaysPerBroadcastChannel = 0,
                    MaxRelaysPerViewingChannel = 2,
                    TotalUpstreamIpv4Kbps = null,
                    TotalUpstreamIpv6Kbps = 0,
                };
                Assert.True((await state.Connection.SaveAsync(GenesisGuiNextConnectionInput.From(next))).Saved);
            }
            await using var restarted = await CreateStateAsync(store);
            Assert.Equal(new RelayAdmissionLimits(null, 0, 2), restarted.NetworkPolicies.Relays.Limits);
            Assert.Equal(new UpstreamBandwidthLimits(null, 0), restarted.NetworkPolicies.Bandwidth.Limits);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ApplicationShutdownRetiresFoundationWithoutClearingOutstandingUsage()
    {
        var state = await CreateStateAsync(new MemoryStore(GenesisGuiNextConnectionPreferences.Default));
        using var owner = state.NetworkPolicies.Relays.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        using var reservation = owner.TryReserve(1).Reservation;
        Assert.NotNull(reservation);
        await state.DisposeAsync();
        Assert.Equal(RelayAdmissionDecision.OwnerRetired, owner.TryReserve(2).Decision);
        Assert.Equal(1, state.NetworkPolicies.Relays.ReservedCount);
        reservation.Dispose();
        Assert.Equal(0, state.NetworkPolicies.Relays.ReservedCount);
    }

    [Fact]
    public async Task RegistryUsesTheSameStartupPolicyAndSavedChangesOnlyAffectNextApplication()
    {
        var store = new MemoryStore(new() { MaxRelaysTotal = 1 });
        await using var first = await CreateStateAsync(store);
        using var directOwner = first.NetworkPolicies.Relays.OpenChannel(
            new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        using var directReservation = directOwner.TryReserve(1).Reservation;
        using var owner = first.DownstreamRelays.Register(
            new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        Assert.False(owner.TryReserve(2, out _));
        Assert.True((await first.Connection.SaveAsync(
            GenesisGuiNextConnectionInput.From(new() { MaxRelaysTotal = 0 }))).Saved);
        directReservation!.Dispose();
        Assert.True(owner.TryReserve(2, out var reservation));
        using (reservation)
        {
            Assert.Equal(1, first.DownstreamRelays.ActiveRelayCount);
            Assert.Equal(1, first.NetworkPolicies.Relays.ReservedCount);
        }
        await using var restarted = await CreateStateAsync(store);
        using var newOwner = restarted.DownstreamRelays.Register(
            new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing);
        Assert.False(newOwner.TryReserve(3, out _));
    }

    private static ValueTask<GenesisGuiNextApplicationState> CreateStateAsync(IGenesisGuiNextConnectionPreferencesStore store) =>
        GenesisGuiNextApplicationState.LoadAsync(new FavoriteStore(), new NotificationStore(),
            DefaultGenesisYellowPageConfigurationStore.Instance, DefaultGenesisGuiNextPlaybackPreferencesStore.Instance,
            store, new SilentSound());

    private sealed class MemoryStore(GenesisGuiNextConnectionPreferences value, bool fail = false) : IGenesisGuiNextConnectionPreferencesStore
    {
        private GenesisGuiNextConnectionPreferences _value = value;
        public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            fail ? ValueTask.FromException<GenesisGuiNextConnectionPreferences>(new IOException()) : ValueTask.FromResult(_value);
        public ValueTask SaveAsync(GenesisGuiNextConnectionPreferences preferences, CancellationToken cancellationToken = default)
        {
            _value = preferences;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class FavoriteStore : IGenesisFavoriteChannelStore
    {
        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>([]);
        public ValueTask SaveAsync(IReadOnlyList<GenesisFavoriteChannel> favorites, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class NotificationStore : IGenesisChannelNotificationPreferenceStore
    {
        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        public ValueTask SaveAsync(GenesisChannelNotificationPreferences preferences, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

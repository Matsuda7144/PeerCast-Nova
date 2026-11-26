using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App.Tests;

public sealed class SmartRelayPacingStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task PacingRequiresOptInAndKeepsStartupBudgetAndAdmissionUntilRestart(int? kbps)
    {
        await using var test = await PublicChannelTestFixture.CreateAsync(new() { TotalUpstreamIpv4Kbps = kbps }, pacing: true);
        var policy = test.State.NetworkPolicies.Relays;
        var pacing = Assert.IsType<PcpDownstreamPacer>(policy.Pacing);
        Assert.Equal(kbps, pacing.MediaKbps);
        using var old = test.State.DownstreamRelays.Register(new(Guid.NewGuid()), new(Guid.NewGuid()),
            RelayChannelKind.Viewing, readBitrateKbps: () => 1);
        Assert.Same(pacing, old.Pacing);
        Assert.Equal(kbps != 0, old.TryReserve(123, out var lease));
        using (lease)
        {
            old.Dispose();
            using var current = test.State.DownstreamRelays.Register(old.ChannelId, new(Guid.NewGuid()),
                RelayChannelKind.Viewing, readBitrateKbps: () => 1);
            Assert.Same(pacing, current.Pacing);
            Assert.Equal(kbps == 0 ? 0 : 1, policy.ReservedCount);
            var input = GenesisGuiNextConnectionInput.From(test.State.Connection.Saved) with { TotalUpstreamIpv4Kbps = "9000" };
            Assert.True((await test.State.Connection.SaveAsync(input)).Saved);
            Assert.Equal(9000, pacing.MediaKbps);
            using var defaultEnabled = GenesisGuiNextNetworkPolicies.Create(test.State.Connection, _ => null);
            Assert.NotNull(defaultEnabled.Relays.Pacing);
            using var off = GenesisGuiNextNetworkPolicies.Create(test.State.Connection, name => name == "NOVA_SMART_RELAY_PACING" ? "0" : null);
            Assert.Null(off.Relays.Pacing);
            Assert.Equal(kbps, off.Bandwidth.Limits.Ipv4Kbps);
            Assert.Equal(9000, test.State.NetworkPolicies.Bandwidth.Limits.Ipv4Kbps);
            Assert.Contains("媒体ペーシング", test.State.DownstreamRelays.CapacityStatusText());
        }
    }
}

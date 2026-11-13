using PeerCast.Nova.Network.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisGuiNextRelayActivationTests
{
    [Fact]
    public async Task SmartRecovery_RetiredOwnerStopsBackoffAndCannotRemoveReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        await using var old = await fixture.Channels.PrepareViewingAsync(Valid, new(System.Net.IPAddress.Loopback, 0),
            new() { UpstreamSession = new() { SmartRelay = new() { Mode = PcpSmartRelayMode.Persistent } } }, timeout.Token);
        var run = old.RunAsync(timeout.Token);
        await UntilAsync(() => old.ViewingRuntime!.SmartRelaySnapshot.State == PcpSmartRelayState.Backoff, timeout.Token);
        await using var replacement = await fixture.Channels.PrepareViewingAsync(Valid, new(System.Net.IPAddress.Loopback, 0), cancellationToken: timeout.Token);
        await run.WaitAsync(timeout.Token);
        Assert.True(old.IsRetired);
        Assert.True(replacement.IsCurrent);
        Assert.False(replacement.ActivationStarted);
        await old.DisposeAsync();
        Assert.True(old.CleanupConfirmed);
        Assert.True(replacement.IsCurrent);
        Assert.Equal(1, fixture.Channels.OwnerCount);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("off", null)]
    [InlineData("invalid", null)]
    [InlineData("limited", PcpSmartRelayMode.Limited)]
    [InlineData(" PERSISTENT ", PcpSmartRelayMode.Persistent)]
    public void SmartRecovery_OnlyExplicitModeEnablesRecovery(string? text, PcpSmartRelayMode? expected) =>
        Assert.Equal(expected, GenesisGuiNextRelayActivation.RecoveryPolicy(text)?.Mode);

    [Fact]
    public async Task SmartRecovery_ActualGuiOwnerRemainsAliveAfterParentLossAndStopReleasesIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        var upstream = new RelayActivationTestUpstream(timeout.Token);
        await using var activation = new GenesisGuiNextRelayActivation(fixture.Channels,
            new() { Mode = PcpSmartRelayMode.Persistent });
        var start = activation.StartAsync(upstream.Channel, timeout.Token);
        await upstream.SendAsync(timeout.Token, 0x61746164);
        Assert.True(await start);
        var owner = activation.CurrentOwner!;
        var endpoint = owner.LocalEndPoint;
        await upstream.DisposeAsync();
        await UntilAsync(() => activation.Snapshot.Recovery == PcpSmartRelayState.Backoff, timeout.Token);
        Assert.Same(owner, activation.CurrentOwner);
        Assert.Equal(endpoint, owner.LocalEndPoint);
        Assert.Equal(1, fixture.Channels.OwnerCount);
        Assert.Contains("再接続", activation.Snapshot.Text);
        await activation.StopAsync().WaitAsync(timeout.Token);
        Assert.True(owner.CleanupConfirmed);
        Assert.Null(activation.CurrentOwner);
        Assert.Equal(0, fixture.Channels.OwnerCount);
        Assert.Equal(0, fixture.State.NetworkPolicies.Relays.ReservedCount);
    }
}

using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisGuiNextRelayActivationTests
{
    private static readonly GenesisChannelItem Valid = new("PCP", "pcp://127.0.0.1:1/channel/11111111111111111111111111111111");

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("http://127.0.0.1:1/channel/11111111111111111111111111111111", null, false)]
    [InlineData("pcp://127.0.0.1:1/channel/00000000000000000000000000000000", null, false)]
    [InlineData("pcp://127.0.0.1:1/channel/11111111111111111111111111111111", "invalid", false)]
    [InlineData("pcp://127.0.0.1:1/channel/11111111111111111111111111111111", "22222222222222222222222222222222", false)]
    [InlineData("pcp://127.0.0.1:1/channel/11111111111111111111111111111111", null, true)]
    [InlineData("pcp://127.0.0.1:1/channel/11111111111111111111111111111111", "11111111-1111-1111-1111-111111111111", true)]
    public void ResolverRequiresPcpIdentityAndConsistentMetadata(string? source, string? id, bool accepted)
    {
        var channel = source is null ? null : new GenesisChannelItem("Not an identity", source, ChannelId: id);
        Assert.Equal(accepted, GenesisGuiNextRelayActivation.TryResolve(channel, out var resolved));
        Assert.Equal(accepted, resolved is not null);
    }

    [Fact]
    public async Task MissingSelectionAndInvalidIdentityNeverPrepare()
    {
        var prepared = 0;
        await using var activation = new GenesisGuiNextRelayActivation((_, _) => { prepared++; throw new InvalidOperationException(); });
        Assert.False(await activation.StartAsync(null));
        Assert.Equal(GenesisRelayActivationError.NoSelection, activation.Snapshot.Error);
        Assert.False(await activation.StartAsync(Valid with { ChannelId = "bad" }));
        Assert.Equal(GenesisRelayActivationError.UnresolvedChannel, activation.Snapshot.Error);
        Assert.Equal(0, prepared);
    }

    [Fact]
    public async Task PreparingDoubleStartStopAndLateOwnerCannotBind()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GenesisGuiNextPublicChannelComposition.Owner? owner = null;
        await using var activation = new GenesisGuiNextRelayActivation(async (channel, _) =>
        {
            entered.SetResult();
            await release.Task;
            // Deliberately late completion ignoring cancellation: no late Run.
            return owner = await fixture.Channels.PrepareViewingAsync(channel, new(IPAddress.Loopback, 0), cancellationToken: CancellationToken.None);
        });
        var start = activation.StartAsync(Valid);
        await entered.Task.WaitAsync(timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Preparing, activation.Snapshot.State);
        Assert.False(await activation.StartAsync(Valid));
        Assert.Equal(GenesisRelayActivationError.AlreadyStarting, activation.Snapshot.Error);
        var stop = activation.StopAsync();
        Assert.Same(stop, activation.StopAsync());
        Assert.False(await activation.StartAsync(Valid));
        Assert.Equal(GenesisRelayActivationError.Stopping, activation.Snapshot.Error);
        release.SetResult();
        await stop.WaitAsync(timeout.Token);
        Assert.False(await start);
        Assert.False(owner!.ActivationStarted);
        Assert.True(owner.CleanupConfirmed);
        Assert.Equal(GenesisRelayActivationState.Stopped, activation.Snapshot.State);
        Assert.Equal(0, fixture.Channels.OwnerCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrepareFailureIsClassifiedCleanAndRetryUsesNewGeneration(bool socket)
    {
        await using var activation = new GenesisGuiNextRelayActivation((_, _) =>
            throw (socket ? new SocketException((int)SocketError.AddressAlreadyInUse) : new IOException("private internal detail")));
        Assert.False(await activation.StartAsync(Valid));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.Equal(socket ? GenesisRelayActivationError.BindFailed : GenesisRelayActivationError.StartFailed, activation.Snapshot.Error);
        Assert.DoesNotContain("private", activation.Snapshot.Text);
        Assert.True(activation.Snapshot.CleanupConfirmed);
        Assert.False(await activation.StartAsync(Valid));
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.Equal(2, activation.Snapshot.Generation);
    }

    [Fact]
    public async Task CallerCancellationBeforeStartDoesNotPrepare()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await using var activation = new GenesisGuiNextRelayActivation((_, _) => throw new InvalidOperationException());
        Assert.False(await activation.StartAsync(Valid, canceled.Token));
        Assert.Equal(GenesisRelayActivationError.Canceled, activation.Snapshot.Error);
        Assert.Equal(0, activation.Snapshot.Generation);
    }

    [Fact]
    public async Task ShutdownTimeoutDoesNotClaimStoppedOrReleaseLateLease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        await using var fanout = new PcpRelayFanout();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new HeldLifetime(disposalEntered, release);
        var owner = new GenesisGuiNextPublicChannelComposition.Owner(fixture.Channels,
            new(Guid.Parse("11111111-1111-1111-1111-111111111111")), new(Guid.NewGuid()), RelayChannelKind.Viewing, new(IPAddress.Loopback, 0));
        owner.Configure(runtime, fanout, _ => Task.CompletedTask, () => new(IPAddress.Loopback, 0), () => throw new InvalidOperationException());
        var registration = fixture.State.DownstreamRelays.Register(owner.ChannelId, owner.SessionId, owner.Kind, fanout);
        owner.Bind(registration);
        Assert.True(owner.TryReserve(90001, out var lease));
        await using var activation = new GenesisGuiNextRelayActivation((_, _) => ValueTask.FromResult(owner), shutdownTimeout: TimeSpan.FromMilliseconds(50));
        var start = activation.StartAsync(Valid);
        await disposalEntered.Task.WaitAsync(timeout.Token);
        Assert.False(await start);
        await activation.StopAsync().WaitAsync(timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Failed, activation.Snapshot.State);
        Assert.Equal(GenesisRelayActivationError.ShutdownFailed, activation.Snapshot.Error);
        Assert.False(activation.Snapshot.CleanupConfirmed);
        Assert.Equal(1, owner.ReservationCount);
        Assert.False(await activation.StartAsync(Valid));
        lease!.Dispose();
        release.SetResult();
        await UntilAsync(() => activation.Snapshot.CleanupConfirmed, timeout.Token);
        Assert.Equal(0, owner.ReservationCount);
    }

    [Fact]
    public async Task AcceptedHandshakeWithoutPacketTimesOutInsteadOfRunning()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        await using var activation = new GenesisGuiNextRelayActivation((channel, token) =>
            fixture.Channels.PrepareViewingAsync(channel, new(IPAddress.Loopback, 0), cancellationToken: token),
            readinessTimeout: TimeSpan.FromSeconds(2));
        var start = activation.StartAsync(upstream.Channel);
        await UntilAsync(() => activation.Snapshot.UpstreamReady, timeout.Token);
        Assert.Equal(GenesisRelayActivationState.Starting, activation.Snapshot.State);
        Assert.False(await start.WaitAsync(timeout.Token));
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.Equal(GenesisRelayActivationError.ReadinessTimedOut, activation.Snapshot.Error);
        Assert.True(activation.Snapshot.CleanupConfirmed);
        Assert.Equal(0, fixture.Channels.OwnerCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnapprovedOwnerRoleOrEndpointCannotActivate(bool broadcast)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        await using var producer = new PcpRelayFanout();
        GenesisGuiNextPublicChannelComposition.Owner? owner = null;
        await using var activation = new GenesisGuiNextRelayActivation(async (channel, token) =>
            owner = broadcast
                ? await fixture.Channels.PrepareBroadcastAsync(new(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                    new(Guid.NewGuid()), producer, new(IPAddress.Loopback, 0), cancellationToken: token)
                : await fixture.Channels.PrepareViewingAsync(channel, new(IPAddress.Any, 0), cancellationToken: token));
        Assert.False(await activation.StartAsync(Valid).WaitAsync(timeout.Token));
        await UntilAsync(() => activation.CanStart, timeout.Token);
        Assert.False(owner!.ActivationStarted);
        Assert.True(owner.CleanupConfirmed);
        Assert.Equal(GenesisRelayActivationError.StartFailed, activation.Snapshot.Error);
    }

    [Fact]
    public async Task CloseRetiresImmediatelyAndRejectsNewStartDisposeIsShared()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var activation = fixture.State.RelayActivation;
        var start = activation.StartAsync(upstream.Channel);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var owner = activation.CurrentOwner!;
        var first = activation.DisposeAsync().AsTask();
        Assert.True(owner.IsRetired);
        Assert.Same(first, activation.DisposeAsync().AsTask());
        Assert.False(await activation.StartAsync(upstream.Channel));
        await Task.WhenAll(first, activation.StopAsync()).WaitAsync(timeout.Token);
        Assert.True(activation.Snapshot.CleanupConfirmed);
        Assert.False(activation.CanStart);
    }

    private sealed class HeldLifetime(TaskCompletionSource entered, TaskCompletionSource release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { entered.TrySetResult(); await release.Task; }
    }
}

using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRelayTreeTests
{
    private static readonly ChannelId Channel = new(Guid.NewGuid());
    private static readonly ChannelSessionId Generation = new(Guid.NewGuid());
    private static readonly RelayAdmissionCapacitySnapshot Capacity = new(0, 10, 0, 4, null, null);
    private static PcpRelayTreeObservation Observation(params PcpRelayConnectionObservation[] children) =>
        new(Channel, Generation, true, false, null, null, PcpSmartRelayState.Disabled, Array.AsReadOnly(children), 0);
    private static GenesisRelayTreeSnapshot Project(PcpRelayTreeObservation observation, RelayChannelKind kind = RelayChannelKind.Viewing) =>
        GenesisDownstreamRelayOwnerRegistry.ProjectTree(observation, kind, Capacity, null, null);

    [Fact]
    public void ProjectionBoundsDeduplicatesAndNeverFillsMissingConnectionsOrRates()
    {
        var rows = Enumerable.Range(0, 100).Select(i => new PcpRelayConnectionObservation(i, PcpRelayConnectionPhase.Connected)).ToArray();
        rows[1] = rows[0];
        var tree = Project(Observation(rows));
        Assert.Equal(63, tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.Downstream));
        Assert.Equal(36, tree.OmittedConnections);
        Assert.All(tree.Nodes, n => Assert.Null(n.WriteKbps));
        Assert.DoesNotContain(tree.Nodes, n => n.Kind is GenesisRelayTreeNodeKind.Upstream or GenesisRelayTreeNodeKind.LocalInput);
        Assert.Equal(tree.Nodes.Count, tree.Nodes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(tree.Nodes, Project(Observation(rows)).Nodes);
        Assert.Throws<NotSupportedException>(() => ((IList<GenesisRelayTreeNode>)tree.Nodes).Clear());
        var connecting = Project(Observation() with { Upstream = new(null, PcpRelayConnectionPhase.Connecting) });
        Assert.Single(connecting.Nodes);
        Assert.Equal(PcpRelayConnectionPhase.Connecting, connecting.Upstream);
    }

    [Theory]
    [InlineData(PcpSmartRelayState.Recovering)]
    [InlineData(PcpSmartRelayState.Backoff)]
    [InlineData(PcpSmartRelayState.Discovering)]
    [InlineData(PcpSmartRelayState.Connecting)]
    [InlineData(PcpSmartRelayState.Negotiating)]
    public void RecoveryAndUnconfirmedCleanupHaveExplicitStates(PcpSmartRelayState recovery)
    {
        var observation = Observation(new PcpRelayConnectionObservation(2, PcpRelayConnectionPhase.Connected)) with
        { Upstream = new(1, PcpRelayConnectionPhase.Closing), Recovery = recovery };
        Assert.Equal(GenesisRelayTreeState.Recovering, Project(observation).State);
        Assert.Equal(GenesisRelayTreeState.CleanupUnconfirmed, Project(observation with { CleanupUnconfirmed = true }).State);
        var stopped = Project(observation with { Running = false, Recovery = PcpSmartRelayState.Stopped });
        Assert.Equal(GenesisRelayTreeState.Stopped, stopped.State);
        Assert.DoesNotContain(stopped.Nodes, n => n.Phase == PcpRelayConnectionPhase.Connected);
    }

    [Fact]
    public void PublishingInputIsNotAPcpPeerAndIndividualMeasurementNeverUsesAggregateRate()
    {
        var observation = Observation(new(2, PcpRelayConnectionPhase.Connected, PcpRelayObservedPath.ChannelLoopback),
            new(3, PcpRelayConnectionPhase.Connected, PcpRelayObservedPath.SharedPublicListener, new(10, 3.5, true, 0, false)),
            new(4, PcpRelayConnectionPhase.Connected, PcpRelayObservedPath.ChannelListener)) with
        { LocalInputConnected = true, Upstream = new(9, PcpRelayConnectionPhase.Connected) };
        var tree = GenesisDownstreamRelayOwnerRegistry.ProjectTree(observation, RelayChannelKind.Broadcast,
            Capacity, new(1234, 99, true, 0, false), null);
        Assert.DoesNotContain(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Upstream);
        Assert.Equal("input", Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.LocalNova).ParentId);
        Assert.Equal(GenesisRelayTreePath.PublishingLoopback, tree.Nodes.Single(n => n.Id == "down-2").Path);
        Assert.Null(tree.Nodes.Single(n => n.Id == "down-2").WriteKbps);
        Assert.Equal(3.5, tree.Nodes.Single(n => n.Id == "down-3").WriteKbps);
        Assert.Equal(GenesisRelayTreePath.Unknown, tree.Nodes.Single(n => n.Id == "down-4").Path);
        var presentation = GenesisGuiNextRelayTreePresentation.From(tree);
        Assert.Contains("ローカル配信入力（RTMP）", presentation.Tree);
        Assert.Contains("Unknown", presentation.Tree);
        Assert.Contains("99.0 kbps", presentation.Summary);
        Assert.DoesNotContain("99.0 kbps", presentation.Tree);
        Assert.Null(Assert.Single(Project(observation with { LocalInputConnected = false }, RelayChannelKind.Broadcast).Nodes,
            n => n.Kind == GenesisRelayTreeNodeKind.LocalNova).ParentId);
    }

    [Fact]
    public async Task DelayedOldReadCannotPublishAfterGenerationReplacementOrBlockRegistry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var policy = new RelayAdmissionPolicy();
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var old = registry.Register(Channel, Generation, RelayChannelKind.Viewing);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var oldSource = new Source(() => { entered.TrySetResult(); release.Wait(timeout.Token); return Observation(new PcpRelayConnectionObservation(100, PcpRelayConnectionPhase.Connected)); });
        old.ObserveSource(oldSource);
        var read = Task.Run(() => registry.ReadRelayTree(Channel), timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            using var current = registry.Register(Channel, new(Guid.NewGuid()), RelayChannelKind.Viewing);
            var source = new Source(() => Observation(new PcpRelayConnectionObservation(200, PcpRelayConnectionPhase.Connected)) with { OwnerId = current.SessionId });
            current.ObserveSource(source);
            release.Set();
            Assert.Equal(GenesisRelayTreeState.Unobserved, (await read.WaitAsync(timeout.Token)).State);
            var tree = registry.ReadRelayTree(Channel);
            Assert.Equal(current.SessionId, tree.OwnerId);
            Assert.Contains(tree.Nodes, n => n.Id == "down-C8");
            Assert.DoesNotContain(tree.Nodes, n => n.Id == "down-64");
            old.Dispose();
            Assert.Equal(current.SessionId, registry.ReadRelayTree(Channel).OwnerId);
            GC.KeepAlive(source);
        }
        finally { release.Set(); await read.WaitAsync(timeout.Token); GC.KeepAlive(oldSource); }
    }

    [Fact]
    public void MissingMismatchedDisposedSourcesAndRetiredReservationsNeverBecomeConnected()
    {
        using var policy = new RelayAdmissionPolicy();
        using var registry = new GenesisDownstreamRelayOwnerRegistry(policy);
        using var owner = registry.Register(Channel, Generation, RelayChannelKind.Viewing);
        Assert.Equal(GenesisRelayTreeState.Unobserved, registry.ReadRelayTree(Channel).State);
        var source = new Source(() => Observation(new PcpRelayConnectionObservation(2, PcpRelayConnectionPhase.Connected)) with { OwnerId = new(Guid.NewGuid()) });
        owner.ObserveSource(source);
        Assert.Single(registry.ReadRelayTree(Channel).Nodes);
        source.Read = () => Observation() with { ChannelId = new(Guid.NewGuid()) };
        Assert.Equal(GenesisRelayTreeState.Unobserved, registry.ReadRelayTree(Channel).State);
        source.Read = () => throw new ObjectDisposedException("private source");
        Assert.Equal(GenesisRelayTreeState.Unobserved, registry.ReadRelayTree(Channel).State);
        source.Read = () => Observation();
        Assert.True(owner.TryReserve(42, out var lease));
        using (lease)
        {
            owner.Dispose();
            var tree = registry.ReadRelayTree(Channel);
            Assert.Equal(GenesisRelayTreeState.CleanupUnconfirmed, tree.State);
            Assert.DoesNotContain(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
            using var next = registry.Register(Channel, new(Guid.NewGuid()), RelayChannelKind.Viewing);
            var nextSource = new Source(() => Observation() with { OwnerId = next.SessionId });
            next.ObserveSource(nextSource);
            Assert.Equal(GenesisRelayTreeState.CleanupUnconfirmed, registry.ReadRelayTree(Channel).State);
            lease!.Dispose();
            Assert.Equal(GenesisRelayTreeState.Active, registry.ReadRelayTree(Channel).State);
            GC.KeepAlive(nextSource);
        }
        registry.Dispose();
        Assert.Equal(GenesisRelayTreeState.Unobserved, registry.ReadRelayTree(Channel).State);
        GC.KeepAlive(source);
    }

    [Fact]
    public void NewObservationContractsContainNoPeerControlledStringsAddressesOrResources()
    {
        foreach (var type in new[] { typeof(PcpRelayTreeObservation), typeof(PcpRelayConnectionObservation) })
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(string) ||
                typeof(System.Net.EndPoint).IsAssignableFrom(p.PropertyType) || typeof(IDisposable).IsAssignableFrom(p.PropertyType));
        var providerProperty = typeof(GenesisDownstreamRelayOwnerRegistry.Owner).GetProperty("TreeSource",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Equal(typeof(WeakReference<IPcpRelayTreeSource>), providerProperty!.PropertyType);
    }

    [Fact]
    public async Task SelectionAndExistingStatusTickOnlyReadWithoutStartingNetworkOrPlayback()
    {
        var prepares = 0;
        await using var activation = new GenesisGuiNextRelayActivation((_, _) => { prepares++; throw new InvalidOperationException(); });
        var model = new GenesisMainViewModel(new FakePlaybackSessionController()) { RelayActivation = activation };
        var channel = new GenesisChannelItem("private title", $"pcp://127.0.0.1:1/channel/{Channel.Value:N}");
        var ticks = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GenesisMainViewModel.RelaySnapshot)) ticks++; };
        Assert.Equal(Channel, model.ReadRelayTree(channel).ChannelId);
        Assert.Null(model.ReadRelayTree(null).ChannelId);
        var other = ChannelIdFromNumber(2);
        Assert.Equal(other, model.ReadRelayTree(channel with { Source = $"pcp://127.0.0.1:1/channel/{other.Value:N}" }).ChannelId);
        model.RefreshRelayStatus();
        Assert.Equal(1, ticks);
        Assert.Equal(0, prepares);
        Assert.Equal(GenesisRelayActivationState.Stopped, activation.Snapshot.State);
    }

    private static ChannelId ChannelIdFromNumber(int value) => new(new Guid(value, 0, 0, new byte[8]));

    [Fact]
    public void ModernOnlyLayoutFitsSmallestCardWithoutHidingPrimaryOperations()
    {
        Assert.True(GenesisGuiNextRelayTreeLayout.Applies(true, GenesisGuiNextTab.Channels));
        Assert.False(GenesisGuiNextRelayTreeLayout.Applies(false, GenesisGuiNextTab.Channels));
        Assert.False(GenesisGuiNextRelayTreeLayout.Applies(true, GenesisGuiNextTab.Publishing));
        foreach (var width in new[] { GenesisMainWindowLayout.MinimumContentWidth, 1400 })
        {
            var layout = new GenesisGuiNextRelayTreeLayout(width);
            var card = new GenesisGuiNextChannelCardLayout(width);
            Assert.True(layout.Body.Top >= card.Title.Bottom);
            Assert.True(layout.Body.Bottom <= card.RecordingStatus.Top);
            Assert.True(layout.Body.Right < card.Metadata.Left);
            Assert.True(GenesisGuiNextRelayTreeLayout.Toggle.Left > GenesisGuiNextChannelCardLayout.RelayStop.Right);
            Assert.True(GenesisGuiNextRelayTreeLayout.Toggle.Right < card.RelayStatus.Left);
            Assert.True(GenesisGuiNextRelayTreeLayout.Toggle.Bottom < GenesisGuiNextAppShellPlacement.DetailsHeight);
        }
        Assert.Contains("情報未取得", GenesisGuiNextRelayTreePresentation.From(GenesisRelayTreeSnapshot.Empty(Channel)).Tree);
        Assert.Contains("チャンネルを選択", GenesisGuiNextRelayTreePresentation.From(GenesisRelayTreeSnapshot.Empty()).Tree);
    }

    private sealed class Source(Func<PcpRelayTreeObservation> read) : IPcpRelayTreeSource
    {
        internal Func<PcpRelayTreeObservation> Read { get; set; } = read;
        public PcpRelayTreeObservation ObserveRelayTree() => Read();
    }
}

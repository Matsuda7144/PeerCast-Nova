using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextTopStatusPresentationTests
{
    [Fact]
    public void UnavailableMeasurementsRemainExplicitlyUnknown()
    {
        Assert.Equal(
            "接続 — | P2Pノード数 — | 上り — | 下り — | ポート開放 —",
            GenesisGuiNextTopStatusPresentation.FormatUnavailable());
    }

    [Fact]
    public void OnlyAnObservedReceivingRelayCountsAsConnectedAndStopsDoNotRetainIt()
    {
        var active = new GenesisRelayActivationSnapshot(State: GenesisRelayActivationState.Running,
            Generation: 4, ListenerBound: true, UpstreamReady: true, ReceivingReady: true, CleanupConfirmed: false);
        Assert.Equal("接続中", GenesisGuiNextTopStatusPresentation.Create(active, null).Connection.Text);
        Assert.Equal("接続中", GenesisGuiNextTopStatusPresentation.Create(active with { Error = GenesisRelayActivationError.AlreadyRunning }, null).Connection.Text);
        Assert.Equal("未接続", GenesisGuiNextTopStatusPresentation.Create(new(Error: GenesisRelayActivationError.NoSelection), null).Connection.Text);
        Assert.Equal("準備中", GenesisGuiNextTopStatusPresentation.Create(active with
        { State = GenesisRelayActivationState.Starting, Generation = 5, ReceivingReady = false }, null).Connection.Text);
        Assert.Equal("未接続", GenesisGuiNextTopStatusPresentation.Create(new(), null).Connection.Text);
        Assert.Equal("—", GenesisGuiNextTopStatusPresentation.Create(null, null).Connection.Text);
        Assert.Equal("—", GenesisGuiNextTopStatusPresentation.Create(active with { ReceivingReady = false }, null).Connection.Text);
        Assert.Equal("解除未確認", GenesisGuiNextTopStatusPresentation.Create(active with
        { State = GenesisRelayActivationState.Failed }, null).Connection.Text);
        Assert.Equal("エラー", GenesisGuiNextTopStatusPresentation.Create(new(State: GenesisRelayActivationState.Failed, Error: GenesisRelayActivationError.BindFailed), null).Connection.Text);
    }

    [Fact]
    public void UnsupportedPeerAndWireCountersAreNeverInventedAsZero()
    {
        foreach (var state in Enum.GetValues<GenesisRelayActivationState>())
        {
            var value = GenesisGuiNextTopStatusPresentation.Create(new(State: state), Network());
            foreach (var index in new[] { 1, 2, 3 }) Assert.Equal("—", value.Value(index).Text);
            Assert.Contains("全体計測未対応", value.AccessibleText);
            Assert.Contains("明示Relayのみ", value.AccessibleText);
        }
    }

    [Fact]
    public void ListeningMappingAndExternallyReachableAreDistinct()
    {
        var network = Network();
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null, network).Port.Text);
        var mapped = network with { Nat = Mapping() };
        var value = GenesisGuiNextTopStatusPresentation.Create(null, mapped);
        Assert.Equal("マッピング済み・外部到達未確認", value.Port.Text);
        Assert.NotEqual(GenesisGuiNextDesign.Success, value.Port.Color);
        Assert.Equal("外部到達確認済み", GenesisGuiNextTopStatusPresentation.Create(null,
            mapped with { Nat = mapped.Nat! with { ExternalReachability = NatDisplayReachability.Reachable } }).Port.Text);
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null,
            mapped with { Nat = mapped.Nat! with { InternalPort = 7101 } }).Port.Text);
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null,
            mapped with { PublicRelay = network.PublicRelay! with { State = NovaPublicRelayState.Stopped } }).Port.Text);
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null,
            mapped with { Nat = mapped.Nat! with { LeaseEvidence = NatLeaseEvidenceKind.RequestedEstimate } }).Port.Text);
    }

    [Fact]
    public void MissingNewGenerationOrDisposedObservationDoesNotReuseOldPortSuccess()
    {
        var network = Network() with { Nat = Mapping() };
        Assert.Equal("マッピング済み・外部到達未確認", GenesisGuiNextTopStatusPresentation.Create(null, network).Port.Text);
        Assert.Equal("—", GenesisGuiNextTopStatusPresentation.Create(null, null).Port.Text);
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null, network with { Revision = 5, Nat = null }).Port.Text);
        Assert.Equal("未待受", GenesisGuiNextTopStatusPresentation.Create(null, network with { Disposed = true }).Port.Text);
        Assert.Equal("解除未確認", GenesisGuiNextTopStatusPresentation.Create(null, network with
        { Disposed = true, ShutdownFailure = NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed }).Port.Text);
        Assert.Equal("未待受", GenesisGuiNextTopStatusPresentation.Create(null, network with
        { PublicListenerAvailability = NovaApplicationPublicListenerAvailability.Unavailable }).Port.Text);
    }

    [Theory]
    [InlineData(NatDisplayState.Discovering, "ルーター検出中")]
    [InlineData(NatDisplayState.Mapping, "開放・解除処理中")]
    [InlineData(NatDisplayState.ResultUnknown, "処理結果未確認")]
    [InlineData(NatDisplayState.UnmapUnconfirmed, "解除未確認")]
    [InlineData(NatDisplayState.PortConflict, "マッピングエラー")]
    public void MappingProgressAndUnconfirmedCleanupStayExplicit(NatDisplayState state, string expected)
    {
        Assert.Equal(expected, GenesisGuiNextTopStatusPresentation.Create(null, Network() with { Nat = Mapping() with { State = state } }).Port.Text);
    }

    [Fact]
    public void SharedStartupListenerShowsOnlyItsOwnConfirmedMapping()
    {
        var network = Network() with
        {
            PublicRelay = null,
            SharedPublicRelay = new(GenesisSharedPublicRelayState.Listening, 7100, 0, null, false, null),
            Nat = Mapping(),
        };
        Assert.Equal("マッピング済み・外部到達未確認", GenesisGuiNextTopStatusPresentation.Create(null, network).Port.Text);
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null,
            network with { SharedPublicRelay = network.SharedPublicRelay with { Port = 7101 } }).Port.Text);
        Assert.Equal("待受中・到達未確認", GenesisGuiNextTopStatusPresentation.Create(null,
            network with { Nat = network.Nat with { RegisteredInternalPorts = [] } }).Port.Text);
    }

    private static NovaApplicationNetworkSnapshot Network() => new(
        true, false, false, false, 1, false, false, false, null, false, 0, 0, 0, null,
        NovaApplicationNetworkFailure.None, NovaApplicationNetworkFailure.None, null, null,
        NovaApplicationPublicListenerAvailability.Listening)
    { PublicRelay = new(Guid.NewGuid(), 1, 1, NovaPublicRelayState.Listening, 1, null, null, new("0.0.0.0", 7100), NovaPublicRelayFailure.None) };

    private static NatPortMappingDisplay Mapping() => new(
        true, 1, "UPnP", null, NatMappingProtocol.Tcp, [7100], NatDisplayState.Mapped, NatDisplayReachability.NotChecked,
        null, 7100, 120, new(120, 120, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(120)),
        NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry, DateTimeOffset.UtcNow, null, [])
    { InternalPort = 7100 };
}

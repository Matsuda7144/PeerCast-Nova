using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsPresentationTests
{
    private static readonly int[] Port7144 = new[] { 7144 };
    private static readonly DateTimeOffset CheckedAt =
        new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateProjectsAppliedListenerAndConnectionCounts()
    {
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            CreateCapabilities(supportsDirectPlay: true, supportsIpv6: true),
            GenesisRelayListenerRuntimeState.Listening,
            activeRelayConnections: 2,
            relayConnectionLimit: 8,
            addressFamily: AddressFamily.InterNetwork,
            boundAddress: IPAddress.Any,
            boundPort: 7144,
            allowsLocalRelay: true,
            allowsGlobalRelay: true,
            activeDirectListeners: 1);

        var presentation = GenesisNetworkSettingsPresentation.Create(
            CreateViewModel(runtime));

        Assert.Equal("Listening", presentation.ListenerStateText);
        Assert.Equal("0.0.0.0:7144", presentation.BoundEndpointText);
        Assert.Equal("2 / 8", presentation.RelayConnectionsText);
        Assert.Equal("1", presentation.DirectListenersText);
        Assert.Equal("Unknown", presentation.Ipv4ReachabilityText);
        Assert.Equal("Unknown", presentation.Ipv6ReachabilityText);
        Assert.Equal("—", presentation.ExternalIpv4Text);
        Assert.Equal("—", presentation.ExternalIpv6Text);
    }

    [Fact]
    public void CreateDoesNotPresentPerChannelEndpointAsSharedListener()
    {
        var capabilities = new GenesisRelayNetworkCapabilities(
            SupportsSharedListener: false,
            SupportsLocalRelayPolicy: false,
            SupportsGlobalRelayPolicy: false,
            SupportsDirectPlay: false,
            SupportsAutomaticPortMapping: false,
            SupportsIpv6: false);
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            capabilities,
            GenesisRelayListenerRuntimeState.Listening,
            activeRelayConnections: 1,
            relayConnectionLimit: 4,
            addressFamily: AddressFamily.InterNetwork,
            boundAddress: IPAddress.Any,
            boundPort: 7144);

        var presentation = GenesisNetworkSettingsPresentation.Create(
            CreateViewModel(runtime));

        Assert.Equal("Unavailable", presentation.ListenerStateText);
        Assert.Equal("—", presentation.BoundEndpointText);
        Assert.Equal("1 / 4", presentation.RelayConnectionsText);
    }

    [Fact]
    public void CreateProjectsIpv6EndpointAndExternalReachabilityEvidence()
    {
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            CreateCapabilities(supportsDirectPlay: false, supportsIpv6: true),
            GenesisRelayListenerRuntimeState.Listening,
            activeRelayConnections: 0,
            addressFamily: AddressFamily.InterNetworkV6,
            boundAddress: IPAddress.IPv6Any,
            boundPort: 7144,
            allowsLocalRelay: true,
            allowsGlobalRelay: false);
        var ipv4Request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            Port7144);
        var ipv6Request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetworkV6,
            Port7144);
        var ipv4Result = GenesisNetworkReachabilityResult.Open(
            ipv4Request,
            Port7144,
            TimeSpan.FromMilliseconds(120),
            CheckedAt,
            IPAddress.Parse("198.51.100.7"));
        var ipv6Result = GenesisNetworkReachabilityResult.Closed(
            ipv6Request,
            TimeSpan.FromMilliseconds(180),
            CheckedAt.AddSeconds(1),
            IPAddress.Parse("2001:db8::7"));

        var presentation = GenesisNetworkSettingsPresentation.Create(
            CreateViewModel(runtime),
            GenesisNetworkReachabilityState.Open,
            ipv4Result,
            GenesisNetworkReachabilityState.Closed,
            ipv6Result);

        Assert.Equal("[::]:7144", presentation.BoundEndpointText);
        Assert.Equal("Unavailable", presentation.DirectListenersText);
        Assert.Equal("Open (7144)", presentation.Ipv4ReachabilityText);
        Assert.Equal("198.51.100.7", presentation.ExternalIpv4Text);
        Assert.Equal("Closed", presentation.Ipv6ReachabilityText);
        Assert.Equal("2001:db8::7", presentation.ExternalIpv6Text);
    }

    [Fact]
    public void CreateKeepsUnsupportedCapabilitiesExplicitlyUnavailable()
    {
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            GenesisRelayNetworkCapabilities.None,
            GenesisRelayListenerRuntimeState.Unavailable,
            activeRelayConnections: 0);

        var presentation = GenesisNetworkSettingsPresentation.Create(
            CreateViewModel(runtime),
            ipv4State: GenesisNetworkReachabilityState.Failed,
            ipv6State: GenesisNetworkReachabilityState.Open);

        Assert.Equal("Unavailable", presentation.ListenerStateText);
        Assert.Equal("—", presentation.BoundEndpointText);
        Assert.Equal("Failed", presentation.Ipv4ReachabilityText);
        Assert.Equal("Unavailable", presentation.Ipv6ReachabilityText);
        Assert.Equal("Unavailable", presentation.ExternalIpv6Text);
        Assert.Equal("Unavailable", presentation.DirectListenersText);
    }

    [Fact]
    public void CreateRejectsReachabilityEvidenceFromWrongAddressFamily()
    {
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            CreateCapabilities(supportsDirectPlay: false, supportsIpv6: true),
            GenesisRelayListenerRuntimeState.Stopped,
            activeRelayConnections: 0);
        var ipv6Result = GenesisNetworkReachabilityResult.Closed(
            new GenesisNetworkReachabilityRequest(
                AddressFamily.InterNetworkV6,
                Port7144),
            TimeSpan.Zero,
            CheckedAt);

        Assert.Throws<ArgumentException>(() =>
            GenesisNetworkSettingsPresentation.Create(
                CreateViewModel(runtime),
                ipv4State: GenesisNetworkReachabilityState.Closed,
                ipv4Result: ipv6Result));
    }

    private static GenesisNetworkSettingsViewModel CreateViewModel(
        GenesisRelayNetworkStatusSnapshot runtime)
    {
        var settings = new GenesisNetworkBasicSettings(
            runtime.BoundPort ?? 7144,
            ipv6Enabled: runtime.AddressFamily == AddressFamily.InterNetworkV6,
            automaticPortMappingEnabled: false,
            allowLocalRelay: runtime.AllowsLocalRelay,
            allowGlobalRelay: runtime.AllowsGlobalRelay);
        return new GenesisNetworkSettingsViewModel(
            new FixedNetworkSettingsController(
                new GenesisNetworkSettingsAppliedSnapshot(settings, runtime)));
    }

    private static GenesisRelayNetworkCapabilities CreateCapabilities(
        bool supportsDirectPlay,
        bool supportsIpv6) => new(
        SupportsSharedListener: true,
        SupportsLocalRelayPolicy: true,
        SupportsGlobalRelayPolicy: true,
        SupportsDirectPlay: supportsDirectPlay,
        SupportsAutomaticPortMapping: false,
        SupportsIpv6: supportsIpv6);

    private sealed class FixedNetworkSettingsController :
        IGenesisNetworkSettingsController
    {
        public FixedNetworkSettingsController(
            GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot { get; }

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
    }
}

using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingAdvancedAssistTests
{
    [Fact]
    public void TryBuildAnnouncePresetUsesFallbackTrackerPortOrDefaultsTo7146()
    {
        var withTracker = new GenesisYellowPageDefinition(
            "yp1",
            "テストYP",
            new Uri("https://yp.example.invalid/index.txt"),
            new Uri("pcp://tracker.example.invalid:7148/"));

        Assert.True(PublishingAdvancedAssist.TryBuildAnnouncePreset(withTracker, out var presetWithPort));
        Assert.NotNull(presetWithPort);
        Assert.Equal("テストYP", presetWithPort.Name);
        Assert.Equal("pcp://tracker.example.invalid:7148", presetWithPort.Endpoint);
        Assert.False(presetWithPort.UsedFallbackPort);

        var indexOnly = new GenesisYellowPageDefinition(
            "yp2",
            "インデックスのみYP",
            new Uri("https://index.example.invalid/index.txt"));

        Assert.True(PublishingAdvancedAssist.TryBuildAnnouncePreset(indexOnly, out var fallbackPreset));
        Assert.NotNull(fallbackPreset);
        Assert.Equal("インデックスのみYP", fallbackPreset.Name);
        Assert.Equal("pcp://index.example.invalid:7146", fallbackPreset.Endpoint);
        Assert.True(fallbackPreset.UsedFallbackPort);
    }

    [Fact]
    public void TryBuildAnnouncePresetRejectsIpv6Host()
    {
        var ipv6Yp = new GenesisYellowPageDefinition(
            "yp-v6",
            "IPv6 YP",
            new Uri("http://[2001:db8::1]:7144/index.txt"));

        Assert.False(PublishingAdvancedAssist.TryBuildAnnouncePreset(ipv6Yp, out var preset));
        Assert.Null(preset);
    }

    [Fact]
    public void TryApplyNatSuggestionPopulatesWanFieldsAndMatchingNicFromNatDisplay()
    {
        var input = PublishingSettingsInput.From(PublishingChannelPreferences.Default);
        var natDisplay = new NatPortMappingDisplay(
            Enabled: true,
            Generation: 1,
            Backend: "UPnP",
            Network: new NatNetworkIdentity(new NatDeviceIdentity("192.168.10.1", "gw"), "192.168.10.25", 1),
            Protocol: NatMappingProtocol.Tcp,
            RegisteredInternalPorts: [7144],
            State: NatDisplayState.Mapped,
            ExternalReachability: NatDisplayReachability.Reachable,
            ExternalAddress: "198.41.0.4",
            ExternalPort: 17144,
            RequestedLifetimeSeconds: 3600,
            Lease: null,
            LeaseEvidence: NatLeaseEvidenceKind.None,
            CapturedAt: DateTimeOffset.UtcNow,
            LastObservation: null,
            Diagnostics: []);
        var snapshot = new NovaApplicationNetworkSnapshot(
            Initialized: true,
            Initializing: false,
            Disposing: false,
            Disposed: false,
            Revision: 1,
            RuntimeDesiredEnabled: true,
            SavedAutoPortMapping: true,
            StartupEnvironmentOverride: false,
            StartupEnvironmentEnabled: null,
            NextStartEffectiveEnabled: true,
            PendingSaveCount: 0,
            LastAcceptedRequestId: 1,
            LastPersistedRequestId: 1,
            LastPersistenceResult: null,
            InitializationFailure: NovaApplicationNetworkFailure.None,
            ShutdownFailure: NovaApplicationNetworkFailure.None,
            ShutdownOutcome: null,
            Nat: natDisplay,
            PublicListenerAvailability: NovaApplicationPublicListenerAvailability.Listening);
        ObsPublishLanInterface[] interfaces =
        [
            new("eth0", "Ethernet", true, "192.168.10.25", "255.255.255.0", 24),
            new("wlan0", "Wi-Fi", true, "192.168.20.12", "255.255.255.0", 24),
        ];

        var result = PublishingAdvancedAssist.TryApplyNatSuggestion(input, snapshot, interfaces, 7144);

        Assert.True(result.Applied);
        Assert.True(result.UpdatedInput.WanEnabled);
        Assert.False(result.UpdatedInput.LanEnabled);
        Assert.Equal("eth0", result.UpdatedInput.WanInterfaceId);
        Assert.Equal("192.168.10.25", result.UpdatedInput.WanAddress);
        Assert.Equal("7144", result.UpdatedInput.WanPort);
        Assert.Equal("198.41.0.4", result.UpdatedInput.AdvertisedAddress);
        Assert.Equal("17144", result.UpdatedInput.AdvertisedPort);
    }

    [Fact]
    public void TryApplyNatSuggestionReturnsGuidanceWhenPublicAddressIsMissing()
    {
        var input = PublishingSettingsInput.From(PublishingChannelPreferences.Default);
        var result = PublishingAdvancedAssist.TryApplyNatSuggestion(input, network: null, [], 7144);

        Assert.False(result.Applied);
        Assert.Equal(input, result.UpdatedInput);
        Assert.Contains("公開IPv4", result.Message, StringComparison.Ordinal);
    }
}

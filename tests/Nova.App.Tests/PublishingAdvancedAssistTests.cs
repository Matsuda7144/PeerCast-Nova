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

    [Fact]
    public void TryBuildAllAnnouncePresetsFiltersDisabledAndDeduplicatesByEndpoint()
    {
        var entries = new[]
        {
            new GenesisYellowPageDefinition(
                "yp1",
                "第一YP",
                new Uri("https://yp1.example.invalid/index.txt"),
                new Uri("pcp://tracker1.example.invalid:7148/"),
                isEnabled: true),
            new GenesisYellowPageDefinition(
                "yp-disabled",
                "無効YP",
                new Uri("https://disabled.example.invalid/index.txt"),
                new Uri("pcp://disabled.example.invalid:7146/"),
                isEnabled: false),
            new GenesisYellowPageDefinition(
                "yp-dup",
                "重複YP",
                new Uri("https://yp1.example.invalid/index.txt"),
                new Uri("pcp://TRACKER1.EXAMPLE.INVALID:7148/"),
                isEnabled: true),
            new GenesisYellowPageDefinition(
                "yp2",
                "第二YP",
                new Uri("https://yp2.example.invalid/index.txt"),
                isEnabled: true),
        };

        var presets = PublishingAdvancedAssist.TryBuildAllAnnouncePresets(entries);

        Assert.Equal(2, presets.Count);
        Assert.Equal("第一YP", presets[0].Name);
        Assert.Equal("pcp://tracker1.example.invalid:7148", presets[0].Endpoint);
        Assert.Equal("第二YP", presets[1].Name);
        Assert.Equal("pcp://yp2.example.invalid:7146", presets[1].Endpoint);
    }

    [Fact]
    public void TryApplyExternalAddressSuggestionAcceptsTrackerPublicIpv4AndRejectsPrivateAddress()
    {
        var input = PublishingSettingsInput.From(PublishingChannelPreferences.Default);
        var interfaces = new[]
        {
            new ObsPublishLanInterface("nic-1", "Ethernet", true, "192.168.10.25", "255.255.255.0", 24),
        };

        var applied = PublishingAdvancedAssist.TryApplyExternalAddressSuggestion(
            input,
            "198.41.0.4",
            network: null,
            interfaces,
            7144,
            sourceLabel: "YP Tracker");

        Assert.True(applied.Applied);
        Assert.True(applied.UpdatedInput.WanEnabled);
        Assert.False(applied.UpdatedInput.LanEnabled);
        Assert.Equal("nic-1", applied.UpdatedInput.WanInterfaceId);
        Assert.Equal("192.168.10.25", applied.UpdatedInput.WanAddress);
        Assert.Equal("7144", applied.UpdatedInput.WanPort);
        Assert.Equal("198.41.0.4", applied.UpdatedInput.AdvertisedAddress);
        Assert.Equal("7144", applied.UpdatedInput.AdvertisedPort);
        Assert.Contains("YP Tracker", applied.Message, StringComparison.Ordinal);

        var rejected = PublishingAdvancedAssist.TryApplyExternalAddressSuggestion(
            input,
            "192.168.10.1",
            network: null,
            interfaces,
            7144,
            sourceLabel: "YP Tracker");

        Assert.False(rejected.Applied);
        Assert.Equal(input, rejected.UpdatedInput);
    }

    [Fact]
    public async Task JsonPublishingPreferencesStoreRoundTripsAdditionalAnnounceTargets()
    {
        var tempDir = Directory.CreateTempSubdirectory("nova-pub-multiyp-");
        try
        {
            var path = Path.Combine(tempDir.FullName, "publishing-preferences.json");
            var store = new JsonPublishingPreferencesStore(path);
            var preferences = PublishingChannelPreferences.Default with
            {
                Wan = new ObsPublishWanOptions(true, "nic-1", "192.168.10.25", 17144, "198.41.0.4", 27144),
                Announce = new PublishingAnnounceOptions(true, "Primary", "pcp://198.41.0.5:7144")
                {
                    AdditionalTargets =
                    [
                        new PublishingAnnounceTarget("Sub 1", "pcp://198.41.0.6:7144"),
                        new PublishingAnnounceTarget("Sub 2", "pcp://198.41.0.7:7146"),
                    ],
                },
            };

            await store.SaveAsync(PublishingSettingsSnapshot.Default with { SettingsRevision = 1, Values = preferences });
            var loaded = await store.LoadAsync();

            Assert.Equal(preferences, loaded.Values);
            var targets = loaded.Values.Announce.GetAllTargets();
            Assert.Equal(3, targets.Count);
            Assert.Equal("Sub 1", targets[1].Name);
            Assert.Equal("pcp://198.41.0.7:7146", targets[2].Endpoint);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}

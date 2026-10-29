using System.Text.Json;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingWanTests
{
    private static ObsPublishWanOptions Wan => new(true, "nic", "192.168.42.5", 17144, "198.41.0.4", 27144);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchemaTwoMigratesFaithfullyWithoutEnablingWan(bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "publishing.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                SchemaVersion = 2,
                SettingsRevision = 7,
                Lan = new ObsPublishLanOptions(enabled, "nic", "192.168.42.5", 17144)
            }));
            var store = new JsonPublishingPreferencesStore(path);
            var before = await store.LoadAsync();
            Assert.Equal(enabled, before.Values.Lan.Enabled);
            Assert.False(before.Values.Wan.Enabled);
            Assert.Equal(enabled ? PublishingRelayMode.Lan : PublishingRelayMode.Loopback, before.Values.RelayMode);
            await store.SaveAsync(before);
            Assert.Equal(before.Values, (await store.LoadAsync()).Values);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task WanSaveOnlyChangesSavedRevisionAndSchemaThreeDoesNotMutateAppliedOrBind()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "publishing.json");
            await using var settings = new PublishingSettingsController(new JsonPublishingPreferencesStore(path));
            await settings.LoadAsync();
            Assert.Equal(PublishingRelayMode.Loopback, settings.Saved.Values.RelayMode);
            settings.MarkApplied(settings.Saved);
            var applied = settings.Applied;
            using var editor = new PublishingSettingsEditor(settings);
            editor.Edit(PublishingSettingsInput.From(settings.Saved.Values) with
            {
                WanEnabled = true,
                WanInterfaceId = Wan.InterfaceId,
                WanAddress = Wan.Address,
                WanPort = "17144",
                AdvertisedAddress = Wan.AdvertisedAddress,
                AdvertisedPort = "27144",
            });
            Assert.False(settings.CanStart);
            await editor.SaveAsync();
            Assert.True(editor.Result.Saved);
            Assert.Same(applied, settings.Applied);
            Assert.Equal(Wan, settings.Saved.Values.Wan);
            Assert.False(settings.Saved.Values.Lan.Enabled);
            Assert.Equal(1, settings.Saved.SettingsRevision);
            var loaded = await new JsonPublishingPreferencesStore(path).LoadAsync();
            Assert.Equal(Wan, loaded.Values.Wan);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(3, json.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(2, json.RootElement.GetProperty("RelayMode").GetInt32());
            Assert.False(loaded.Values.ToOptions().Lan.Enabled);
            // Save has no listener/transport dependency. Production announcement isolation is checked separately.
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":3}")]
    [InlineData("{\"SchemaVersion\":3,\"RelayMode\":2,\"Lan\":{},\"Wan\":{}}")]
    [InlineData("{\"SchemaVersion\":3,\"RelayMode\":99,\"Lan\":{},\"Wan\":{}}")]
    [InlineData("{\"SchemaVersion\":3,\"RelayMode\":0,\"Lan\":{},\"Wan\":{\"Address\":null}}")]
    [InlineData("{\"SchemaVersion\":4,\"RelayMode\":0,\"Lan\":{},\"Wan\":{}}")]
    public async Task InvalidSchemaThreeIsPreservedAndBlocksStart(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "publishing.json");
            await File.WriteAllTextAsync(path, json);
            await using var settings = new PublishingSettingsController(new JsonPublishingPreferencesStore(path));
            await settings.LoadAsync();
            Assert.False(settings.CanStart);
            Assert.Equal(json, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task NumericBandwidthRejectsWanBeforeAnyBind(int budget)
    {
        await using var fixture = await PublicChannelTestFixture.CreateAsync(new() { TotalUpstreamIpv4Kbps = budget });
        var factory = new SystemNovaObsPublishingRuntimeFactory(fixture.State);
        await using var host = new ObsPublishChannelHost(new() { Wan = Wan }, interfaces: new Interfaces(), registerRelay: factory.Register);
        host.Start();
        var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(ObsPublishWanFailure.BandwidthUnsupported, host.PublishingStatus.WanRelay!.Failure);
        Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(0, fixture.State.PublicChannels.OwnerCount);
    }

    [Fact]
    public void PresentationSeparatesBoundDeclaredAndUnknownReachabilityWithoutConnectableUrl()
    {
        var status = new ObsPublishWanStatus(true, true, Wan.Address, Wan.Port, Wan.AdvertisedAddress, Wan.AdvertisedPort, ObsPublishWanFailure.None, false);
        var text = PublishingWanPresentation.Format(status);
        Assert.Contains($"{Wan.Address}:{Wan.Port}", text);
        Assert.Contains($"{Wan.AdvertisedAddress}:{Wan.AdvertisedPort}", text);
        Assert.Contains("外部到達性未確認", text);
        Assert.Contains("YP掲載OFF", text);
        Assert.DoesNotContain("pcp://", text);
        Assert.DoesNotContain(Wan.Address, PublishingWanPresentation.Format(status with { ListenerReady = false }));
        Assert.DoesNotContain(Wan.Address, PublishingWanPresentation.Format(status with { CleanupIncomplete = true }));
        Assert.Throws<ArgumentException>(() => (PublishingSettingsInput.From(PublishingChannelPreferences.Default) with
        { WanEnabled = true, LanEnabled = true, WanInterfaceId = Wan.InterfaceId, WanAddress = Wan.Address, WanPort = "17144" }).Validate());
    }

    private sealed class Interfaces : IObsPublishLanInterfaces
    {
        public IReadOnlyList<ObsPublishLanInterface> Read() => [new("nic", "Fake NIC", true, Wan.Address, "255.255.255.0", 24)];
    }
}

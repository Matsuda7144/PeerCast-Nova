using System.Text.Json;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingAnnounceSettingsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task OlderSchemasCannotEnableAnnounce(int schema)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                SchemaVersion = schema,
                RelayMode = 0,
                Lan = new ObsPublishLanOptions(),
                Wan = new ObsPublishWanOptions(),
                Announce = new PublishingAnnounceOptions(true, "unexpected", "pcp://198.41.0.5:7144"),
            }));
            var settings = await new JsonPublishingPreferencesStore(path).LoadAsync();
            Assert.False(settings.Values.Announce.Enabled);
            Assert.Equal(PublishingRelayMode.Loopback, settings.Values.RelayMode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SaveAndLoadDoNotResolveOrConnectAndPreserveExplicitIntent()
    {
        var path = Path.GetTempFileName();
        try
        {
            var store = new JsonPublishingPreferencesStore(path);
            var values = PublishingChannelPreferences.Default with
            {
                Wan = new(true, "fake nic", "192.168.1.2", 7144, "198.41.0.4", 17144),
                Announce = new(true, "test", "pcp://unresolved.example:7144"),
            };
            await store.SaveAsync(PublishingSettingsSnapshot.Default with { SettingsRevision = 7, Values = values });
            var loaded = await store.LoadAsync();
            Assert.Equal(values, loaded.Values);
            Assert.Equal(7, loaded.SettingsRevision);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(4, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnnounceOnRequiresExplicitWanAndAdvertisedEndpoint()
    {
        Assert.Throws<ArgumentException>(() => (PublishingChannelPreferences.Default with
        {
            Announce = new(true, "test", "pcp://198.41.0.5:7144"),
        }).Validate());
    }
}

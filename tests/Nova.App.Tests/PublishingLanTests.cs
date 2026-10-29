using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingLanTests
{
    private static ObsPublishLanOptions Lan => new(true, "nic", "192.168.42.5", 17144);

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task NumericBandwidthFailsBeforeRegistration(int budget)
    {
        await using var fixture = await PublicChannelTestFixture.CreateAsync(new() { TotalUpstreamIpv4Kbps = budget });
        await using var fanout = new PcpRelayFanout();
        var factory = new SystemNovaObsPublishingRuntimeFactory(fixture.State);
        var exception = Assert.Throws<ObsPublishLanException>(() => factory.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), fanout));
        Assert.Equal(ObsPublishLanFailure.BandwidthUnsupported, exception.Failure);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
        Assert.Equal(0, fixture.State.PublicChannels.OwnerCount);
    }

    [Fact]
    public async Task NoContextOrRetiredContextCannotFallOpen()
    {
        await using var fixture = await PublicChannelTestFixture.CreateAsync();
        await using var fanout = new PcpRelayFanout();
        var id = new ChannelId(Guid.NewGuid());
        var session = new ChannelSessionId(Guid.NewGuid());
        Assert.Throws<ObsPublishLanException>(() => new SystemNovaObsPublishingRuntimeFactory().Register(id, session, fanout));
        Assert.Throws<ObsPublishLanException>(() => new SystemNovaObsPublishingRuntimeFactory(fixture.State, allowLan: false).Register(id, session, fanout));
        await fixture.DisposeAsync();
        Assert.Throws<ObsPublishLanException>(() => new SystemNovaObsPublishingRuntimeFactory(fixture.State).Register(id, session, fanout));
    }

    [Fact]
    public async Task SchemaOneMigratesOffAndSchemaTwoRoundTripsNextStartWithoutLiveMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "publishing.json");
            await File.WriteAllTextAsync(path, """{"SchemaVersion":1,"SettingsRevision":7,"ChannelName":"old","Hint":42}""");
            await using var settings = new PublishingSettingsController(new JsonPublishingPreferencesStore(path));
            await settings.LoadAsync();
            Assert.False(settings.Saved.Values.Lan.Enabled);
            settings.MarkApplied(settings.Saved);
            var applied = settings.Applied;
            var input = PublishingSettingsInput.From(settings.Saved.Values) with
            { LanEnabled = true, LanInterfaceId = Lan.InterfaceId, LanAddress = Lan.Address, LanPort = "17144" };
            using var editor = new PublishingSettingsEditor(settings);
            editor.Edit(input);
            Assert.False(settings.CanStart);
            await editor.SaveAsync();
            Assert.True(editor.Result.Saved);
            Assert.Same(applied, settings.Applied);
            Assert.True(settings.Saved.Values.Lan.Enabled);
            Assert.Equal(8, settings.Saved.SettingsRevision);
            var loaded = await new JsonPublishingPreferencesStore(path).LoadAsync();
            Assert.Equal(Lan, loaded.Values.Lan);
            Assert.Equal(42, loaded.Extensions["Hint"].GetInt32());
            using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(3, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("host.invalid")]
    public void InvalidLanPortCannotBecomeSavedSettings(string value) =>
        Assert.Throws<ArgumentException>(() => (PublishingSettingsInput.From(PublishingChannelPreferences.Default) with
        { LanEnabled = true, LanInterfaceId = Lan.InterfaceId, LanAddress = Lan.Address, LanPort = value }).Validate());

    [Fact]
    public void OnlyReadyValidatedLanEndpointProducesExistingPcpUrl()
    {
        var id = Guid.NewGuid();
        var status = new ObsPublishLanStatus(true, true, "192.168.42.5", 17144, ObsPublishLanFailure.None);
        var text = PublishingLanPresentation.Format(status, id);
        Assert.Contains("帯域制限未適用", text);
        var url = text.Split("\r\n")[1];
        Assert.True(DailyPcpPlaybackSourceParser.TryParse(url, out var parsed));
        Assert.Equal(id, parsed!.ChannelId.Value);
        Assert.Equal(17144, parsed.Port);
        foreach (var other in new[] { status with { Ready = false }, status with { Failure = ObsPublishLanFailure.BindFailed },
            status with { Address = "0.0.0.0" }, status with { Port = 0 }, status with { Enabled = false } })
            Assert.DoesNotContain("pcp://", PublishingLanPresentation.Format(other, id));
    }

    [Fact]
    public async Task BindFailureRetiresRealRegistrationAndRetainsUnreleasedReservationProof()
    {
        // No LAN connection or physical NIC bind: deliberately choose a currently unassigned private IP.
        var assigned = SystemObsPublishLanInterfaces.Instance.Read().Select(item => item.Address).ToHashSet();
        var address = Enumerable.Range(1, 254).Select(n => $"10.254.253.{n}").First(ip => !assigned.Contains(ip));
        await using var fixture = await PublicChannelTestFixture.CreateAsync();
        var nic = new ObsPublishLanInterface("fake-nic", "Fake LAN", true, address, "255.255.255.0", 24);
        GenesisDownstreamRelayOwnerRegistry.Owner? registration = null;
        IDisposable? held = null;
        var options = new ObsPublishChannelOptions { Lan = new(true, nic.Id, address, 17144), Ingest = new() { Port = 0 } };
        var host = new ObsPublishChannelHost(options, interfaces: new Interfaces(nic), registerRelay: (id, session, fanout) =>
        {
            registration = fixture.State.DownstreamRelays.Register(id, session, PeerCast.Nova.Network.Policies.RelayChannelKind.Broadcast, fanout);
            Assert.True(registration.TryReserve(long.MaxValue, out held));
            return registration;
        });
        try
        {
            host.Start();
            var result = await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ObsPublishChannelEndReason.StartupFailed, result.EndReason);
            Assert.False(result.ShutdownCompleted); // Never certify cleanup while a real policy lease remains.
            Assert.False(registration!.TryReserve(long.MaxValue - 1, out _));
            Assert.Equal(1, fixture.State.DownstreamRelays.ActiveRelayCount);
            Assert.Equal(ObsPublishLanFailure.BindFailed, host.PublishingStatus.LanRelay!.Failure);
            Assert.ThrowsAny<InvalidOperationException>(() => host.RelayEndPoint);
            Assert.Throws<InvalidOperationException>(() => host.IngestEndPoint);
        }
        finally
        {
            held?.Dispose();
            await host.DisposeAsync();
        }
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
    }

    private sealed class Interfaces(params ObsPublishLanInterface[] items) : IObsPublishLanInterfaces
    {
        public IReadOnlyList<ObsPublishLanInterface> Read() => items;
    }
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextYellowPageChannelCatalogRouterTests
{
    [Fact]
    public async Task ReadsFallbackUntilApplicationConfigurationIsBound()
    {
        var fallback = new RecordingAggregatedCatalog("fallback");
        var router = new GenesisGuiNextYellowPageChannelCatalogRouter(
            fallback,
            _ => throw new InvalidOperationException("Factory should not run."));

        var snapshot = await router.ReadDetailedAsync();

        Assert.Equal("fallback", snapshot.Entries.Single().Channel.Name);
        Assert.Equal(1, fallback.ReadCount);
    }

    [Fact]
    public async Task BindRoutesReadsThroughCatalogBuiltFromSameConfiguration()
    {
        var fallback = new RecordingAggregatedCatalog("fallback");
        GenesisYellowPageConfiguration? observedConfiguration = null;
        var configured = new RecordingAggregatedCatalog("configured");
        var router = new GenesisGuiNextYellowPageChannelCatalogRouter(
            fallback,
            configuration =>
            {
                observedConfiguration = configuration;
                return configured;
            });
        var yellowPages = GenesisYellowPageConfiguration.CreateDefault();

        router.Bind(yellowPages);
        var snapshot = await router.ReadDetailedAsync();

        Assert.Same(yellowPages, observedConfiguration);
        Assert.Equal("configured", snapshot.Entries.Single().Channel.Name);
        Assert.Equal(0, fallback.ReadCount);
        Assert.Equal(1, configured.ReadCount);
    }

    [Fact]
    public async Task RebindReplacesConfiguredCatalogWithoutTouchingFallback()
    {
        var fallback = new RecordingAggregatedCatalog("fallback");
        var configuredCatalogs = new Queue<RecordingAggregatedCatalog>(
            [
                new("first"),
                new("second"),
            ]);
        var router = new GenesisGuiNextYellowPageChannelCatalogRouter(
            fallback,
            _ => configuredCatalogs.Dequeue());

        router.Bind(GenesisYellowPageConfiguration.CreateDefault());
        var first = await router.ReadDetailedAsync();
        router.Bind(new GenesisYellowPageConfiguration(
            Array.Empty<GenesisYellowPageDefinition>()));
        var second = await router.ReadDetailedAsync();

        Assert.Equal("first", first.Entries.Single().Channel.Name);
        Assert.Equal("second", second.Entries.Single().Channel.Name);
        Assert.Equal(0, fallback.ReadCount);
    }

    [Fact]
    public async Task PreCancelledReadDoesNotReachCurrentCatalog()
    {
        var fallback = new RecordingAggregatedCatalog("fallback");
        var router = new GenesisGuiNextYellowPageChannelCatalogRouter(fallback);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await router.ReadDetailedAsync(cancellationSource.Token));

        Assert.Equal(0, fallback.ReadCount);
    }

    private sealed class RecordingAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly GenesisAggregatedChannelCatalogSnapshot _snapshot;

        public RecordingAggregatedCatalog(string channelName)
        {
            var channel = new GenesisChannelItem(
                channelName,
                $"pcp://example.test/{channelName}",
                "[test]",
                1,
                0,
                128,
                TimeSpan.FromMinutes(1),
                "FLV");
            _snapshot = new GenesisAggregatedChannelCatalogSnapshot(
                [new GenesisAggregatedChannelCatalogEntry(channel, ["test-yp"])],
                [new GenesisYellowPageCatalogReadStatus(
                    GenesisYellowPageDefaults.Sp,
                    GenesisYellowPageCatalogReadState.Succeeded)]);
        }

        public int ReadCount { get; private set; }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            var detailed = await ReadDetailedAsync(cancellationToken).ConfigureAwait(false);
            return detailed.Catalog;
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return ValueTask.FromResult(_snapshot);
        }
    }
}

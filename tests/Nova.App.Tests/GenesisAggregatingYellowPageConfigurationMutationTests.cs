namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAggregatingYellowPageConfigurationMutationTests
{
    [Fact]
    public async Task ConfigurationChangeDuringRefreshDoesNotPublishDisabledProviderResult()
    {
        var first = Definition("first");
        var second = Definition("second") with { IsEnabled = false };
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var firstChannel = new GenesisChannelItem(
            "Stale first",
            "pcp://first.example/channel");
        var secondChannel = new GenesisChannelItem(
            "Current second",
            "pcp://second.example/channel");
        var firstCatalog = new ControlledCatalog(new([firstChannel]));
        var factory = new RecordingFactory(new Dictionary<string, IGenesisChannelCatalog>
        {
            ["first"] = firstCatalog,
            ["second"] = new FixedCatalog(new([secondChannel])),
        });
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            factory);

        var readTask = catalog.ReadDetailedAsync().AsTask();
        await firstCatalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(configuration.SetEnabled("first", false));
        Assert.True(configuration.SetEnabled("second", true));
        firstCatalog.Release.TrySetResult(true);

        var detailed = await readTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            secondChannel with { YellowPage = "second" },
            Assert.Single(detailed.Catalog.Channels));
        Assert.DoesNotContain(
            detailed.Catalog.Channels,
            channel => string.Equals(
                channel.Name,
                firstChannel.Name,
                StringComparison.Ordinal));
        var status = Assert.Single(detailed.YellowPages);
        Assert.Equal("second", status.Definition.Id);
        Assert.Equal(GenesisYellowPageCatalogReadState.Succeeded, status.State);
        Assert.Collection(
            factory.CreatedIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
    }

    private static GenesisYellowPageDefinition Definition(string id) =>
        new(
            id,
            id,
            new Uri($"https://{id}.example/index.txt"));

    private sealed class RecordingFactory : IGenesisYellowPageChannelCatalogFactory
    {
        private readonly object _sync = new();
        private readonly IReadOnlyDictionary<string, IGenesisChannelCatalog> _catalogs;
        private readonly List<string> _createdIds = [];

        public RecordingFactory(
            IReadOnlyDictionary<string, IGenesisChannelCatalog> catalogs)
        {
            _catalogs = catalogs;
        }

        public IReadOnlyList<string> CreatedIds
        {
            get
            {
                lock (_sync)
                {
                    return Array.AsReadOnly(_createdIds.ToArray());
                }
            }
        }

        public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition)
        {
            lock (_sync)
            {
                _createdIds.Add(definition.Id);
            }

            return _catalogs[definition.Id];
        }
    }

    private sealed class ControlledCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public ControlledCatalog(GenesisChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return _snapshot;
        }
    }

    private sealed class FixedCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public FixedCatalog(GenesisChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }
    }
}

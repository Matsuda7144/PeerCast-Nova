namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageConfigurationRaceTests
{
    [Fact]
    public async Task InFlightReadRetriesWhenEnabledConfigurationChanges()
    {
        var first = Definition("first", isEnabled: true);
        var second = Definition("second", isEnabled: false);
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var staleChannel = new GenesisChannelItem(
            "Stale",
            "pcp://first.example/channel");
        var currentChannel = new GenesisChannelItem(
            "Current",
            "pcp://second.example/channel");
        var pending = new DeferredCatalog();
        var factory = new RecordingFactory(new Dictionary<string, IGenesisChannelCatalog>
        {
            ["first"] = pending,
            ["second"] = new FixedCatalog(new([currentChannel])),
        });
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            factory);

        var readTask = catalog.ReadAsync().AsTask();
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(configuration.SetEnabled("first", false));
        Assert.True(configuration.SetEnabled("second", true));
        pending.Complete(new([staleChannel]));

        var snapshot = await readTask.WaitAsync(TimeSpan.FromSeconds(1));

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal(currentChannel with { YellowPage = "second" }, channel);
        Assert.Collection(
            factory.CreatedIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
    }

    [Fact]
    public async Task InFlightFailureRetriesWhenEnabledConfigurationChanges()
    {
        var first = Definition("first", isEnabled: true);
        var second = Definition("second", isEnabled: false);
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var currentChannel = new GenesisChannelItem(
            "Current",
            "pcp://second.example/channel");
        var pending = new DeferredCatalog();
        var factory = new RecordingFactory(new Dictionary<string, IGenesisChannelCatalog>
        {
            ["first"] = pending,
            ["second"] = new FixedCatalog(new([currentChannel])),
        });
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            factory);

        var readTask = catalog.ReadAsync().AsTask();
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(configuration.SetEnabled("first", false));
        Assert.True(configuration.SetEnabled("second", true));
        pending.Fail(new InvalidOperationException("Stale provider failed."));

        var snapshot = await readTask.WaitAsync(TimeSpan.FromSeconds(1));

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal(currentChannel with { YellowPage = "second" }, channel);
        Assert.Collection(
            factory.CreatedIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
    }

    [Fact]
    public async Task RepeatedConfigurationChangesFailClosedAfterBoundedRetries()
    {
        var first = Definition("first", isEnabled: true);
        var configuration = new GenesisYellowPageConfiguration([first]);
        var churning = new ConfigurationChurningCatalog(
            configuration,
            first.Id,
            new([new GenesisChannelItem(
                "Never published",
                "pcp://first.example/channel")]));
        var factory = new RecordingFactory(new Dictionary<string, IGenesisChannelCatalog>
        {
            ["first"] = churning,
        });
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            factory);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.ReadAsync().AsTask());

        Assert.Equal(
            "Yellow Page configuration changed repeatedly during refresh.",
            error.Message);
        Assert.Equal(4, churning.ReadCount);
        Assert.Collection(
            factory.CreatedIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("first", id),
            id => Assert.Equal("first", id),
            id => Assert.Equal("first", id));
        Assert.True(Assert.Single(configuration.Definitions).IsEnabled);
    }

    private static GenesisYellowPageDefinition Definition(
        string id,
        bool isEnabled) =>
        new(
            id,
            id,
            new Uri($"https://{id}.example/index.txt"),
            isEnabled: isEnabled);

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

    private sealed class DeferredCatalog : IGenesisChannelCatalog
    {
        private readonly TaskCompletionSource<GenesisChannelCatalogSnapshot> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Started.TrySetResult();
            return await _completion.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public void Complete(GenesisChannelCatalogSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            _completion.TrySetResult(snapshot);
        }

        public void Fail(Exception error)
        {
            ArgumentNullException.ThrowIfNull(error);
            _completion.TrySetException(error);
        }
    }

    private sealed class ConfigurationChurningCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisYellowPageConfiguration _configuration;
        private readonly string _id;
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public ConfigurationChurningCatalog(
            GenesisYellowPageConfiguration configuration,
            string id,
            GenesisChannelCatalogSnapshot snapshot)
        {
            _configuration = configuration;
            _id = id;
            _snapshot = snapshot;
        }

        public int ReadCount { get; private set; }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (!_configuration.SetEnabled(_id, false) ||
                !_configuration.SetEnabled(_id, true))
            {
                throw new InvalidOperationException(
                    "Synthetic Yellow Page configuration churn failed.");
            }

            return ValueTask.FromResult(_snapshot);
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

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAggregatingYellowPageChannelCatalogTests
{
    [Fact]
    public async Task ReadAggregatesEnabledYellowPagesAndTracksOrigins()
    {
        const string sharedId = "0123456789ABCDEF0123456789ABCDEF";
        var firstShared = new GenesisChannelItem(
            "Shared from first",
            "pcp://first.example/channel/0123456789ABCDEF0123456789ABCDEF",
            ChannelId: sharedId)
        { ContactUrl = "https://first.example/contact" };
        var firstOnly = new GenesisChannelItem(
            "First only",
            "pcp://first.example/only");
        var secondShared = new GenesisChannelItem(
            "Shared from second",
            "pcp://second.example/channel/0123456789ABCDEF0123456789ABCDEF",
            ChannelId: sharedId)
        { ContactUrl = "https://second.example/contact" };
        var secondOnly = new GenesisChannelItem(
            "Second only",
            "pcp://second.example/only");
        var first = Definition("first");
        var second = Definition("second");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var catalog = CreateCatalog(
            configuration,
            ("first", new FixedCatalog(new([firstShared, firstOnly]))),
            ("second", new FixedCatalog(new([secondShared, secondOnly]))));

        var detailed = await catalog.ReadDetailedAsync();

        Assert.Equal(3, detailed.Catalog.Channels.Count);
        Assert.Equal(
            firstShared with { YellowPage = "first, second" },
            detailed.Catalog.Channels[0]);
        Assert.Equal(
            firstOnly with { YellowPage = "first" },
            detailed.Catalog.Channels[1]);
        Assert.Equal(
            secondOnly with { YellowPage = "second" },
            detailed.Catalog.Channels[2]);

        var shared = Assert.Single(
            detailed.Entries,
            entry => string.Equals(
                entry.Channel.ChannelId,
                sharedId,
                StringComparison.Ordinal));
        Assert.Equal(
            firstShared with { YellowPage = "first, second" },
            shared.Channel);
        Assert.Equal("first, second", shared.Channel.YellowPageText);
        Assert.Collection(
            shared.YellowPageIds,
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
        Assert.Collection(
            detailed.YellowPages,
            status => Assert.Equal(
                GenesisYellowPageCatalogReadState.Succeeded,
                status.State),
            status => Assert.Equal(
                GenesisYellowPageCatalogReadState.Succeeded,
                status.State));
    }

    [Fact]
    public async Task SpRawChannelExposesConfiguredYellowPageName()
    {
        var sp = GenesisYellowPageDefaults.Sp;
        var rawChannel = new GenesisChannelItem(
            "SP RAW",
            "pcp://203.0.113.10:7144/channel/0123456789ABCDEF0123456789ABCDEF",
            Type: "RAW",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var catalog = CreateCatalog(
            new GenesisYellowPageConfiguration([sp]),
            (sp.Id, new FixedCatalog(new([rawChannel]))));

        var channel = Assert.Single((await catalog.ReadAsync()).Channels);

        Assert.Equal("RAW", channel.TypeText);
        Assert.Equal("SP", channel.YellowPageText);
    }

    [Fact]
    public async Task DisabledYellowPageIsNotCreatedOrRead()
    {
        var enabled = Definition("enabled");
        var disabled = Definition("disabled") with { IsEnabled = false };
        var configuration = new GenesisYellowPageConfiguration([enabled, disabled]);
        var factory = new RecordingFactory(new Dictionary<string, IGenesisChannelCatalog>
        {
            ["enabled"] = new FixedCatalog(new([
                new GenesisChannelItem("Enabled", "pcp://enabled.example/channel"),
            ])),
            ["disabled"] = new FaultingCatalog(),
        });
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            factory);

        var snapshot = await catalog.ReadAsync();

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("enabled", channel.YellowPageText);
        Assert.Equal("enabled", Assert.Single(factory.CreatedIds));
    }

    [Fact]
    public async Task FailedYellowPageDoesNotDiscardSuccessfulYellowPage()
    {
        var first = Definition("first");
        var second = Definition("second");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var channel = new GenesisChannelItem("Available", "pcp://first.example/channel");
        var catalog = CreateCatalog(
            configuration,
            ("first", new FixedCatalog(new([channel]))),
            ("second", new FaultingCatalog()));

        var detailed = await catalog.ReadDetailedAsync();

        Assert.Single(detailed.Catalog.Channels);
        Assert.Equal(
            channel with { YellowPage = "first" },
            detailed.Catalog.Channels[0]);
        Assert.Equal(
            GenesisYellowPageCatalogReadState.Succeeded,
            detailed.YellowPages[0].State);
        Assert.Equal(
            GenesisYellowPageCatalogReadState.Failed,
            detailed.YellowPages[1].State);
    }

    [Fact]
    public async Task AllEnabledYellowPageFailuresFaultTheAggregateRead()
    {
        var first = Definition("first");
        var second = Definition("second");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var catalog = CreateCatalog(
            configuration,
            ("first", new FaultingCatalog()),
            ("second", new FaultingCatalog()));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await catalog.ReadAsync());
    }

    [Fact]
    public async Task EachReadUsesCurrentEnabledConfigurationSnapshot()
    {
        var first = Definition("first");
        var second = Definition("second") with { IsEnabled = false };
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var firstChannel = new GenesisChannelItem("First", "pcp://first.example/channel");
        var secondChannel = new GenesisChannelItem("Second", "pcp://second.example/channel");
        var catalog = CreateCatalog(
            configuration,
            ("first", new FixedCatalog(new([firstChannel]))),
            ("second", new FixedCatalog(new([secondChannel]))));

        var firstSnapshot = await catalog.ReadAsync();
        Assert.Equal(
            firstChannel with { YellowPage = "first" },
            Assert.Single(firstSnapshot.Channels));

        Assert.True(configuration.SetEnabled("first", false));
        Assert.True(configuration.SetEnabled("second", true));

        var secondSnapshot = await catalog.ReadAsync();
        Assert.Equal(
            secondChannel with { YellowPage = "second" },
            Assert.Single(secondSnapshot.Channels));
    }

    [Fact]
    public async Task CancellationStopsAggregateRead()
    {
        var definition = Definition("pending");
        var configuration = new GenesisYellowPageConfiguration([definition]);
        var pending = new PendingCatalog();
        var catalog = CreateCatalog(
            configuration,
            ("pending", pending));
        using var cancellationSource = new CancellationTokenSource();

        var readTask = catalog.ReadAsync(cancellationSource.Token).AsTask();
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
        Assert.True(pending.Cancelled);
    }

    private static GenesisAggregatingYellowPageChannelCatalog CreateCatalog(
        GenesisYellowPageConfiguration configuration,
        params (string Id, IGenesisChannelCatalog Catalog)[] catalogs)
    {
        return new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            new RecordingFactory(catalogs.ToDictionary(
                static pair => pair.Id,
                static pair => pair.Catalog,
                StringComparer.Ordinal)));
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

    private sealed class FaultingCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Yellow Page read failed.");
        }
    }

    private sealed class PendingCatalog : IGenesisChannelCatalog
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
                return GenesisChannelCatalogSnapshot.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}

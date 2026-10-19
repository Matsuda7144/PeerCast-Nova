namespace PeerCast.Nova.App.Tests;

public sealed class SpGenesisChannelCatalogCompositionTests
{
    [Fact]
    public void DefaultFactoryUsesAggregatedPresenceCapableCatalog()
    {
        var catalog = SpGenesisChannelCatalog.Create();

        Assert.IsAssignableFrom<IGenesisAggregatedChannelCatalog>(catalog);
    }

    [Fact]
    public async Task SpRawIndexFlowsThroughAggregationIntoListPresentation()
    {
        var textSource = new FixedIndexTextSource(BuildSpRawRow());
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(
            new GenesisYellowPageConfiguration([GenesisYellowPageDefaults.Sp]),
            new SingleCatalogFactory(SpGenesisChannelCatalog.Create(textSource)));

        var channel = Assert.Single((await aggregate.ReadAsync()).Channels);
        var rowValues = GenesisChannelListPresentation.CreateRowValues(channel);

        Assert.Equal(SpGenesisChannelCatalog.IndexUri, textSource.LastIndexUri);
        Assert.Equal("RAW", channel.TypeText);
        Assert.Equal("SP", channel.YellowPageText);
        Assert.Equal("RAW", rowValues[^2]);
        Assert.Equal("SP", rowValues[^1]);
    }

    private static string BuildSpRawRow()
    {
        var fields = new[]
        {
            "SP RAW",
            "0123456789ABCDEF0123456789ABCDEF",
            "203.0.113.10:7144",
            "http://example.invalid/contact",
            "game",
            "RAW relay test",
            "12",
            "3",
            "0",
            "RAW",
            "",
            "",
            "",
            "",
            "",
            "00:42",
            "",
            "SP RAW fixture",
        };
        return string.Join("<>", fields);
    }

    private sealed class SingleCatalogFactory : IGenesisYellowPageChannelCatalogFactory
    {
        private readonly IGenesisChannelCatalog _catalog;

        public SingleCatalogFactory(IGenesisChannelCatalog catalog)
        {
            _catalog = catalog;
        }

        public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition)
        {
            Assert.Equal(GenesisYellowPageDefaults.Sp.Id, definition.Id);
            return _catalog;
        }
    }

    private sealed class FixedIndexTextSource : IPeerCastIndexTextSource
    {
        private readonly string _indexText;

        public FixedIndexTextSource(string indexText)
        {
            _indexText = indexText;
        }

        public Uri? LastIndexUri { get; private set; }

        public ValueTask<string> ReadAsync(
            Uri indexUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastIndexUri = indexUri;
            return ValueTask.FromResult(_indexText);
        }
    }
}

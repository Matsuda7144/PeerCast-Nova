namespace PeerCast.Nova.App.Tests;

public sealed class HttpPeerCastIndexChannelCatalogTests
{
    private static readonly Uri IndexUri = new("http://example.invalid/index.txt");
    private static readonly Uri TrackerUri = new("pcp://tracker.example:7144/");

    [Fact]
    public async Task ReadUsesConfiguredIndexAndFallbackTracker()
    {
        var channelId = "0123456789ABCDEF0123456789ABCDEF";
        var textSource = new RecordingTextSource(BuildRow(
            name: "Example",
            channelId: channelId,
            tracker: string.Empty));
        var catalog = new HttpPeerCastIndexChannelCatalog(
            IndexUri,
            TrackerUri,
            textSource);

        var snapshot = await catalog.ReadAsync();

        Assert.Equal(IndexUri, textSource.LastUri);
        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("Example", channel.Name);
        Assert.Equal(
            $"pcp://tracker.example:7144/channel/{channelId}",
            channel.Source);
    }

    [Fact]
    public async Task ReadUsesRowTrackerWithoutFallbackTracker()
    {
        var channelId = "0123456789ABCDEF0123456789ABCDEF";
        var textSource = new RecordingTextSource(BuildRow(
            name: "Example",
            channelId: channelId,
            tracker: "row-tracker.example:7144"));
        var catalog = new HttpPeerCastIndexChannelCatalog(
            IndexUri,
            fallbackTrackerUri: null,
            textSource);

        var snapshot = await catalog.ReadAsync();

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal(
            $"pcp://row-tracker.example:7144/channel/{channelId}",
            channel.Source);
    }

    [Fact]
    public async Task MissingRowTrackerWithoutFallbackOmitsUnplayableChannel()
    {
        var textSource = new RecordingTextSource(BuildRow(
            name: "Example",
            channelId: "0123456789ABCDEF0123456789ABCDEF",
            tracker: string.Empty));
        var catalog = new HttpPeerCastIndexChannelCatalog(
            IndexUri,
            fallbackTrackerUri: null,
            textSource);

        var snapshot = await catalog.ReadAsync();

        Assert.Empty(snapshot.Channels);
    }

    [Fact]
    public async Task YellowPageFactoryUsesDefinitionEndpoints()
    {
        var channelId = "0123456789ABCDEF0123456789ABCDEF";
        var definition = new GenesisYellowPageDefinition(
            "custom",
            "Custom",
            new Uri("https://custom.example/index.txt"),
            new Uri("pcp://custom-tracker.example:7144/"));
        var textSource = new RecordingTextSource(BuildRow(
            name: "Custom Channel",
            channelId: channelId,
            tracker: string.Empty));
        var catalog = GenesisYellowPageCatalogFactory.Create(definition, textSource);

        var snapshot = await catalog.ReadAsync();

        Assert.Equal(definition.IndexUri, textSource.LastUri);
        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("Custom Channel", channel.Name);
        Assert.Equal(
            $"pcp://custom-tracker.example:7144/channel/{channelId}",
            channel.Source);
    }

    [Fact]
    public async Task CancellationFlowsToTextSource()
    {
        var textSource = new RecordingTextSource(string.Empty);
        var catalog = new HttpPeerCastIndexChannelCatalog(
            IndexUri,
            TrackerUri,
            textSource);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await catalog.ReadAsync(cancellationSource.Token));

        Assert.True(textSource.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CancellationAfterTextReadStopsBeforeParsing()
    {
        var channelId = "0123456789ABCDEF0123456789ABCDEF";
        using var cancellationSource = new CancellationTokenSource();
        var textSource = new RecordingTextSource(
            BuildRow("Example", channelId, string.Empty),
            cancellationSource.Cancel);
        var catalog = new HttpPeerCastIndexChannelCatalog(
            IndexUri,
            TrackerUri,
            textSource);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await catalog.ReadAsync(cancellationSource.Token));

        Assert.True(textSource.LastCancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("relative/index.txt")]
    [InlineData("ftp://example.invalid/index.txt")]
    public void InvalidIndexUriIsRejected(string value)
    {
        var uri = new Uri(value, UriKind.RelativeOrAbsolute);
        var textSource = new RecordingTextSource(string.Empty);

        Assert.Throws<ArgumentException>(() =>
            new HttpPeerCastIndexChannelCatalog(uri, TrackerUri, textSource));
    }

    [Fact]
    public void NullTextSourceIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HttpPeerCastIndexChannelCatalog(IndexUri, TrackerUri, null!));
    }

    [Fact]
    public void SpDefaultsUseRequestedYellowPageEndpoints()
    {
        Assert.Equal(
            new Uri("http://bayonet.ddo.jp/sp/index.txt"),
            SpGenesisChannelCatalog.IndexUri);
        Assert.Equal(
            new Uri("pcp://bayonet.ddo.jp:7146/"),
            SpGenesisChannelCatalog.TrackerUri);
    }

    [Fact]
    public async Task SpFactoryUsesRequestedIndexEndpoint()
    {
        var textSource = new RecordingTextSource(string.Empty);
        var catalog = SpGenesisChannelCatalog.Create(textSource);

        var snapshot = await catalog.ReadAsync();

        Assert.Empty(snapshot.Channels);
        Assert.Equal(SpGenesisChannelCatalog.IndexUri, textSource.LastUri);
    }

    private static string BuildRow(string name, string channelId, string tracker)
    {
        var fields = new[]
        {
            name,
            channelId,
            tracker,
            "",
            "game",
            "test",
            "1",
            "1",
            "1000",
            "FLV",
            "",
            "",
            "",
            "",
            "",
            "00:10",
            "",
            "",
        };
        return string.Join("<>", fields);
    }

    private sealed class RecordingTextSource : IPeerCastIndexTextSource
    {
        private readonly string _text;
        private readonly Action? _afterRead;

        public RecordingTextSource(string text, Action? afterRead = null)
        {
            _text = text;
            _afterRead = afterRead;
        }

        public Uri? LastUri { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask<string> ReadAsync(
            Uri indexUri,
            CancellationToken cancellationToken = default)
        {
            LastUri = indexUri;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            _afterRead?.Invoke();
            return ValueTask.FromResult(_text);
        }
    }
}

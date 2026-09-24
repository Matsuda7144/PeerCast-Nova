namespace PeerCast.Nova.App;

internal interface IPeerCastIndexTextSource
{
    ValueTask<string> ReadAsync(
        Uri indexUri,
        CancellationToken cancellationToken = default);
}

internal sealed class HttpPeerCastIndexChannelCatalog : IGenesisChannelCatalog
{
    private readonly Uri _indexUri;
    private readonly Uri _fallbackTrackerUri;
    private readonly IPeerCastIndexTextSource _textSource;

    public HttpPeerCastIndexChannelCatalog(
        Uri indexUri,
        Uri fallbackTrackerUri,
        IPeerCastIndexTextSource textSource)
    {
        ArgumentNullException.ThrowIfNull(indexUri);
        ArgumentNullException.ThrowIfNull(fallbackTrackerUri);
        ArgumentNullException.ThrowIfNull(textSource);
        ValidateHttpIndexUri(indexUri);
        ValidateTrackerUri(fallbackTrackerUri);

        _indexUri = indexUri;
        _fallbackTrackerUri = fallbackTrackerUri;
        _textSource = textSource;
    }

    public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var indexText = await _textSource.ReadAsync(_indexUri, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return PeerCastIndexChannelCatalogParser.Parse(indexText, _fallbackTrackerUri);
    }

    private static void ValidateHttpIndexUri(Uri indexUri)
    {
        if (!indexUri.IsAbsoluteUri ||
            (indexUri.Scheme != Uri.UriSchemeHttp &&
                indexUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(indexUri.Host))
        {
            throw new ArgumentException(
                "The channel index URI must be an absolute HTTP or HTTPS URI.",
                nameof(indexUri));
        }
    }

    private static void ValidateTrackerUri(Uri trackerUri)
    {
        if (!trackerUri.IsAbsoluteUri ||
            !string.Equals(trackerUri.Scheme, "pcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(trackerUri.Host))
        {
            throw new ArgumentException(
                "The fallback tracker URI must be an absolute pcp:// URI.",
                nameof(trackerUri));
        }
    }
}

internal static class SpGenesisChannelCatalog
{
    public static Uri IndexUri { get; } = new("http://bayonet.ddo.jp/sp/index.txt");

    public static Uri TrackerUri { get; } = new("pcp://bayonet.ddo.jp:7146/");

    public static IGenesisChannelCatalog Create() =>
        Create(SystemPeerCastIndexTextSource.Instance);

    public static IGenesisChannelCatalog Create(IPeerCastIndexTextSource textSource)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        return new HttpPeerCastIndexChannelCatalog(IndexUri, TrackerUri, textSource);
    }
}

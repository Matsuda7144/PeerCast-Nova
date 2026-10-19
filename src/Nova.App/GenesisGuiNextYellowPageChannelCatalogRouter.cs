namespace PeerCast.Nova.App;

/// <summary>
/// Keeps the startup catalog usable before GUI Next user state is available,
/// then routes subsequent reads through the application-owned Yellow Page
/// configuration without losing aggregated supplier status semantics.
/// </summary>
internal sealed class GenesisGuiNextYellowPageChannelCatalogRouter :
    IGenesisAggregatedChannelCatalog
{
    private readonly object _sync = new();
    private readonly IGenesisAggregatedChannelCatalog _fallbackCatalog;
    private readonly Func<
        GenesisYellowPageConfiguration,
        IGenesisAggregatedChannelCatalog> _configuredCatalogFactory;
    private IGenesisAggregatedChannelCatalog? _configuredCatalog;

    public GenesisGuiNextYellowPageChannelCatalogRouter(
        IGenesisAggregatedChannelCatalog fallbackCatalog)
        : this(
            fallbackCatalog,
            static configuration =>
                new GenesisAggregatingYellowPageChannelCatalog(configuration))
    {
    }

    internal GenesisGuiNextYellowPageChannelCatalogRouter(
        IGenesisAggregatedChannelCatalog fallbackCatalog,
        Func<GenesisYellowPageConfiguration, IGenesisAggregatedChannelCatalog>
            configuredCatalogFactory)
    {
        ArgumentNullException.ThrowIfNull(fallbackCatalog);
        ArgumentNullException.ThrowIfNull(configuredCatalogFactory);
        _fallbackCatalog = fallbackCatalog;
        _configuredCatalogFactory = configuredCatalogFactory;
    }

    public void Bind(GenesisYellowPageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configuredCatalog = _configuredCatalogFactory(configuration);
        ArgumentNullException.ThrowIfNull(configuredCatalog);
        lock (_sync)
        {
            _configuredCatalog = configuredCatalog;
        }
    }

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
        return CurrentCatalog().ReadDetailedAsync(cancellationToken);
    }

    private IGenesisAggregatedChannelCatalog CurrentCatalog()
    {
        lock (_sync)
        {
            return _configuredCatalog ?? _fallbackCatalog;
        }
    }
}

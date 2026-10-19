namespace PeerCast.Nova.App;

internal enum GenesisYellowPageCatalogReadState
{
    Succeeded,
    Failed,
}

internal sealed record GenesisYellowPageCatalogReadStatus(
    GenesisYellowPageDefinition Definition,
    GenesisYellowPageCatalogReadState State);

internal sealed record GenesisAggregatedChannelCatalogEntry(
    GenesisChannelItem Channel,
    IReadOnlyList<string> YellowPageIds);

internal sealed class GenesisAggregatedChannelCatalogSnapshot
{
    public GenesisAggregatedChannelCatalogSnapshot(
        IEnumerable<GenesisAggregatedChannelCatalogEntry> entries,
        IEnumerable<GenesisYellowPageCatalogReadStatus> yellowPages)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(yellowPages);

        Entries = Array.AsReadOnly(entries.ToArray());
        YellowPages = Array.AsReadOnly(yellowPages.ToArray());
        Catalog = new GenesisChannelCatalogSnapshot(
            Array.AsReadOnly(Entries
                .Select(static entry => entry.Channel)
                .ToArray()));
    }

    public IReadOnlyList<GenesisAggregatedChannelCatalogEntry> Entries { get; }

    public IReadOnlyList<GenesisYellowPageCatalogReadStatus> YellowPages { get; }

    public GenesisChannelCatalogSnapshot Catalog { get; }
}

internal interface IGenesisAggregatedChannelCatalog : IGenesisChannelCatalog
{
    ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
        CancellationToken cancellationToken = default);
}

internal interface IGenesisYellowPageChannelCatalogFactory
{
    IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition);
}

internal sealed class SystemGenesisYellowPageChannelCatalogFactory :
    IGenesisYellowPageChannelCatalogFactory
{
    public static SystemGenesisYellowPageChannelCatalogFactory Instance { get; } = new();

    private SystemGenesisYellowPageChannelCatalogFactory()
    {
    }

    public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GenesisYellowPageCatalogFactory.Create(definition);
    }
}

/// <summary>
/// Reads the currently enabled Yellow Pages independently, isolates a failed
/// provider from successful providers, and deterministically de-duplicates
/// channels by the stable Genesis channel identity while retaining the Yellow
/// Page ids and display names that supplied each aggregated channel.
/// </summary>
internal sealed class GenesisAggregatingYellowPageChannelCatalog :
    IGenesisAggregatedChannelCatalog
{
    private const string AllYellowPagesFailedMessage =
        "All enabled Yellow Page catalogs failed.";
    private const string ConfigurationChangedRepeatedlyMessage =
        "Yellow Page configuration changed repeatedly during refresh.";
    private const int MaximumConfigurationReadAttempts = 4;

    private readonly GenesisYellowPageConfiguration _configuration;
    private readonly IGenesisYellowPageChannelCatalogFactory _catalogFactory;

    public GenesisAggregatingYellowPageChannelCatalog(
        GenesisYellowPageConfiguration configuration)
        : this(
            configuration,
            SystemGenesisYellowPageChannelCatalogFactory.Instance)
    {
    }

    internal GenesisAggregatingYellowPageChannelCatalog(
        GenesisYellowPageConfiguration configuration,
        IGenesisYellowPageChannelCatalogFactory catalogFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(catalogFactory);
        _configuration = configuration;
        _catalogFactory = catalogFactory;
    }

    public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var detailed = await ReadDetailedAsync(cancellationToken)
            .ConfigureAwait(false);
        return detailed.Catalog;
    }

    public async ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < MaximumConfigurationReadAttempts; attempt++)
        {
            var configurationSnapshot = _configuration.CaptureEnabledReadSnapshot();
            try
            {
                var detailed = await ReadConfigurationSnapshotAsync(
                        configurationSnapshot.EnabledDefinitions,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (_configuration.IsRevisionCurrent(configurationSnapshot.Revision))
                {
                    return detailed;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (!_configuration.IsRevisionCurrent(configurationSnapshot.Revision))
            {
                // A settings mutation invalidated this provider set while it was
                // in flight. Retry from the current enabled configuration instead
                // of publishing stale channels or a stale provider failure.
            }
        }

        throw new InvalidOperationException(ConfigurationChangedRepeatedlyMessage);
    }

    private async ValueTask<GenesisAggregatedChannelCatalogSnapshot>
        ReadConfigurationSnapshotAsync(
            IReadOnlyList<GenesisYellowPageDefinition> definitions,
            CancellationToken cancellationToken)
    {
        if (definitions.Count == 0)
        {
            return new GenesisAggregatedChannelCatalogSnapshot(
                Array.Empty<GenesisAggregatedChannelCatalogEntry>(),
                Array.Empty<GenesisYellowPageCatalogReadStatus>());
        }

        var readTasks = new Task<YellowPageRead>[definitions.Count];
        for (var index = 0; index < definitions.Count; index++)
        {
            readTasks[index] = ReadYellowPageAsync(
                definitions[index],
                cancellationToken);
        }

        var reads = await Task.WhenAll(readTasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!reads.Any(static read => read.Snapshot is not null))
        {
            throw new InvalidOperationException(AllYellowPagesFailedMessage);
        }

        var entries = Aggregate(reads);
        var statuses = reads.Select(static read =>
            new GenesisYellowPageCatalogReadStatus(
                read.Definition,
                read.Snapshot is null
                    ? GenesisYellowPageCatalogReadState.Failed
                    : GenesisYellowPageCatalogReadState.Succeeded));

        return new GenesisAggregatedChannelCatalogSnapshot(entries, statuses);
    }

    private async Task<YellowPageRead> ReadYellowPageAsync(
        GenesisYellowPageDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = _catalogFactory.Create(definition);
            ArgumentNullException.ThrowIfNull(catalog);
            var snapshot = await catalog.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new YellowPageRead(
                definition,
                GenesisChannelCatalogSanitizer.Sanitize(snapshot));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new YellowPageRead(definition, Snapshot: null);
        }
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<GenesisAggregatedChannelCatalogEntry> Aggregate(
        IReadOnlyList<YellowPageRead> reads)
    {
        var byIdentity = new Dictionary<string, EntryBuilder>(StringComparer.Ordinal);
        var ordered = new List<EntryBuilder>();

        foreach (var read in reads)
        {
            if (read.Snapshot is null)
            {
                continue;
            }

            foreach (var channel in read.Snapshot.Channels)
            {
                var identity = GenesisChannelIdentity.GetStableKey(channel);
                if (!byIdentity.TryGetValue(identity, out var builder))
                {
                    if (ordered.Count >= GenesisChannelCatalogSanitizer.MaximumChannels)
                    {
                        continue;
                    }

                    builder = new EntryBuilder(channel);
                    byIdentity.Add(identity, builder);
                    ordered.Add(builder);
                }

                builder.AddYellowPage(read.Definition);
            }
        }

        return Array.AsReadOnly(
            ordered
                .Select(static builder => builder.Build())
                .ToArray());
    }

    private sealed record YellowPageRead(
        GenesisYellowPageDefinition Definition,
        GenesisChannelCatalogSnapshot? Snapshot);

    private sealed class EntryBuilder
    {
        private readonly List<string> _yellowPageIds = [];
        private readonly HashSet<string> _seenYellowPageIds =
            new(StringComparer.Ordinal);
        private readonly List<string> _yellowPageNames = [];
        private readonly HashSet<string> _seenYellowPageNames =
            new(StringComparer.Ordinal);

        public EntryBuilder(GenesisChannelItem channel)
        {
            Channel = channel;
        }

        public GenesisChannelItem Channel { get; }

        public void AddYellowPage(GenesisYellowPageDefinition definition)
        {
            if (!_seenYellowPageIds.Add(definition.Id))
            {
                return;
            }

            _yellowPageIds.Add(definition.Id);
            if (_seenYellowPageNames.Add(definition.Name))
            {
                _yellowPageNames.Add(definition.Name);
            }
        }

        public GenesisAggregatedChannelCatalogEntry Build()
        {
            var yellowPage = GenesisChannelCatalogSanitizer.NormalizeYellowPage(
                _yellowPageNames.Count == 0
                    ? null
                    : string.Join(", ", _yellowPageNames));
            return new(
                Channel with { YellowPage = yellowPage },
                Array.AsReadOnly(_yellowPageIds.ToArray()));
        }
    }
}

namespace PeerCast.Nova.App;

public sealed record GenesisChannelCatalogSnapshot(
    IReadOnlyList<GenesisChannelItem> Channels)
{
    public static GenesisChannelCatalogSnapshot Empty { get; } =
        new(Array.Empty<GenesisChannelItem>());
}

public interface IGenesisChannelCatalog
{
    ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class EmptyGenesisChannelCatalog : IGenesisChannelCatalog
{
    public static EmptyGenesisChannelCatalog Instance { get; } = new();

    private EmptyGenesisChannelCatalog()
    {
    }

    public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
    }
}

internal enum GenesisChannelCatalogRefreshResult
{
    Completed,
    Cancelled,
    Faulted,
}

internal sealed class GenesisChannelCatalogCoordinator
{
    private const string RefreshFailedMessage = "Channel refresh failed.";
    private readonly IGenesisChannelCatalog _catalog;
    private readonly GenesisMainViewModel _viewModel;
    private readonly IGenesisChannelPresenceNotificationPublisher _notificationPublisher;
    private readonly GenesisChannelPresenceTracker _presenceTracker = new();

    public GenesisChannelCatalogCoordinator(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel)
        : this(
            catalog,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance)
    {
    }

    internal GenesisChannelCatalogCoordinator(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel,
        IGenesisChannelPresenceNotificationPublisher notificationPublisher)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notificationPublisher);
        _catalog = catalog;
        _viewModel = viewModel;
        _notificationPublisher = notificationPublisher;
    }

    internal GenesisChannelPresenceSnapshot? PresenceSnapshot { get; private set; }

    internal GenesisChannelViewProjectionSnapshot? ChannelViews { get; private set; }

    public async ValueTask<GenesisChannelCatalogRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        _viewModel.SetChannelCatalogRefreshState(isRefreshing: true, errorMessage: null);
        try
        {
            var projection = await ReadProjectedSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            ChannelViews = projection;
            _viewModel.ReplaceChannels(projection.Channels);
            _viewModel.SetChannelCatalogRefreshState(isRefreshing: false, errorMessage: null);
            return GenesisChannelCatalogRefreshResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _viewModel.SetChannelCatalogRefreshState(isRefreshing: false, errorMessage: null);
            return GenesisChannelCatalogRefreshResult.Cancelled;
        }
        catch
        {
            _viewModel.SetChannelCatalogRefreshState(
                isRefreshing: false,
                errorMessage: RefreshFailedMessage);
            return GenesisChannelCatalogRefreshResult.Faulted;
        }
    }

    private async ValueTask<GenesisChannelViewProjectionSnapshot> ReadProjectedSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (_catalog is IGenesisAggregatedChannelCatalog aggregatedCatalog)
        {
            var detailed = await aggregatedCatalog.ReadDetailedAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var presence = _presenceTracker.ApplySuccessfulRefresh(detailed);
            PresenceSnapshot = presence;
            await PublishNotificationsAsync(presence, cancellationToken)
                .ConfigureAwait(false);
            return GenesisChannelViewProjectionSnapshot.FromPresence(presence);
        }

        var snapshot = await _catalog.ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        return GenesisChannelViewProjectionSnapshot.FromCatalog(sanitized);
    }

    private async ValueTask PublishNotificationsAsync(
        GenesisChannelPresenceSnapshot presence,
        CancellationToken cancellationToken)
    {
        try
        {
            await _notificationPublisher.PublishAsync(presence, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Presence/catalog state is already committed for this generation.
            // Auxiliary notification cancellation must not roll it back.
        }
        catch
        {
            // Notifications are auxiliary. A presenter, sound, preference, or
            // publisher failure must not turn a successful catalog refresh into
            // a channel refresh failure.
        }
    }
}

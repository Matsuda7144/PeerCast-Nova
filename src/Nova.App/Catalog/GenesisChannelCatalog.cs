namespace PeerCast.Nova.App;

public sealed record GenesisYellowPageNotice(
    string Name,
    string? Description = null,
    string? Type = null);

public sealed record GenesisChannelCatalogSnapshot(
    IReadOnlyList<GenesisChannelItem> Channels,
    IReadOnlyList<GenesisYellowPageNotice> Notices)
{
    public GenesisChannelCatalogSnapshot(IReadOnlyList<GenesisChannelItem> channels)
        : this(channels, Array.Empty<GenesisYellowPageNotice>())
    {
    }

    public static GenesisChannelCatalogSnapshot Empty { get; } =
        new(
            Array.Empty<GenesisChannelItem>(),
            Array.Empty<GenesisYellowPageNotice>());
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
    private readonly GenesisYellowPageRefreshDiagnosticTrace? _diagnosticTrace;
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
        : this(catalog, viewModel, notificationPublisher, diagnosticTrace: null)
    {
    }

    internal GenesisChannelCatalogCoordinator(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel,
        IGenesisChannelPresenceNotificationPublisher notificationPublisher,
        GenesisYellowPageRefreshDiagnosticTrace? diagnosticTrace)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notificationPublisher);
        _catalog = catalog;
        _viewModel = viewModel;
        _notificationPublisher = notificationPublisher;
        _diagnosticTrace = diagnosticTrace;
    }

    internal GenesisChannelPresenceSnapshot? PresenceSnapshot { get; private set; }

    internal GenesisChannelViewProjectionSnapshot? ChannelViews { get; private set; }

    public async ValueTask<GenesisChannelCatalogRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        _viewModel.SetChannelCatalogRefreshState(isRefreshing: true, errorMessage: null);
        try
        {
            var read = await ReadProjectedSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            var projection = read.Projection;
            ChannelViews = projection;
            _viewModel.ReplaceChannels(projection.Channels);
            _viewModel.SetChannelCatalogRefreshState(isRefreshing: false, errorMessage: null);
            var context = GenesisYellowPageRefreshDiagnosticAmbient.Current;
            if (read.RefreshAttemptId is { } attemptId && context is not null)
            {
                context = context.ForAttempt(attemptId, read.ConfigurationRevision);
            }

            _diagnosticTrace?.TryRecord(
                "CatalogProjectionCompleted",
                context,
                stage: "GuiRows",
                outcome: "Applied",
                projectedRowCount: projection.Channels.Count);
            _diagnosticTrace?.TryRecord(
                "RefreshCompleted",
                context,
                stage: "Refresh",
                outcome: "Completed",
                projectedRowCount: projection.Channels.Count);
            return GenesisChannelCatalogRefreshResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _viewModel.SetChannelCatalogRefreshState(isRefreshing: false, errorMessage: null);
            _diagnosticTrace?.TryRecord(
                "RefreshFailed",
                stage: "Refresh",
                outcome: "Cancelled",
                exceptionType: nameof(OperationCanceledException),
                failureStage: "Cancellation");
            return GenesisChannelCatalogRefreshResult.Cancelled;
        }
        catch (Exception exception)
        {
            _viewModel.SetChannelCatalogRefreshState(
                isRefreshing: false,
                errorMessage: RefreshFailedMessage);
            _diagnosticTrace?.TryRecord(
                "RefreshFailed",
                stage: "Refresh",
                outcome: "Failed",
                exceptionType: exception.GetType().Name,
                failureStage: "CatalogOrProjection");
            return GenesisChannelCatalogRefreshResult.Faulted;
        }
    }

    private async ValueTask<CatalogProjectionRead> ReadProjectedSnapshotAsync(
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
            var projection = GenesisChannelViewProjectionSnapshot.FromPresence(
                presence,
                detailed.Notices);
            ApplyGuiNextViews(projection, presence);
            return new CatalogProjectionRead(
                projection,
                detailed.RefreshAttemptId,
                detailed.ConfigurationRevision);
        }

        var context = GenesisYellowPageRefreshDiagnosticAmbient.Current;
        var attemptId = context?.Trace is null ? (Guid?)null : Guid.NewGuid();
        if (attemptId is { } actualAttemptId && context is not null)
        {
            context = context.ForAttempt(actualAttemptId, configurationRevision: null);
        }

        _diagnosticTrace?.TryRecord(
            "RefreshStarted",
            context,
            stage: "CatalogRead",
            outcome: "Started");
        var snapshot = await _catalog.ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var catalogProjection = GenesisChannelViewProjectionSnapshot.FromCatalog(sanitized);
        ApplyGuiNextViews(catalogProjection, presence: null);
        return new CatalogProjectionRead(
            catalogProjection,
            attemptId,
            ConfigurationRevision: null);
    }

    private void ApplyGuiNextViews(
        GenesisChannelViewProjectionSnapshot projection,
        GenesisChannelPresenceSnapshot? presence)
    {
        if (_notificationPublisher is not GenesisGuiNextChannelPresencePublisher guiNextPublisher)
        {
            return;
        }

        try
        {
            guiNextPublisher.ApplyChannelViews(projection, presence);
        }
        catch
        {
            // GUI Next projection is auxiliary to the committed catalog generation.
        }
    }

    private sealed record CatalogProjectionRead(
        GenesisChannelViewProjectionSnapshot Projection,
        Guid? RefreshAttemptId,
        long? ConfigurationRevision);

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

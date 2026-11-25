namespace PeerCast.Nova.App;

/// <summary>
/// Presents one channel notification without owning notification sound. Native
/// implementations remain behind this application-facing boundary.
/// </summary>
internal interface IGenesisChannelNotificationPresenter
{
    ValueTask PresentAsync(
        GenesisChannelNotification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Plays the platform system notification sound. The same boundary is used by
/// normal notification dispatch and the Settings test-sound action.
/// </summary>
internal interface IGenesisSystemNotificationSoundPlayer
{
    ValueTask PlayAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates visual notification presentation and optional system sound while
/// keeping both native concerns outside presence/catalog logic.
/// </summary>
internal sealed class GenesisChannelNotificationDispatcher
{
    private readonly IGenesisChannelNotificationPresenter _presenter;
    private readonly IGenesisSystemNotificationSoundPlayer _soundPlayer;

    public GenesisChannelNotificationDispatcher(
        IGenesisChannelNotificationPresenter presenter,
        IGenesisSystemNotificationSoundPlayer soundPlayer)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(soundPlayer);
        _presenter = presenter;
        _soundPlayer = soundPlayer;
    }

    public async ValueTask DispatchAsync(
        GenesisChannelNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        await _presenter.PresentAsync(notification, cancellationToken)
            .ConfigureAwait(false);
        if (!notification.PlaySound)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _soundPlayer.PlayAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask TestSoundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _soundPlayer.PlayAsync(cancellationToken);
    }
}

internal interface IGenesisChannelPresenceNotificationPublisher
{
    ValueTask PublishAsync(
        GenesisChannelPresenceSnapshot presence,
        CancellationToken cancellationToken = default);
}

internal sealed class EmptyGenesisChannelPresenceNotificationPublisher :
    IGenesisChannelPresenceNotificationPublisher
{
    public static EmptyGenesisChannelPresenceNotificationPublisher Instance { get; } =
        new();

    private EmptyGenesisChannelPresenceNotificationPublisher()
    {
    }

    public ValueTask PublishAsync(
        GenesisChannelPresenceSnapshot presence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presence);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Keeps GUI Next channel tabs on the exact committed presence snapshot consumed
/// by notification rules. Projection failures are bounded so they cannot suppress
/// notifications or turn a successful catalog refresh into a failure.
/// </summary>
internal sealed class GenesisGuiNextChannelPresencePublisher :
    IGenesisChannelPresenceNotificationPublisher
{
    private readonly GenesisGuiNextTabsViewModel _tabs;
    private readonly IGenesisChannelPresenceNotificationPublisher _next;

    public GenesisGuiNextChannelPresencePublisher(
        GenesisGuiNextTabsViewModel tabs,
        IGenesisChannelPresenceNotificationPublisher next)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(next);
        _tabs = tabs;
        _next = next;
    }

    internal GenesisGuiNextTabsViewModel Tabs => _tabs;

    internal void ApplyChannelViews(
        GenesisChannelViewProjectionSnapshot views,
        GenesisChannelPresenceSnapshot? presence = null)
    {
        ArgumentNullException.ThrowIfNull(views);
        _tabs.ApplyChannelViews(views, presence);
    }

    public ValueTask PublishAsync(
        GenesisChannelPresenceSnapshot presence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presence);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ApplyChannelViews(
                GenesisChannelViewProjectionSnapshot.FromPresence(presence),
                presence);
        }
        catch
        {
            // GUI Next projection is auxiliary to the already committed catalog
            // generation and must not suppress notification dispatch.
        }

        return _next.PublishAsync(presence, cancellationToken);
    }
}

/// <summary>
/// Converts one committed presence snapshot to notifications using the current
/// favorites and preferences, then dispatches each activation independently.
/// A presentation/sound failure is bounded to that activation and cannot prevent
/// later notifications in the same refresh generation.
/// </summary>
internal sealed class GenesisChannelNotificationPublisher :
    IGenesisChannelPresenceNotificationPublisher
{
    private readonly GenesisFavoriteChannelCollection _favorites;
    private readonly Func<GenesisChannelNotificationPreferences> _preferencesProvider;
    private readonly GenesisChannelNotificationDispatcher _dispatcher;

    public GenesisChannelNotificationPublisher(
        GenesisFavoriteChannelCollection favorites,
        Func<GenesisChannelNotificationPreferences> preferencesProvider,
        GenesisChannelNotificationDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(preferencesProvider);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _favorites = favorites;
        _preferencesProvider = preferencesProvider;
        _dispatcher = dispatcher;
    }

    public async ValueTask PublishAsync(
        GenesisChannelPresenceSnapshot presence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presence);
        cancellationToken.ThrowIfCancellationRequested();
        var preferences = _preferencesProvider();
        ArgumentNullException.ThrowIfNull(preferences);
        var notifications = GenesisChannelNotificationProjection.Create(
            presence,
            _favorites.Snapshot,
            preferences);

        var playedSoundInGeneration = false;
        foreach (var notification in notifications)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // 1 回の更新サイクルで複数チャンネルが同時に新着となっても効果音は最大 1 回に抑止する。
            var effectiveNotification = notification.PlaySound && playedSoundInGeneration
                ? notification with { PlaySound = false }
                : notification;

            try
            {
                await _dispatcher.DispatchAsync(effectiveNotification, cancellationToken)
                    .ConfigureAwait(false);
                if (effectiveNotification.PlaySound)
                {
                    playedSoundInGeneration = true;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Notification UI and sound are auxiliary. One failure must not
                // suppress later activation notifications in the same generation.
            }
        }
    }
}

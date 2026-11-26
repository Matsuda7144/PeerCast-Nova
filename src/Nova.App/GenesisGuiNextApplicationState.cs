namespace PeerCast.Nova.App;

internal enum GenesisChannelNotificationSettingsMutationResult
{
    Changed,
    Unchanged,
    PersistenceFailed,
}

internal enum GenesisChannelNotificationSettingsTestSoundResult
{
    Played,
    Failed,
}

[Flags]
internal enum GenesisGuiNextApplicationStateLoadFailure
{
    None = 0,
    Favorites = 1 << 0,
    NotificationPreferences = 1 << 1,
    YellowPages = 1 << 2,
    PlaybackPreferences = 1 << 3,
    ConnectionPreferences = 1 << 4,
}

/// <summary>
/// Mutable, thread-safe notification settings shared by Settings UI and channel
/// notification dispatch. Test sound is an explicit action and therefore plays
/// regardless of the current automatic-sound preference.
/// </summary>
internal sealed class GenesisChannelNotificationSettings
{
    private readonly object _sync = new();
    private readonly IGenesisSystemNotificationSoundPlayer _soundPlayer;
    private GenesisChannelNotificationPreferences _preferences;
    private Func<GenesisChannelNotificationPreferences, bool>? _persist;
    private Action<GenesisChannelNotificationSettingsMutationResult>? _feedback;
    private Action<GenesisChannelNotificationSettingsTestSoundResult>? _testSoundFeedback;

    public GenesisChannelNotificationSettings(
        GenesisChannelNotificationPreferences preferences,
        IGenesisSystemNotificationSoundPlayer soundPlayer)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(soundPlayer);
        _preferences = preferences;
        _soundPlayer = soundPlayer;
    }

    public GenesisChannelNotificationPreferences Current
    {
        get
        {
            lock (_sync)
            {
                return _preferences;
            }
        }
    }

    public GenesisChannelNotificationSettingsMutationResult SetNotifyNewChannels(
        bool value) =>
        Update(current => current with { NotifyNewChannels = value });

    public GenesisChannelNotificationSettingsMutationResult SetNotifyFavoriteChannels(
        bool value) =>
        Update(current => current with { NotifyFavoriteChannels = value });

    public GenesisChannelNotificationSettingsMutationResult SetPlaySound(bool value) =>
        Update(current => current with { PlaySound = value });

    public async ValueTask TestSoundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GenesisChannelNotificationSettingsTestSoundResult result;
        try
        {
            await _soundPlayer.PlayAsync(cancellationToken).ConfigureAwait(false);
            result = GenesisChannelNotificationSettingsTestSoundResult.Played;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = GenesisChannelNotificationSettingsTestSoundResult.Failed;
        }

        PublishTestSoundFeedback(result);
    }

    internal void BindPersistence(
        Func<GenesisChannelNotificationPreferences, bool> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        lock (_sync)
        {
            _persist = persist;
        }
    }

    internal void BindFeedback(
        Action<GenesisChannelNotificationSettingsMutationResult> feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        lock (_sync)
        {
            _feedback = feedback;
        }
    }

    internal void BindTestSoundFeedback(
        Action<GenesisChannelNotificationSettingsTestSoundResult> feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        lock (_sync)
        {
            _testSoundFeedback = feedback;
        }
    }

    private GenesisChannelNotificationSettingsMutationResult Update(
        Func<GenesisChannelNotificationPreferences, GenesisChannelNotificationPreferences>
            update)
    {
        ArgumentNullException.ThrowIfNull(update);
        GenesisChannelNotificationSettingsMutationResult result;
        Action<GenesisChannelNotificationSettingsMutationResult>? feedback;
        lock (_sync)
        {
            var next = update(_preferences);
            if (Equals(next, _preferences))
            {
                result = GenesisChannelNotificationSettingsMutationResult.Unchanged;
            }
            else if (!TryPersist(next))
            {
                result = GenesisChannelNotificationSettingsMutationResult.PersistenceFailed;
            }
            else
            {
                _preferences = next;
                result = GenesisChannelNotificationSettingsMutationResult.Changed;
            }

            feedback = _feedback;
        }

        if (feedback is not null)
        {
            try
            {
                feedback(result);
            }
            catch
            {
                // Feedback is auxiliary presentation and cannot replace the
                // already-completed notification-settings mutation result.
            }
        }

        return result;
    }

    private void PublishTestSoundFeedback(
        GenesisChannelNotificationSettingsTestSoundResult result)
    {
        Action<GenesisChannelNotificationSettingsTestSoundResult>? feedback;
        lock (_sync)
        {
            feedback = _testSoundFeedback;
        }

        if (feedback is null)
        {
            return;
        }

        try
        {
            feedback(result);
        }
        catch
        {
            // Test-sound feedback is auxiliary presentation and cannot replace the
            // already-completed sound action result.
        }
    }

    private bool TryPersist(GenesisChannelNotificationPreferences next)
    {
        if (_persist is null)
        {
            return true;
        }

        try
        {
            return _persist(next);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// One application-owned GUI Next state graph. Favorites, Yellow Page
/// configuration, tabs, and notification settings are loaded once and shared for
/// the GUI lifetime. Explicit favorite edits, Yellow Page edits, and notification
/// preference toggles are persisted transactionally when applied. Finite shutdown
/// retries persistence only for domains that loaded successfully, so fallback state
/// cannot passively replace saved data that failed to load.
/// </summary>
internal sealed class GenesisGuiNextApplicationState : IAsyncDisposable
{
    private readonly IGenesisFavoriteChannelStore _favoriteStore;
    private readonly IGenesisChannelNotificationPreferenceStore _notificationStore;
    private readonly IGenesisYellowPageConfigurationStore _yellowPageStore;
    private readonly IGenesisGuiNextPlaybackPreferencesStore _playbackPreferencesStore;
    private readonly GenesisRecordingRuleRepository _recordingRuleRepository;
    private int _disposed;

    private GenesisGuiNextApplicationState(
        GenesisFavoriteChannelCollection favorites,
        GenesisChannelNotificationSettings notifications,
        GenesisYellowPageConfiguration yellowPages,
        GenesisGuiNextPlaybackSettings playback,
        GenesisGuiNextConnectionSettings connection,
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        GenesisRecordingRuleRepository recordingRuleRepository,
        GenesisGuiNextApplicationStateLoadFailure loadFailures,
        Func<string, string?>? readEnvironment = null,
        PeerCast.Nova.Network.Publishing.PublishingAnnounceOwner? announceOwner = null)
    {
        AnnounceOwner = announceOwner ?? new();
        Favorites = favorites;
        Notifications = notifications;
        YellowPages = yellowPages;
        Playback = playback;
        Connection = connection;
        NetworkPolicies = GenesisGuiNextNetworkPolicies.Create(connection, readEnvironment);
        DownstreamRelays = new GenesisDownstreamRelayOwnerRegistry(NetworkPolicies.Relays);
        PublicChannels = new GenesisGuiNextPublicChannelComposition(DownstreamRelays);
        PublicChannels.ConfigurePublic(connection.Startup);
        RelayActivation = new GenesisGuiNextRelayActivation(PublicChannels,
            GenesisGuiNextRelayActivation.RecoveryPolicy(Environment.GetEnvironmentVariable("NOVA_SMART_RELAY_RECOVERY")));
        ArgumentNullException.ThrowIfNull(recordingRuleRepository);
        _recordingRuleRepository = recordingRuleRepository;
        Tabs = new(favorites, recordingRuleRepository);
        _favoriteStore = favoriteStore;
        _notificationStore = notificationStore;
        _yellowPageStore = yellowPageStore;
        _playbackPreferencesStore = playbackPreferencesStore;
        LoadFailures = loadFailures;
        Favorites.BindPersistence(TryPersistFavorites);
        Notifications.BindPersistence(TryPersistNotifications);
        YellowPages.BindPersistence(TryPersistYellowPages);
        Playback.BindPersistence(TryPersistPlaybackPreferences);
        Connection.BindLiveApply(ApplyLiveConnectionSettingsAsync);
    }

    private async ValueTask ApplyLiveConnectionSettingsAsync(
        GenesisGuiNextConnectionPreferences next,
        CancellationToken cancellationToken)
    {
        NetworkPolicies.ApplyLive(next);
        Connection.MarkActive(next);
        try
        {
            await PublicChannels.ApplyLivePublicSettingsAsync(next).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            PeerCast.Nova.Foundation.Logging.FileNovaLogger.Default.Log(
                PeerCast.Nova.Foundation.Logging.LogLevel.Warning,
                "Failed to apply live public relay settings.",
                exception);
        }
    }

    public GenesisFavoriteChannelCollection Favorites { get; }

    public GenesisChannelNotificationSettings Notifications { get; }

    public GenesisYellowPageConfiguration YellowPages { get; }

    public GenesisGuiNextPlaybackSettings Playback { get; }

    public GenesisGuiNextConnectionSettings Connection { get; }

    public GenesisGuiNextNetworkPolicies NetworkPolicies { get; }

    internal PeerCast.Nova.Network.Publishing.PublishingAnnounceOwner AnnounceOwner { get; }
    public GenesisDownstreamRelayOwnerRegistry DownstreamRelays { get; }

    public GenesisGuiNextPublicChannelComposition PublicChannels { get; }
    public GenesisGuiNextRelayActivation RelayActivation { get; }

    public GenesisGuiNextTabsViewModel Tabs { get; }

    internal GenesisGuiNextApplicationStateLoadFailure LoadFailures { get; }

    public static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        CancellationToken cancellationToken = default) =>
        LoadAsync(
            favoriteStore,
            notificationStore,
            DefaultGenesisYellowPageConfigurationStore.Instance,
            DefaultGenesisGuiNextPlaybackPreferencesStore.Instance,
            soundPlayer,
            pathSource: null,
            cancellationToken);

    public static async ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        CancellationToken cancellationToken = default) =>
        await LoadAsync(
                favoriteStore,
                notificationStore,
                yellowPageStore,
                DefaultGenesisGuiNextPlaybackPreferencesStore.Instance,
                soundPlayer,
                pathSource: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        GenesisGuiNextPlaybackPreferencesSource? pathSource = null,
        CancellationToken cancellationToken = default) =>
        LoadAsync(favoriteStore, notificationStore, yellowPageStore, playbackPreferencesStore,
            DefaultGenesisGuiNextConnectionPreferencesStore.Instance, soundPlayer, pathSource, cancellationToken);

    internal static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisGuiNextConnectionPreferencesStore connectionPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        GenesisGuiNextPlaybackPreferencesSource? pathSource = null,
        CancellationToken cancellationToken = default) =>
        LoadCoreAsync(
            favoriteStore,
            notificationStore,
            yellowPageStore,
            playbackPreferencesStore,
            soundPlayer,
            pathSource,
            recordingRuleRepositoryFactory: null,
            connectionPreferencesStore,
            cancellationToken);

    internal static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        Func<string, string?> readEnvironment,
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisGuiNextConnectionPreferencesStore connectionPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        CancellationToken cancellationToken = default) =>
        LoadCoreAsync(favoriteStore, notificationStore, yellowPageStore, playbackPreferencesStore,
            soundPlayer, pathSource: null, recordingRuleRepositoryFactory: null,
            connectionPreferencesStore, cancellationToken, readEnvironment);

    internal static ValueTask<GenesisGuiNextApplicationState> LoadWithRecordingRuleRepositoryFactoryAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        GenesisGuiNextPlaybackPreferencesSource? pathSource,
        Func<GenesisRecordingRuleRepository> recordingRuleRepositoryFactory,
        CancellationToken cancellationToken = default) =>
        LoadWithRecordingRuleRepositoryFactoryAsync(favoriteStore, notificationStore, yellowPageStore,
            playbackPreferencesStore, soundPlayer, pathSource, recordingRuleRepositoryFactory,
            DefaultGenesisGuiNextConnectionPreferencesStore.Instance, cancellationToken);

    internal static ValueTask<GenesisGuiNextApplicationState> LoadWithRecordingRuleRepositoryFactoryAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        GenesisGuiNextPlaybackPreferencesSource? pathSource,
        Func<GenesisRecordingRuleRepository> recordingRuleRepositoryFactory,
        IGenesisGuiNextConnectionPreferencesStore connectionPreferencesStore,
        CancellationToken cancellationToken = default) =>
        LoadCoreAsync(
            favoriteStore,
            notificationStore,
            yellowPageStore,
            playbackPreferencesStore,
            soundPlayer,
            pathSource,
            (Func<GenesisRecordingRuleRepository>?)recordingRuleRepositoryFactory,
            connectionPreferencesStore,
            cancellationToken);

    internal static ValueTask<GenesisGuiNextApplicationState> LoadWithRecordingRuleRepositoryAndAnnounceOwnerAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        GenesisGuiNextPlaybackPreferencesSource? pathSource,
        Func<GenesisRecordingRuleRepository> recordingRuleRepositoryFactory,
        IGenesisGuiNextConnectionPreferencesStore connectionPreferencesStore,
        PeerCast.Nova.Network.Publishing.PublishingAnnounceOwner? announceOwner,
        CancellationToken cancellationToken = default) =>
        LoadCoreAsync(
            favoriteStore,
            notificationStore,
            yellowPageStore,
            playbackPreferencesStore,
            soundPlayer,
            pathSource,
            (Func<GenesisRecordingRuleRepository>?)recordingRuleRepositoryFactory,
            connectionPreferencesStore,
            cancellationToken,
            announceOwner: announceOwner);

    private static async ValueTask<GenesisGuiNextApplicationState> LoadCoreAsync(
        IGenesisFavoriteChannelStore favoriteStore,
        IGenesisChannelNotificationPreferenceStore notificationStore,
        IGenesisYellowPageConfigurationStore yellowPageStore,
        IGenesisGuiNextPlaybackPreferencesStore playbackPreferencesStore,
        IGenesisSystemNotificationSoundPlayer soundPlayer,
        GenesisGuiNextPlaybackPreferencesSource? pathSource,
        Func<GenesisRecordingRuleRepository>? recordingRuleRepositoryFactory,
        IGenesisGuiNextConnectionPreferencesStore connectionPreferencesStore,
        CancellationToken cancellationToken,
        Func<string, string?>? readEnvironment = null,
        PeerCast.Nova.Network.Publishing.PublishingAnnounceOwner? announceOwner = null)
    {
        ArgumentNullException.ThrowIfNull(favoriteStore);
        ArgumentNullException.ThrowIfNull(notificationStore);
        ArgumentNullException.ThrowIfNull(yellowPageStore);
        ArgumentNullException.ThrowIfNull(playbackPreferencesStore);
        ArgumentNullException.ThrowIfNull(connectionPreferencesStore);
        ArgumentNullException.ThrowIfNull(soundPlayer);
        cancellationToken.ThrowIfCancellationRequested();

        var loadFailures = GenesisGuiNextApplicationStateLoadFailure.None;
        IReadOnlyList<GenesisFavoriteChannel> loadedFavorites;
        try
        {
            loadedFavorites = await favoriteStore.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            loadFailures |= GenesisGuiNextApplicationStateLoadFailure.Favorites;
            loadedFavorites = Array.Empty<GenesisFavoriteChannel>();
        }

        GenesisChannelNotificationPreferences loadedNotifications;
        try
        {
            loadedNotifications = await notificationStore.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            loadFailures |=
                GenesisGuiNextApplicationStateLoadFailure.NotificationPreferences;
            loadedNotifications = GenesisChannelNotificationPreferences.Default;
        }

        GenesisYellowPageConfiguration loadedYellowPages;
        try
        {
            var definitions = await yellowPageStore.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            loadedYellowPages = new GenesisYellowPageConfiguration(definitions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            loadFailures |= GenesisGuiNextApplicationStateLoadFailure.YellowPages;
            loadedYellowPages = GenesisYellowPageConfiguration.CreateDefault();
        }

        GenesisGuiNextPlaybackPreferences loadedPlaybackPreferences;
        try
        {
            loadedPlaybackPreferences = await playbackPreferencesStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            loadFailures |= GenesisGuiNextApplicationStateLoadFailure.PlaybackPreferences;
            loadedPlaybackPreferences = GenesisGuiNextPlaybackPreferences.Default;
        }

        GenesisGuiNextConnectionPreferences loadedConnectionPreferences;
        try
        {
            loadedConnectionPreferences = await connectionPreferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            loadedConnectionPreferences.Validate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            loadFailures |= GenesisGuiNextApplicationStateLoadFailure.ConnectionPreferences;
            loadedConnectionPreferences = GenesisGuiNextConnectionPreferences.Default;
        }

        var recordingRuleRepository = (recordingRuleRepositoryFactory ??
            (static () => new GenesisRecordingRuleRepository(path: null)))();
        try
        {
            return new(
                new GenesisFavoriteChannelCollection(loadedFavorites),
                new GenesisChannelNotificationSettings(loadedNotifications, soundPlayer),
                loadedYellowPages,
                new GenesisGuiNextPlaybackSettings(loadedPlaybackPreferences, pathSource),
                new GenesisGuiNextConnectionSettings(loadedConnectionPreferences, connectionPreferencesStore,
                    (loadFailures & GenesisGuiNextApplicationStateLoadFailure.ConnectionPreferences) != 0),
                favoriteStore,
                notificationStore,
                yellowPageStore,
                playbackPreferencesStore,
                recordingRuleRepository,
                loadFailures, readEnvironment, announceOwner);
        }
        catch
        {
            await recordingRuleRepository.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Connection.BindLiveApply(null);

        Exception? publicChannelFailure = null;
        try
        {
            await RelayActivation.DisposeAsync().ConfigureAwait(false);
            await PublicChannels.DisposeAsync().ConfigureAwait(false);
            if (!RelayActivation.Snapshot.CleanupConfirmed)
                throw new InvalidOperationException("Local relay cleanup was not confirmed.");
        }
        catch (Exception exception) { publicChannelFailure = exception; }
        finally
        {
            await AnnounceOwner.DisposeAsync().ConfigureAwait(false);
            DownstreamRelays.Dispose();
            NetworkPolicies.Dispose();
        }
        await Tabs.DisposeAsync().ConfigureAwait(false);
        await Connection.DisposeAsync().ConfigureAwait(false);
        await _recordingRuleRepository.DisposeAsync().ConfigureAwait(false);

        if (LoadedSuccessfully(GenesisGuiNextApplicationStateLoadFailure.Favorites))
        {
            try
            {
                await _favoriteStore.SaveAsync(Favorites.Snapshot, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Persistence is auxiliary to finite application shutdown.
            }
        }

        if (LoadedSuccessfully(
                GenesisGuiNextApplicationStateLoadFailure.NotificationPreferences))
        {
            try
            {
                await _notificationStore.SaveAsync(
                        Notifications.Current,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // One failed preference save must not block the remaining shutdown path.
            }
        }

        if (LoadedSuccessfully(GenesisGuiNextApplicationStateLoadFailure.YellowPages))
        {
            try
            {
                await _yellowPageStore.SaveAsync(
                        YellowPages.Definitions,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // One failed configuration save must not block finite GUI shutdown.
            }
        }

        if (LoadedSuccessfully(GenesisGuiNextApplicationStateLoadFailure.PlaybackPreferences))
        {
            try
            {
                await _playbackPreferencesStore.SaveAsync(
                        Playback.Preferences,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // One failed preference save must not block finite GUI shutdown.
            }
        }
        if (publicChannelFailure is not null)
            throw new InvalidOperationException("Public channel shutdown failed.", publicChannelFailure);
    }

    private bool LoadedSuccessfully(GenesisGuiNextApplicationStateLoadFailure failure) =>
        (LoadFailures & failure) == 0;

    private bool TryPersistFavorites(
        IReadOnlyList<GenesisFavoriteChannel> favorites)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        try
        {
            // Add/remove are explicit user actions. Make the tiny local JSON
            // snapshot durable before publishing the membership change to the GUI.
            _favoriteStore.SaveAsync(favorites, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryPersistNotifications(
        GenesisChannelNotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        try
        {
            // Settings toggles are explicit and infrequent. Complete the tiny local
            // JSON write before publishing the new preference to notification work.
            _notificationStore.SaveAsync(preferences, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryPersistYellowPages(
        IReadOnlyList<GenesisYellowPageDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        try
        {
            // The settings window reports success only after the tiny local JSON
            // snapshot is durable. Keep this synchronous bridge scoped to explicit
            // user mutations; catalog/network work remains asynchronous.
            _yellowPageStore.SaveAsync(definitions, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryPersistPlaybackPreferences(GenesisGuiNextPlaybackPreferences preferences)
    {
        try
        {
            _playbackPreferencesStore.SaveAsync(
                    preferences,
                    CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal static class SystemGenesisGuiNextApplicationState
{
    internal const string ApplicationDirectoryName = PeerCast.Nova.Foundation.Configuration.NovaAppDataPaths.PrimaryDirectoryName;
    private const string FavoriteFileName = "favorites.json";
    private const string NotificationPreferenceFileName =
        "notification-preferences.json";
    private const string YellowPageConfigurationFileName = "yellow-pages.json";
    private const string PlaybackPreferencesFileName = "playback-preferences.json";
    private const string ConnectionPreferencesFileName = "connection-preferences.json";
    private const string PublishingBroadcastIdFileName = "publishing-bcid.txt";

    public static ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        CancellationToken cancellationToken = default) =>
        LoadAsync(pathSource: null, cancellationToken: cancellationToken);

    internal static async ValueTask<GenesisGuiNextApplicationState> LoadAsync(
        GenesisGuiNextPlaybackPreferencesSource? pathSource,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        GenesisGuiNextApplicationState state;
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            state = await GenesisGuiNextApplicationState.LoadWithRecordingRuleRepositoryFactoryAsync(
                    EmptyFavoriteStore.Instance,
                    EmptyNotificationPreferenceStore.Instance,
                    DefaultGenesisYellowPageConfigurationStore.Instance,
                    UnavailableGenesisGuiNextPlaybackPreferencesStore.Instance,
                    SystemGenesisSystemNotificationSoundPlayer.Instance,
                    pathSource,
                    GenesisRecordingRuleRepository.CreateSystem,
                    UnavailableGenesisGuiNextConnectionPreferencesStore.Instance,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var applicationDirectory = Path.Combine(
                localApplicationData,
                ApplicationDirectoryName);
            var announceOwner = PublishingBroadcastIdStore.LoadOrCreateOwner(
                Path.Combine(applicationDirectory, PublishingBroadcastIdFileName));
            state = await GenesisGuiNextApplicationState.LoadWithRecordingRuleRepositoryAndAnnounceOwnerAsync(
                    new JsonGenesisFavoriteChannelStore(
                        Path.Combine(applicationDirectory, FavoriteFileName)),
                    new JsonGenesisChannelNotificationPreferenceStore(
                        Path.Combine(applicationDirectory, NotificationPreferenceFileName)),
                    new JsonGenesisYellowPageConfigurationStore(
                        Path.Combine(applicationDirectory, YellowPageConfigurationFileName)),
                    new JsonGenesisGuiNextPlaybackPreferencesStore(
                        Path.Combine(applicationDirectory, PlaybackPreferencesFileName)),
                    SystemGenesisSystemNotificationSoundPlayer.Instance,
                    pathSource,
                    GenesisRecordingRuleRepository.CreateSystem,
                    new JsonGenesisGuiNextConnectionPreferencesStore(
                        Path.Combine(applicationDirectory, ConnectionPreferencesFileName)),
                    announceOwner,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        SystemGenesisGuiNextNotificationSettingsFeedback.Bind(state.Notifications);
        SystemGenesisGuiNextTestSoundFeedback.Bind(state.Notifications);
        return state;
    }

    internal static class PublishingBroadcastIdStore
    {
        internal static PeerCast.Nova.Network.Publishing.PublishingAnnounceOwner LoadOrCreateOwner(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            string? existingText = null;
            Guid? parsed = null;
            var existed = false;

            try
            {
                if (File.Exists(filePath))
                {
                    existed = true;
                    existingText = File.ReadAllText(filePath).Trim();
                    if (Guid.TryParse(existingText, out var candidate) && candidate != Guid.Empty)
                    {
                        parsed = candidate;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }

            var owner = new PeerCast.Nova.Network.Publishing.PublishingAnnounceOwner(parsed);
            var expectedText = owner.BroadcastId.ToString("N").ToUpperInvariant();
            if (!existed || !string.Equals(existingText, expectedText, StringComparison.Ordinal))
            {
                TryPersist(filePath, expectedText);
            }

            return owner;
        }

        private static void TryPersist(string filePath, string serializedBroadcastId)
        {
            try
            {
                var fullPath = Path.GetFullPath(filePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, serializedBroadcastId);
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
    }

    private sealed class EmptyFavoriteStore : IGenesisFavoriteChannelStore
    {
        public static EmptyFavoriteStore Instance { get; } = new();

        private EmptyFavoriteStore()
        {
        }

        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>(
                Array.Empty<GenesisFavoriteChannel>());
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisFavoriteChannel> favorites,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(favorites);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnavailableGenesisGuiNextPlaybackPreferencesStore :
        IGenesisGuiNextPlaybackPreferencesStore
    {
        public static UnavailableGenesisGuiNextPlaybackPreferencesStore Instance { get; } = new();

        private UnavailableGenesisGuiNextPlaybackPreferencesStore()
        {
        }

        public ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisGuiNextPlaybackPreferences.Default);
        }

        public ValueTask SaveAsync(
            GenesisGuiNextPlaybackPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Local application data is unavailable.");
        }
    }

    private sealed class EmptyNotificationPreferenceStore :
        IGenesisChannelNotificationPreferenceStore
    {
        public static EmptyNotificationPreferenceStore Instance { get; } = new();

        private EmptyNotificationPreferenceStore()
        {
        }

        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        }

        public ValueTask SaveAsync(
            GenesisChannelNotificationPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}

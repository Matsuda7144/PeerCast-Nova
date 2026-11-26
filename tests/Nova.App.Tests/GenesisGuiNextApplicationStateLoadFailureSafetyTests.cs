namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextApplicationStateLoadFailureSafetyTests
{
    [Fact]
    public async Task LoadFailuresFallBackWithoutPassiveOverwrite()
    {
        var favoriteStore = new RecordingFavoriteStore(loadThrows: true);
        var notificationStore = new RecordingNotificationStore(loadThrows: true);
        var yellowPageStore = new RecordingYellowPageStore(loadThrows: true);
        var playbackStore = new RecordingPlaybackStore(loadThrows: true);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            yellowPageStore,
            playbackStore,
            SilentSoundPlayer.Instance);

        Assert.Equal(
            GenesisGuiNextApplicationStateLoadFailure.Favorites |
            GenesisGuiNextApplicationStateLoadFailure.NotificationPreferences |
            GenesisGuiNextApplicationStateLoadFailure.YellowPages |
            GenesisGuiNextApplicationStateLoadFailure.PlaybackPreferences,
            state.LoadFailures);
        Assert.Empty(state.Favorites.Snapshot);
        Assert.Equal(
            GenesisChannelNotificationPreferences.Default,
            state.Notifications.Current);
        Assert.Equal(
            [GenesisYellowPageDefaults.Sp],
            state.YellowPages.Definitions);

        await state.DisposeAsync();

        Assert.Equal(0, favoriteStore.SaveCount);
        Assert.Equal(0, notificationStore.SaveCount);
        Assert.Equal(0, yellowPageStore.SaveCount);
        Assert.Equal(0, playbackStore.SaveCount);
    }

    [Fact]
    public async Task ShutdownRetriesOnlyDomainsThatLoadedSuccessfully()
    {
        var favoriteStore = new RecordingFavoriteStore(loadThrows: true);
        var notificationStore = new RecordingNotificationStore();
        var yellowPageStore = new RecordingYellowPageStore();
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            favoriteStore,
            notificationStore,
            yellowPageStore,
            SilentSoundPlayer.Instance);

        Assert.Equal(
            GenesisGuiNextApplicationStateLoadFailure.Favorites,
            state.LoadFailures);

        await state.DisposeAsync();

        Assert.Equal(0, favoriteStore.SaveCount);
        Assert.Equal(1, notificationStore.SaveCount);
        Assert.Equal(1, yellowPageStore.SaveCount);
    }

    [Fact]
    public async Task ExplicitMutationCanPersistAfterLoadFailure()
    {
        var notificationStore = new RecordingNotificationStore(loadThrows: true);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            new RecordingFavoriteStore(),
            notificationStore,
            new RecordingYellowPageStore(),
            SilentSoundPlayer.Instance);

        Assert.Equal(
            GenesisChannelNotificationSettingsMutationResult.Changed,
            state.Notifications.SetPlaySound(false));
        Assert.Equal(1, notificationStore.SaveCount);
        Assert.False(notificationStore.Saved!.PlaySound);

        await state.DisposeAsync();

        Assert.Equal(1, notificationStore.SaveCount);
    }

    [Fact]
    public async Task ExplicitPlaybackSettingCanPersistAfterLoadFailure()
    {
        using var directory = new TemporaryDirectory();
        var executablePath = directory.CreateExecutableFile();
        var playbackStore = new RecordingPlaybackStore(loadThrows: true);
        var state = await GenesisGuiNextApplicationState.LoadAsync(
            new RecordingFavoriteStore(),
            new RecordingNotificationStore(),
            new RecordingYellowPageStore(),
            playbackStore,
            SilentSoundPlayer.Instance);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            state.Playback.SetPcrPlayerExecutablePath(executablePath));
        Assert.Equal(1, playbackStore.SaveCount);
        Assert.NotNull(playbackStore.Saved?.PcrPlayerExecutablePath);

        await state.DisposeAsync();

        // The explicit settings action persisted, but shutdown did not retry and
        // replace the value that could not be loaded at startup.
        Assert.Equal(1, playbackStore.SaveCount);
    }

    [Fact]
    public void ResolverListsFailedDomainsInStableOrder()
    {
        var presentation = GenesisGuiNextApplicationStateLoadFeedback.Resolve(
            GenesisGuiNextApplicationStateLoadFailure.Favorites |
            GenesisGuiNextApplicationStateLoadFailure.NotificationPreferences |
            GenesisGuiNextApplicationStateLoadFailure.YellowPages |
            GenesisGuiNextApplicationStateLoadFailure.PlaybackPreferences);

        Assert.NotNull(presentation);
        Assert.Equal(
            "Some saved GUI data could not be loaded. Defaults are being used for: Favorites, Notification settings, Yellow Pages, Playback settings. The affected saved data will not be overwritten automatically; changing those items can replace it.",
            presentation.Message);
        Assert.Equal("PeerCast Nova", presentation.Caption);
    }

    [Fact]
    public void NoLoadFailuresProduceNoFeedback()
    {
        var presented = new List<GenesisGuiNextApplicationStateLoadFeedbackPresentation>();

        GenesisGuiNextApplicationStateLoadFeedback.Report(
            GenesisGuiNextApplicationStateLoadFailure.None,
            presented.Add);

        Assert.Null(GenesisGuiNextApplicationStateLoadFeedback.Resolve(
            GenesisGuiNextApplicationStateLoadFailure.None));
        Assert.Empty(presented);
    }

    [Fact]
    public void FeedbackFailureCannotFaultStartupFallback()
    {
        GenesisGuiNextApplicationStateLoadFeedback.Report(
            GenesisGuiNextApplicationStateLoadFailure.Favorites,
            static _ => throw new InvalidOperationException("Presentation failed."));
    }

    [Fact]
    public void SystemApplicationStateDefersLoadFailurePresentationToOwnedSurface()
    {
        var stateSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextApplicationState.cs"));
        var surfaceSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisInteractiveMainWindowSurfaceTarget.cs"));

        Assert.DoesNotContain(
            "SystemGenesisGuiNextApplicationStateLoadFeedback.Report",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemGenesisGuiNextApplicationStateLoadFeedback.Report(\n" +
            "                windowToken,\n" +
            "                guiNextState.LoadFailures);",
            surfaceSource,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        RepositorySourceTestHelper.FindRepositoryFile(relativeParts);

    private sealed class RecordingFavoriteStore : IGenesisFavoriteChannelStore
    {
        private readonly bool _loadThrows;

        public RecordingFavoriteStore(bool loadThrows = false)
        {
            _loadThrows = loadThrows;
        }

        public int SaveCount { get; private set; }

        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadThrows)
            {
                throw new InvalidDataException("Favorite load failed.");
            }

            return ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>(
                Array.Empty<GenesisFavoriteChannel>());
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisFavoriteChannel> favorites,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(favorites);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingNotificationStore :
        IGenesisChannelNotificationPreferenceStore
    {
        private readonly bool _loadThrows;

        public RecordingNotificationStore(bool loadThrows = false)
        {
            _loadThrows = loadThrows;
        }

        public GenesisChannelNotificationPreferences? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadThrows)
            {
                throw new InvalidDataException("Notification preference load failed.");
            }

            return ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        }

        public ValueTask SaveAsync(
            GenesisChannelNotificationPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            Saved = preferences;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingYellowPageStore :
        IGenesisYellowPageConfigurationStore
    {
        private readonly bool _loadThrows;

        public RecordingYellowPageStore(bool loadThrows = false)
        {
            _loadThrows = loadThrows;
        }

        public int SaveCount { get; private set; }

        public ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadThrows)
            {
                throw new InvalidDataException("Yellow Page load failed.");
            }

            return ValueTask.FromResult<IReadOnlyList<GenesisYellowPageDefinition>>(
                [GenesisYellowPageDefaults.Sp]);
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisYellowPageDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPlaybackStore(bool loadThrows = false) :
        IGenesisGuiNextPlaybackPreferencesStore
    {
        public GenesisGuiNextPlaybackPreferences? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (loadThrows)
            {
                throw new InvalidDataException("Playback settings load failed.");
            }

            return ValueTask.FromResult(GenesisGuiNextPlaybackPreferences.Default);
        }

        public ValueTask SaveAsync(
            GenesisGuiNextPlaybackPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            Saved = preferences;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SilentSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public static SilentSoundPlayer Instance { get; } = new();

        private SilentSoundPlayer()
        {
        }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"NovaPlaybackLoadFailure-{Guid.NewGuid():N}");

        public string CreateExecutableFile()
        {
            var filePath = System.IO.Path.Combine(
                Path,
                PcrPlayerExternalPlayerLauncher.ExecutableFileName);
            Directory.CreateDirectory(Path);
            File.WriteAllBytes(filePath, []);
            return filePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

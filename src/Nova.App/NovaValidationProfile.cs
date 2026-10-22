namespace PeerCast.Nova.App;

/// <summary>
/// Prepared before native startup. Only explicit CLI selection uses these
/// stores. No production documents are read/copied and no fallback is allowed.
/// </summary>
internal sealed class NovaValidationProfile : IAsyncDisposable
{
    private readonly FileStream lease;
    private readonly GenesisGuiNextApplicationState state;
    private readonly List<IAsyncDisposable> owners = [];
    private bool disposed;

    private NovaValidationProfile(NovaValidationProfilePaths paths, FileStream lease,
        GenesisGuiNextApplicationState state, NovaSettingsStore settings,
        GenesisRecordingHistoryStore history)
    {
        Paths = paths;
        this.lease = lease;
        this.state = state;
        Settings = settings;
        History = history;
        RecordingSettings = new(settings, paths.Recordings);
        if (RecordingSettings.Presentation.Error is not null)
            throw new IOException("Validation recording settings are unavailable.");
    }

    internal NovaValidationProfilePaths Paths { get; }
    internal NovaSettingsStore Settings { get; }
    internal GenesisRecordingHistoryStore History { get; }
    internal GenesisRecordingOutputDirectorySettings RecordingSettings { get; }

    internal static Task<NovaValidationProfile> CreateSystemAsync(string root, CancellationToken token)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new IOException("Production roots cannot be determined.");
        return CreateAsync(root,
            [
                Path.Combine(local, "PeerCast", "Nova"),
                Path.Combine(local, "PeerCast Nova"),
                GenesisRecordingOutputDirectorySettings.ResolveDefaultDirectory(),
            ], Environment.GetEnvironmentVariable, token);
    }

    internal static async Task<NovaValidationProfile> CreateAsync(string root,
        IReadOnlyList<string> productionRoots, Func<string, string?> readEnvironment,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        // Never silently ignore ambient overrides or let diagnostics escape.
        foreach (var name in new[] { "NOVA_AUTO_PORT_MAPPING", RecordingTransformDiagnosticFile.DirectoryEnvironmentVariable })
            if (!string.IsNullOrWhiteSpace(readEnvironment(name)))
                throw new InvalidOperationException("Validation environment override is not allowed.");
        var paths = new NovaValidationProfilePaths(root, productionRoots);
        var marker = paths.Resolve(NovaValidationProfilePaths.Marker);
        var existed = Directory.Exists(paths.Root);
        if (existed && (!File.Exists(marker) ||
            File.ReadAllText(marker) != NovaValidationProfilePaths.MarkerValue))
            throw new IOException("An existing unowned directory is not a validation profile.");
        Directory.CreateDirectory(paths.Root);
        FileStream? lease = null;
        GenesisGuiNextApplicationState? state = null;
        try
        {
            paths.CheckAll();
            lease = new FileStream(paths.Resolve(".validation.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            foreach (var directory in NovaValidationProfilePaths.Directories)
            {
                var path = paths.Resolve(directory);
                Directory.CreateDirectory(path);
                // Fail startup before any native host on missing write access.
                var probe = paths.Resolve(directory, ".probe-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await File.WriteAllTextAsync(probe, "probe", token).ConfigureAwait(false);
                    _ = await File.ReadAllTextAsync(probe, token).ConfigureAwait(false);
                }
                finally { File.Delete(probe); }
            }
            var settings = new NovaSettingsStore(paths.Settings, paths.CheckAll, value =>
            {
                if (value.RecordingOutputDirectory is { } output) paths.Check(output);
            });
            _ = settings.Load(); // Strict validation before the legacy fallback constructor.
            var history = new GenesisRecordingHistoryStore(paths.History,
                checkAccess: paths.CheckAll, validate: paths.CheckMetadata);
            var loaded = history.Load();
            if (!loaded.CanWrite) throw new IOException("Validation History is unavailable.");
            await using (var rules = new GenesisRecordingRuleRepository(paths.Rules, checkAccess: paths.CheckAll))
                if (!(await rules.GetSnapshotAsync(token).ConfigureAwait(false)).IsAvailable)
                    throw new IOException("Validation Rules are unavailable.");
            state = await GenesisGuiNextApplicationState.LoadWithRecordingRuleRepositoryFactoryAsync(
                new JsonGenesisFavoriteChannelStore(paths.Favorites, paths.CheckAll),
                new JsonGenesisChannelNotificationPreferenceStore(paths.Notifications, paths.CheckAll),
                new JsonGenesisYellowPageConfigurationStore(paths.YellowPages, paths.CheckAll),
                new JsonGenesisGuiNextPlaybackPreferencesStore(paths.Playback, paths.CheckAll),
                SystemGenesisSystemNotificationSoundPlayer.Instance, null,
                () => new GenesisRecordingRuleRepository(paths.Rules, checkAccess: paths.CheckAll),
                new JsonGenesisGuiNextConnectionPreferencesStore(paths.Connection, paths.CheckAll), token)
                .ConfigureAwait(false);
            if (state.LoadFailures != GenesisGuiNextApplicationStateLoadFailure.None)
                throw new IOException("Validation preferences or Rules are unavailable.");
            paths.CheckAll();
            token.ThrowIfCancellationRequested();
            if (!existed) await File.WriteAllTextAsync(marker, NovaValidationProfilePaths.MarkerValue, token).ConfigureAwait(false);
            var profile = new NovaValidationProfile(paths, lease, state, settings, history);
            SystemGenesisGuiNextNotificationSettingsFeedback.Bind(state.Notifications);
            SystemGenesisGuiNextTestSoundFeedback.Bind(state.Notifications);
            return profile;
        }
        catch
        {
            if (state is not null) await state.DisposeAsync().ConfigureAwait(false);
            lease?.Dispose();
            // Retain isolated partial outputs. Never clean/copy a production path.
            throw;
        }
    }

    internal void CheckReady()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Paths.CheckAll();
        _ = Settings.Load();
    }

    internal GenesisGuiNextApplicationState GetGuiState(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckReady();
        return state;
    }

    internal INovaApplicationNetworkOwner CreateNetworkOwner()
    {
        CheckReady();
        return new NovaApplicationNetworkOwner(Settings, static () => null,
            static options => new NovaValidationNatRuntime(options),
            createPublicRelayDriver: static _ => new NovaValidationPublicRelayDriver());
    }

    internal IPlaybackSessionController CreatePlaybackController(GenesisGuiNextPlaybackPathSource paths)
    {
        CheckReady();
        var directory = Paths.Resolve("diagnostics", "run-" + Guid.NewGuid().ToString("N"));
        var diagnostic = RecordingTransformDiagnosticFile.TryCreate(directory) ??
            throw new IOException("Validation diagnostics are unavailable.");
        owners.Add(diagnostic);
        var recording = new PcpRecordingRuntimeComposition(RecordingSettings.CurrentRuntimeDirectory, History,
            defaultRecordingDirectory: Paths.Recordings,
            libraryFileSystem: new NovaValidationLibraryFileSystem(Paths), diagnostics: diagnostic);
        owners.Add(recording);
        var daily = new NovaDailyHttpFlvPlaybackController(
            PcrPlayerExternalPlayerLauncher.CreateWithPathProvider(() => paths.Current),
            static _ => { }, static _ => { }, recording, new GenesisSharedPcpPlaybackRuntime(recording),
            new NovaValidationRuleProvider(Paths));
        var controller = new NovaValidationPlaybackController(new GuiNextDailyPlaybackSessionController(daily));
        owners.Add(controller);
        return controller;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        List<Exception> failures = [];
        try
        {
            foreach (var owner in owners.AsEnumerable().Reverse())
                try { await owner.DisposeAsync().ConfigureAwait(false); } catch (Exception e) { failures.Add(e); }
            try { await state.DisposeAsync().ConfigureAwait(false); } catch (Exception e) { failures.Add(e); }
        }
        finally { await lease.DisposeAsync().ConfigureAwait(false); }
        if (failures.Count != 0) throw new AggregateException(failures);
    }
}

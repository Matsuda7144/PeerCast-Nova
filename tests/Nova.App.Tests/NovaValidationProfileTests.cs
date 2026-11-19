using System.Diagnostics;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaValidationProfileTests
{
    [Fact]
    public void NormalOptionsDoNotImplicitlySelectProfile()
    {
        Assert.Null(new NovaWindowsStartupOptions().ValidationProfile);
        Assert.Equal(GenesisRecordingOutputDirectorySettings.ResolveDefaultDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            GenesisRecordingOutputDirectorySettings.ResolveDefaultDirectory());
    }

    [Fact]
    public async Task NormalEntrypointUsesOnlyExistingComposition()
    {
        var normal = 0; var validation = 0;
        var result = await NovaApplicationEntrypoint.RunAsync([], new StringWriter(), new StringWriter(),
            (_, _) => { normal++; return ValueTask.FromResult(NovaWindowsStartupResult.Completed); },
            CancellationToken.None, (_, _) => { validation++; throw new InvalidOperationException(); });
        Assert.Equal(0, result); Assert.Equal(1, normal); Assert.Equal(0, validation);
    }

    [Fact]
    public async Task ExplicitProfileSelectsOnlyValidationComposition()
    {
        using var temp = new Fixture();
        var normal = 0; string? selected = null;
        var result = await NovaApplicationEntrypoint.RunAsync(["--validation-profile", temp.Root],
            new StringWriter(), new StringWriter(),
            (_, _) => { normal++; throw new InvalidOperationException(); }, CancellationToken.None,
            (root, _) => { selected = root; return ValueTask.FromResult(NovaWindowsStartupResult.Completed); });
        Assert.Equal(0, result); Assert.Equal(temp.Root, selected); Assert.Equal(0, normal);
        Assert.False(Directory.Exists(temp.Root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("../escape")]
    [InlineData("--validation-profile")]
    public async Task InvalidCliRootCannotDispatch(string root)
    {
        var normal = 0; var validation = 0;
        Assert.Equal(2, await NovaApplicationEntrypoint.RunAsync(["--validation-profile", root],
            new StringWriter(), new StringWriter(),
            (_, _) => { normal++; throw new InvalidOperationException(); }, CancellationToken.None,
            (_, _) => { validation++; throw new InvalidOperationException(); }));
        Assert.Equal(0, normal); Assert.Equal(0, validation);
    }

    [Theory]
    [InlineData("equal")]
    [InlineData("inside")]
    [InlineData("ancestor")]
    public void ProductionOverlapRejectedBeforeCreatingDirectory(string direction)
    {
        using var temp = new Fixture();
        var production = direction == "equal" ? temp.Root :
            direction == "inside" ? temp.Base : Path.Combine(temp.Root, "production");
        Assert.Throws<ArgumentException>(() => new NovaValidationProfilePaths(temp.Root, [production]));
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public void AbsoluteTraversalRejected()
    {
        using var temp = new Fixture();
        Assert.False(NovaValidationProfilePaths.IsAbsoluteRoot(temp.Root + "/../escape"));
        Assert.Throws<ArgumentException>(() => new NovaValidationProfilePaths(temp.Root + "/../escape", []));
    }

    [Theory]
    [InlineData("NOVA_AUTO_PORT_MAPPING", "on")]
    [InlineData("NOVA_AUTO_PORT_MAPPING", "off")]
    [InlineData("NOVA_AUTO_PORT_MAPPING", "invalid")]
    [InlineData("NOVA_RECORDING_TRANSFORM_DIAGNOSTICS_DIRECTORY", "outside")]
    public async Task AmbientOverrideFailsClosedBeforeFilesystem(string name, string value)
    {
        using var temp = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => NovaValidationProfile.CreateAsync(
            temp.Root, [], key => key == name ? value : null));
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public async Task ExistingUnownedDirectoryIsNeverReadAsProfile()
    {
        using var temp = new Fixture();
        Directory.CreateDirectory(temp.Root);
        var sentinel = Path.Combine(temp.Root, "settings.json");
        await File.WriteAllTextAsync(sentinel, "do-not-read-as-Nova-options");
        await Assert.ThrowsAsync<IOException>(() => temp.Create());
        Assert.Equal("do-not-read-as-Nova-options", await File.ReadAllTextAsync(sentinel));
        Assert.False(File.Exists(Path.Combine(temp.Root, NovaValidationProfilePaths.Marker)));
    }

    [Fact]
    public async Task AllStoragePathsAndTemporaryOutputsAreInsideRootWithoutCopying()
    {
        using var temp = new Fixture();
        Directory.CreateDirectory(temp.Production);
        var sentinel = Path.Combine(temp.Production, "settings.json");
        await File.WriteAllTextAsync(sentinel, "not-a-Nova-document");
        await using var profile = await temp.Create();
        Assert.False(profile.Settings.Load().AutoPortMapping);
        Assert.Equal(profile.Paths.Recordings, profile.RecordingSettings.CurrentRuntimeDirectory);
        foreach (var name in NovaValidationProfilePaths.Directories)
            Assert.True(Directory.Exists(profile.Paths.Resolve(name)));
        await profile.Settings.SaveAsync(new NovaOptions { AutoPortMapping = true });
        await new JsonGenesisFavoriteChannelStore(profile.Paths.Favorites).SaveAsync([]);
        await new JsonGenesisChannelNotificationPreferenceStore(profile.Paths.Notifications)
            .SaveAsync(GenesisChannelNotificationPreferences.Default);
        await new JsonGenesisYellowPageConfigurationStore(profile.Paths.YellowPages).SaveAsync([]);
        await new JsonGenesisGuiNextPlaybackPreferencesStore(profile.Paths.Playback)
            .SaveAsync(GenesisGuiNextPlaybackPreferences.Default);
        var connection = profile.GetGuiState(CancellationToken.None).Connection;
        var runtimeSettingsBefore = await File.ReadAllTextAsync(profile.Paths.Settings);
        Assert.True((await connection.SaveAsync(GenesisGuiNextConnectionInput.From(
            connection.Startup with { ListenPort = 17144 }))).Saved);
        Assert.Equal(17144, (await new JsonGenesisGuiNextConnectionPreferencesStore(profile.Paths.Connection).LoadAsync()).ListenPort);
        Assert.Equal(7144, connection.Startup.ListenPort);
        Assert.Equal(runtimeSettingsBefore, await File.ReadAllTextAsync(profile.Paths.Settings));
        Assert.All(new[] { profile.Paths.Settings, profile.Paths.Favorites, profile.Paths.Notifications,
            profile.Paths.YellowPages, profile.Paths.Playback, profile.Paths.Connection, profile.Paths.History, profile.Paths.Rules,
            profile.Paths.Recordings, profile.Paths.Diagnostics }, path => Assert.StartsWith(temp.Root, path));
        Assert.Equal("not-a-Nova-document", await File.ReadAllTextAsync(sentinel));
        Assert.DoesNotContain(Directory.EnumerateFiles(temp.Root, "*", SearchOption.AllDirectories),
            path => path.Contains(".probe-", StringComparison.Ordinal) || path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeasePreventsConcurrentProfileAndCanBeReopenedAfterClose()
    {
        using var temp = new Fixture();
        var first = await temp.Create();
        await Assert.ThrowsAsync<IOException>(() => temp.Create());
        await first.DisposeAsync();
        await first.DisposeAsync();
        await using var second = await temp.Create();
        second.CheckReady();
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("favorites")]
    [InlineData("notifications")]
    [InlineData("yellow-pages")]
    [InlineData("playback")]
    [InlineData("connection")]
    [InlineData("history")]
    [InlineData("rules")]
    public async Task CorruptIsolatedDocumentFailsBeforeNativeStartup(string document)
    {
        using var temp = new Fixture();
        var profile = await temp.Create();
        var path = document switch
        {
            "settings" => profile.Paths.Settings,
            "favorites" => profile.Paths.Favorites,
            "notifications" => profile.Paths.Notifications,
            "yellow-pages" => profile.Paths.YellowPages,
            "playback" => profile.Paths.Playback,
            "connection" => profile.Paths.Connection,
            "history" => profile.Paths.History,
            _ => profile.Paths.Rules,
        };
        await profile.DisposeAsync();
        await File.WriteAllTextAsync(path, "{bad");
        await Assert.ThrowsAnyAsync<Exception>(() => temp.Create());
        Assert.Equal("{bad", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConfiguredRecordingPathOutsideProfileFailsWithoutDefaultFallback()
    {
        using var temp = new Fixture();
        var profile = await temp.Create();
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Unavailable,
            profile.RecordingSettings.Save(temp.Production));
        Assert.Null(profile.Settings.Load().RecordingOutputDirectory);
        var settingsPath = profile.Paths.Settings;
        await profile.DisposeAsync();
        await new NovaSettingsStore(settingsPath).SaveAsync(new NovaOptions { RecordingOutputDirectory = temp.Production });
        await Assert.ThrowsAsync<IOException>(() => temp.Create());
        Assert.False(Directory.Exists(temp.Production));
    }

    [Fact]
    public async Task HistoryKnownRootOutsideProfileFailsClosed()
    {
        using var temp = new Fixture();
        var profile = await temp.Create();
        var path = profile.Paths.History;
        await profile.DisposeAsync();
        var document = new GenesisRecordingMetadataSnapshot(new(0, [], 0, 0),
            new(0, [new(temp.Production, GenesisRecordingRootProvenance.Explicit)]));
        await File.WriteAllBytesAsync(path, GenesisRecordingHistoryJsonCodec.Serialize(document));
        await Assert.ThrowsAsync<IOException>(() => temp.Create());
        Assert.False(Directory.Exists(temp.Production));
    }

    [Fact]
    public async Task WriterFactOutsideProfileIsNeverAdmittedForLibraryProbing()
    {
        using var temp = new Fixture();
        var profile = await temp.Create();
        var path = profile.Paths.History;
        await profile.DisposeAsync();
        var time = DateTimeOffset.UtcNow;
        var fact = new RecordingSessionTerminalFact(
            RecordingSinkSession.Manual(new(Guid.NewGuid()), new(Guid.NewGuid())),
            null, "FLV", ".flv", time, time, RecordingSinkSessionLifecycleKind.Completed,
            RecordingFileWriteResult.FromFinalizedFacts(Path.Combine(temp.Production, "outside.flv"), 1,
                RecordingFileWriteCompletionKind.Stopped));
        await File.WriteAllBytesAsync(path, GenesisRecordingHistoryJsonCodec.Serialize(
            new GenesisRecordingMetadataSnapshot(new(0, [fact], 0, 0), new(0, []))));
        await Assert.ThrowsAsync<IOException>(() => temp.Create());
        Assert.False(Directory.Exists(temp.Production));
    }

    [Fact]
    public async Task AccessFailureStopsStoreWithoutWritingOrFallback()
    {
        using var temp = new Fixture();
        var store = new NovaSettingsStore(Path.Combine(temp.Root, "settings.json"),
            () => throw new UnauthorizedAccessException("fake access boundary"));
        Assert.Throws<UnauthorizedAccessException>(() => store.Load());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(new NovaOptions()));
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public void RepositoryAncestorIsRejectedWithoutWritingEvidenceIntoSource()
    {
        using var temp = new Fixture();
        Directory.CreateDirectory(Path.Combine(temp.Base, ".git"));
        Assert.Throws<IOException>(() => new NovaValidationProfilePaths(temp.Root, []));
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public async Task ValidationFailureDoesNotFallBackToNormalStartup()
    {
        using var temp = new Fixture();
        var normal = 0;
        var error = new StringWriter();
        Assert.Equal(1, await NovaApplicationEntrypoint.RunAsync(["--validation-profile", temp.Root],
            new StringWriter(), error, (_, _) => { normal++; throw new InvalidOperationException(); },
            CancellationToken.None, (_, _) => throw new IOException("private details")));
        Assert.Equal(0, normal);
        Assert.DoesNotContain("private details", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("No production fallback", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnwritableStorageFailsWithoutFallbackOrActiveLease()
    {
        using var temp = new Fixture();
        var profile = await temp.Create();
        await profile.DisposeAsync();
        var blocked = Path.Combine(temp.Root, "rules");
        Directory.Delete(blocked);
        await File.WriteAllTextAsync(blocked, "blocking-directory");
        await Assert.ThrowsAnyAsync<IOException>(() => temp.Create());
        using var lease = new FileStream(Path.Combine(temp.Root, ".validation.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal("blocking-directory", await File.ReadAllTextAsync(blocked));
    }

    [Fact]
    public async Task CancellationBeforeInitializationCreatesNoStorage()
    {
        using var temp = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NovaValidationProfile.CreateAsync(temp.Root, [], _ => null, cancellation.Token));
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public async Task NatDefaultOffAndTogglePersistsWithNoNetworkOrRouterEvidence()
    {
        using var temp = new Fixture();
        await using var profile = await temp.Create();
        await using var owner = profile.CreateNetworkOwner();
        Assert.True((await owner.InitializeAsync()).Initialized);
        var adapter = new GuiNextNetworkSettingsAdapter(owner);
        Assert.False(adapter.ReadSnapshot().RuntimeDesiredEnabled);
        Assert.False(adapter.ReadSnapshot().StartupEnvironmentOverride);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved,
            (await adapter.SetAutoPortMappingAsync(true)).Status);
        var snapshot = adapter.ReadSnapshot();
        Assert.True(snapshot.RuntimeDesiredEnabled);
        Assert.Equal(NovaApplicationPublicListenerAvailability.Unavailable, snapshot.PublicListenerAvailability);
        // Assert the existing typed safety evidence without importing #265's UI.
        var nat = Assert.IsType<NatPortMappingDisplay>(snapshot.Nat);
        Assert.Null(nat.Network);
        Assert.Null(nat.ExternalAddress);
        Assert.Null(nat.ExternalPort);
        Assert.Null(nat.Lease);
        Assert.Equal(NatLeaseEvidenceKind.None, nat.LeaseEvidence);
        Assert.Equal(NatDisplayReachability.NotChecked, nat.ExternalReachability);
        Assert.Empty(nat.RegisteredInternalPorts);
        Assert.True(profile.Settings.Load().AutoPortMapping);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved,
            (await adapter.SetAutoPortMappingAsync(false)).Status);
        Assert.False(profile.Settings.Load().AutoPortMapping);
    }

    [Fact]
    public async Task SafetyBackendNeverReturnsRouterSuccess()
    {
        var result = await new NovaValidationMappingBackend().ExecuteAsync(null!, CancellationToken.None);
        Assert.Equal(NatBackendOutcome.NotSent, result.Outcome);
        Assert.Null(result.ExternalPort);
        Assert.Null(result.RouterEpochSeconds);
        Assert.Null(new NovaValidationNetworkSource().GetCurrentNetwork());
    }

    [Fact]
    public async Task PublicDriverCannotBindEvenAfterExplicitAcquire()
    {
        using var temp = new Fixture();
        await using var profile = await temp.Create();
        await using var owner = profile.CreateNetworkOwner();
        Assert.True((await owner.InitializeAsync()).Initialized);
        var request = new NovaPublicRelayRequest(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()),
            "pcp://192.0.2.10:7144/" + Guid.NewGuid().ToString("N"));
        // Mismatched identity may reject sooner; no successful bind is possible.
        var result = await owner.AcquirePublicRelayAsync(request);
        Assert.Null(result.Lease);
        Assert.NotEqual(NovaPublicRelayFailure.None, result.Failure);
        var driver = new NovaValidationPublicRelayDriver();
        Assert.Equal(NovaPublicRelayFailure.ListenFailed, (await driver.StartAsync(null!, CancellationToken.None)).Failure);
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task DiagnosticsIsolatedAndMediaCommandsNeverForward()
    {
        using var temp = new Fixture();
        await using var profile = await temp.Create();
        var controller = profile.CreatePlaybackController(new());
        Assert.IsType<NovaValidationPlaybackController>(controller);
        Assert.False(controller is IGenesisManualRecordingSessionController);
        Assert.False(controller is IGenesisAutomaticRecordingCatalogObserver);
        Assert.Equal(PlaybackSessionCommandResult.Rejected, (await controller.PlayAsync("pcp://192.0.2.10:7144/" +
            Guid.NewGuid().ToString("N"))).Result);
        Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
        Assert.Equal(PlaybackSessionCommandResult.Completed, (await controller.StopAsync()).Result);
        Assert.Equal(PlaybackSessionCommandResult.Completed, (await controller.StopAsync()).Result);
        Assert.Single(Directory.GetFiles(profile.Paths.Diagnostics, RecordingTransformDiagnosticFile.FileName,
            SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(profile.Paths.Recordings));
        await ((IAsyncDisposable)controller).DisposeAsync();
    }

    [Fact]
    public async Task AncestorReparseIsRejectedBeforeAnyProfileFileRead()
    {
        using var temp = new Fixture();
        Directory.CreateDirectory(temp.Production);
        var link = Path.Combine(temp.Base, "link");
        MakeLink(link, temp.Production);
        Assert.Throws<IOException>(() => new NovaValidationProfilePaths(Path.Combine(link, "child"), []));
        await Assert.ThrowsAsync<IOException>(() => NovaValidationProfile.CreateAsync(
            Path.Combine(link, "child"), [], _ => null));
        Assert.False(Directory.Exists(Path.Combine(temp.Production, "child")));
        Directory.Delete(link);
    }

    [Fact]
    public async Task RuntimeReparseReplacementRejectsSettingsAndPreferenceWrites()
    {
        using var temp = new Fixture();
        await using var profile = await temp.Create();
        Directory.CreateDirectory(temp.Production);
        var preferences = Path.Combine(temp.Root, "preferences");
        Directory.Delete(preferences);
        MakeLink(preferences, temp.Production);
        await Assert.ThrowsAsync<IOException>(() => profile.Settings.SaveAsync(new NovaOptions()));
        await Assert.ThrowsAsync<IOException>(() => new JsonGenesisFavoriteChannelStore(
            profile.Paths.Root + "/preferences/favorites.json", profile.Paths.CheckAll).SaveAsync([]).AsTask());
        Assert.Empty(Directory.GetFiles(temp.Production));
        Directory.Delete(preferences);
        Directory.CreateDirectory(preferences);
    }

    [Fact]
    public async Task HistoryLockReparseCannotEscapeProfile()
    {
        using var temp = new Fixture();
        var profile = await temp.Create();
        var path = profile.Paths.History + ".lock";
        await profile.DisposeAsync();
        Directory.CreateDirectory(temp.Production);
        MakeLink(path, temp.Production);
        await Assert.ThrowsAsync<IOException>(() => temp.Create());
        Assert.Empty(Directory.GetFiles(temp.Production));
        Directory.Delete(path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingRootLifetimeHandlesNormalCloseAndStartupFailure(bool fail)
    {
        using var temp = new Fixture();
        await using var profile = await temp.Create();
        var host = new Host(fail);
        var surface = new Surface();
        var composition = new NovaWindowsStartupComposition(
            new() { ValidationProfile = profile, ChannelCatalog = EmptyGenesisChannelCatalog.Instance }, new Factory(host));
        var result = await composition.RunOwnedGenesisAsync((_, _) => surface);
        Assert.Equal(fail ? NovaWindowsStartupResult.Faulted : NovaWindowsStartupResult.Completed, result);
        Assert.True(host.Disposed);
        Assert.Equal(!fail, surface.Closed);
        Assert.True(File.Exists(profile.Paths.History));
    }

    [Fact]
    public async Task InvalidSettingsAfterPreparationRejectsBeforeCreatingNativeHost()
    {
        using var temp = new Fixture();
        await using var profile = await temp.Create();
        await File.WriteAllTextAsync(profile.Paths.Settings, "{bad");
        var host = new Host(false);
        var composition = new NovaWindowsStartupComposition(
            new() { ValidationProfile = profile }, new Factory(host));
        Assert.Equal(NovaWindowsStartupResult.Faulted,
            await composition.RunOwnedGenesisAsync((_, _) => new Surface()));
        Assert.Equal(0, host.Starts);
    }

    private static void MakeLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Base { get; } = Path.Combine(Path.GetTempPath(), "NovaValidationTests-" + Guid.NewGuid().ToString("N"));
        internal Fixture() => Directory.CreateDirectory(Base);
        internal string Root => Path.Combine(Base, "profile");
        internal string Production => Path.Combine(Base, "fake-production");
        internal Task<NovaValidationProfile> Create() => NovaValidationProfile.CreateAsync(Root, [Production], _ => null);
        public void Dispose()
        {
            var full = Path.GetFullPath(Base);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid test cleanup root.");
            Directory.Delete(full, recursive: true);
        }
    }

    private sealed class Factory(Host host) : INovaWindowsStartupHostFactory
    {
        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options) => host;
    }

    private sealed class Host(bool fail) : INovaWindowsStartupHost, IGenesisMainWindowDispatcher
    {
        public long WindowToken => 1;
        public int Starts { get; private set; }
        public bool Disposed { get; private set; }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(CancellationToken token)
        {
            Starts++; return ValueTask.FromResult(fail ? PlaybackWindowsPhysicalRunnerResult.Faulted : PlaybackWindowsPhysicalRunnerResult.Started);
        }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(CancellationToken token) =>
            ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(CancellationToken token) =>
            ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation, CancellationToken token) =>
            new(GenesisMainWindowDispatcherResult.Completed, await operation(WindowToken, token));
    }

    private sealed class Surface : IGenesisMainWindowSurfaceTarget, IAsyncDisposable
    {
        public bool Closed { get; private set; }
        public ValueTask<IAsyncDisposable?> OpenAsync(long window, GenesisMainViewModel model, CancellationToken token)
        {
            Assert.True(model.NetworkSettingsAdapter!.ReadSnapshot().Initialized);
            return ValueTask.FromResult<IAsyncDisposable?>(this);
        }
        public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
    }
}

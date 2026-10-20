using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextNetworkCompositionTests
{
    [Fact]
    public async Task ProductionRootInitializesOneOwnerBeforeBorrowingAndJoinsCleanupAfterViewsClose()
    {
        await using var test = new Fixture();
        test.Host.Wait = () =>
        {
            var adapter = Assert.IsType<GuiNextNetworkSettingsAdapter>(test.Surface.ViewModel!.NetworkSettingsAdapter);
            var first = adapter.ReadSnapshot();
            Assert.True(first.Initialized);
            Assert.False(first.Initializing);
            Assert.Equal(NovaApplicationPublicListenerAvailability.Unavailable, first.PublicListenerAvailability);
            Assert.Equal(NovaPublicRelayState.Stopped, first.PublicRelay!.State);
            var reads = test.Network.Reads;
            for (var index = 0; index < 4; index++) Assert.Equal(first.Revision, adapter.ReadSnapshot().Revision);
            Assert.Equal(reads, test.Network.Reads);
            Assert.Equal(0, test.Router.Count);
            Assert.Equal(0, test.DriverCreates);
            Assert.Equal(1, test.Owner.Initializations);
            Assert.Equal(1, test.RuntimeCreates);
            Assert.Equal(1, test.ControlCreates);
            return Task.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        };

        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
        Assert.Equal(1, test.OwnerCreates);
        Assert.Equal(1, test.Owner.Disposals);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control!.Disposals);
        Assert.Equal(["HostStart", "SurfaceOpen", "HostWait", "SurfaceClose", "HostStop", "HostDispose",
            "NetworkDispose", "PlaybackDispose"], test.Events);
        Assert.True(test.RealOwner.ReadSnapshot().Disposed);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(true, "off", false)]
    [InlineData(false, "on", true)]
    [InlineData(true, "  OFF ", false)]
    public async Task StartupPolicyRespectsSavedAndEnvironmentWithoutPublicDemand(bool saved, string? environment, bool effective)
    {
        await using var test = new Fixture { EnvironmentValue = environment };
        await test.Store.SaveAsync(new NovaOptions { AutoPortMapping = saved });
        test.Host.Wait = () =>
        {
            var snapshot = test.Surface.ViewModel!.NetworkSettingsAdapter!.ReadSnapshot();
            Assert.Equal(saved, snapshot.SavedAutoPortMapping);
            Assert.Equal(effective, snapshot.RuntimeDesiredEnabled);
            Assert.Equal(effective, snapshot.NextStartEffectiveEnabled);
            Assert.Equal(environment is not null, snapshot.StartupEnvironmentOverride);
            Assert.Empty(snapshot.Nat!.RegisteredInternalPorts);
            Assert.Equal(NovaApplicationPublicListenerAvailability.Unavailable, snapshot.PublicListenerAvailability);
            Assert.Equal(0, test.Router.Count);
            Assert.Equal(0, test.DriverCreates);
            return Task.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        };
        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
        Assert.Equal(saved, test.Store.Load().AutoPortMapping);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task BorrowerHasOnlyReadAndTypedWriteAndCannotInitializeOrDisposeOwner()
    {
        await using var test = new Fixture();
        var adapter = new GuiNextNetworkSettingsAdapter(test.Owner);
        Assert.False(adapter.ReadSnapshot().Initialized);
        Assert.Null(adapter.ReadSnapshot().Nat);
        Assert.Equal(NovaApplicationNetworkFailure.NotInitialized,
            (await adapter.SetAutoPortMappingAsync(true)).Failure);
        Assert.Equal(0, test.Owner.Initializations);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.Equal(0, test.Network.Reads);
        Assert.Equal(["ReadSnapshot", "SetAutoPortMappingAsync"],
            typeof(IGuiNextNetworkSettingsAdapter).GetMethods().Select(method => method.Name).Order().ToArray());
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(GuiNextNetworkSettingsAdapter)));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(typeof(GuiNextNetworkSettingsAdapter)));
    }

    [Fact]
    public async Task MultipleViewsBorrowSameOwnerAndClosingChildDoesNotStopApplication()
    {
        await using var test = new Fixture();
        test.Host.Wait = async () =>
        {
            var adapter = test.Surface.ViewModel!.NetworkSettingsAdapter!;
            var child = new GenesisMainViewModel(new FakePlaybackSessionController(), false, adapter);
            Assert.Same(adapter, child.NetworkSettingsAdapter);
            var another = new GuiNextNetworkSettingsAdapter(test.Owner);
            var before = another.ReadSnapshot();
            await test.Surface.Lifetime.DisposeAsync();
            Assert.Equal(0, test.Owner.Disposals);
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved,
                (await child.NetworkSettingsAdapter!.SetAutoPortMappingAsync(true)).Status);
            var after = another.ReadSnapshot();
            Assert.True(after.Revision > before.Revision);
            Assert.Equal(before.PublicRelay, after.PublicRelay);
            Assert.Equal(before.PublicRelay!.RuntimeOwnerId, after.PublicRelay!.RuntimeOwnerId);
            Assert.True(after.RuntimeDesiredEnabled);
            Assert.Equal(1, test.RuntimeCreates);
            Assert.Equal(0, test.DriverCreates);
            return PlaybackWindowsPhysicalRunnerResult.Stopped;
        };
        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
        Assert.Equal(1, test.Owner.Disposals);
        Assert.Equal(NovaApplicationNetworkFailure.Closing,
            (await test.Surface.ViewModel!.NetworkSettingsAdapter!.SetAutoPortMappingAsync(false)).Failure);
    }

    [Fact]
    public async Task AdapterUsesFieldOnlySaveAndPreservesLatestOtherSettings()
    {
        await using var test = new Fixture();
        await test.Store.SaveAsync(new NovaOptions { RecordingOutputDirectory = test.Root, NodeName = "before" });
        test.Host.Wait = async () =>
        {
            await test.Store.UpdateAsync(current => current with
            {
                RecordingOutputDirectory = Path.Combine(test.Root, "next"),
                NodeName = "latest",
                ListenPort = 8123,
                EnableDetailedDiagnostics = true,
            });
            var outcome = await test.Surface.ViewModel!.NetworkSettingsAdapter!.SetAutoPortMappingAsync(true);
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, outcome.Status);
            Assert.Equal(1, test.Owner.Writes);
            var saved = test.Store.Load();
            Assert.True(saved.AutoPortMapping);
            Assert.Equal("latest", saved.NodeName);
            Assert.Equal(8123, saved.ListenPort);
            Assert.True(saved.EnableDetailedDiagnostics);
            Assert.Equal(Path.Combine(test.Root, "next"), saved.RecordingOutputDirectory);
            Assert.Equal(0, test.Router.Count);
            return PlaybackWindowsPhysicalRunnerResult.Stopped;
        };
        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
    }

    [Fact]
    public async Task PersistenceFailureRemainsTypedAndDoesNotRollbackDesired()
    {
        await using var test = new Fixture();
        test.Host.Wait = async () =>
        {
            await File.WriteAllTextAsync(test.SettingsPath, "private-corrupt-settings");
            var adapter = test.Surface.ViewModel!.NetworkSettingsAdapter!;
            var result = await adapter.SetAutoPortMappingAsync(true);
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Failed, result.Status);
            Assert.Equal(NovaApplicationNetworkFailure.PersistenceFailed, result.Failure);
            var snapshot = adapter.ReadSnapshot();
            Assert.True(snapshot.RuntimeDesiredEnabled);
            Assert.False(snapshot.SavedAutoPortMapping);
            Assert.Equal(result, snapshot.LastPersistenceResult);
            Assert.Equal(NovaApplicationNetworkFailure.PersistenceFailed, snapshot.SafeFailure);
            Assert.DoesNotContain("private-corrupt-settings", result.ToString(), StringComparison.Ordinal);
            return PlaybackWindowsPhysicalRunnerResult.Stopped;
        };
        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
    }

    [Fact]
    public async Task ShutdownRejectsNewWritesAndDrainsAcceptedSaveBeforeRuntimeDisposal()
    {
        await using var test = new Fixture { HoldSaves = true };
        Task<NovaApplicationNetworkPersistenceResult>? save = null;
        test.Host.Wait = async () =>
        {
            save = test.Surface.ViewModel!.NetworkSettingsAdapter!.SetAutoPortMappingAsync(true);
            await Await(test.Control!.Persisted.Task);
            Assert.Equal(1, test.RealOwner.ReadSnapshot().PendingSaveCount);
            return PlaybackWindowsPhysicalRunnerResult.Stopped;
        };
        var run = test.RunAsync();
        try
        {
            await Await(test.Owner.DisposalEntered.Task);
            Assert.False(run.IsCompleted);
            Assert.Equal(0, test.Runtime!.Disposals);
            Assert.Equal(0, test.Control!.Disposals);
            Assert.Equal(NovaApplicationNetworkFailure.Closing,
                (await test.Surface.ViewModel!.NetworkSettingsAdapter!.SetAutoPortMappingAsync(false)).Failure);
        }
        finally { test.Control?.Release.TrySetResult(); }
        Assert.Equal(NovaWindowsStartupResult.Completed, await Await(run));
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await save!).Status);
        Assert.Equal(0, test.RealOwner.ReadSnapshot().PendingSaveCount);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(1, test.Control!.Disposals);
    }

    [Fact]
    public async Task CancellationDuringInitializationJoinsPartialRuntimeCleanup()
    {
        await using var test = new Fixture { HoldRuntimeFactory = true };
        using var cancellation = new CancellationTokenSource();
        var run = test.RunAsync(cancellation.Token);
        try
        {
            await Await(test.RuntimeFactoryEntered.Task);
            await cancellation.CancelAsync();
            await Await(test.Owner.DisposalEntered.Task);
            Assert.False(run.IsCompleted);
            Assert.Null(test.Surface.ViewModel);
        }
        finally { test.RuntimeFactoryRelease.TrySetResult(); }
        Assert.Equal(NovaWindowsStartupResult.Cancelled, await Await(run));
        Assert.Equal(1, test.Owner.Disposals);
        Assert.Equal(1, test.Runtime!.Disposals);
        Assert.Equal(0, test.DriverCreates);
        Assert.True(test.RealOwner.ReadSnapshot().Disposed);
    }

    [Theory]
    [InlineData("invalid", false)]
    [InlineData(null, true)]
    public async Task InitializationFailureIsBoundedAndClosesHostWithoutOpeningView(
        string? environment, bool failRuntime)
    {
        await using var test = new Fixture { EnvironmentValue = environment, FailRuntime = failRuntime };
        Assert.Equal(NovaWindowsStartupResult.Faulted, await test.RunAsync());
        Assert.Null(test.Surface.ViewModel);
        Assert.Equal(failRuntime ? NovaApplicationNetworkFailure.RuntimeInitializationFailed : NovaApplicationNetworkFailure.InvalidEnvironment, test.RealOwner.ReadSnapshot().SafeFailure);
        Assert.Equal(1, test.Owner.Disposals);
        Assert.DoesNotContain("HostWait", test.Events);
        Assert.Contains("HostDispose", test.Events);
    }

    [Theory]
    [InlineData(PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported, NovaWindowsStartupResult.PlatformUnsupported)]
    [InlineData(PlaybackWindowsPhysicalRunnerResult.Faulted, NovaWindowsStartupResult.Faulted)]
    [InlineData(PlaybackWindowsPhysicalRunnerResult.None, NovaWindowsStartupResult.StartupRejected)]
    [InlineData(PlaybackWindowsPhysicalRunnerResult.Cancelled, NovaWindowsStartupResult.Cancelled)]
    public async Task HostRejectionDoesNotInitializeNetworkButStillReleasesRoot(
        PlaybackWindowsPhysicalRunnerResult hostResult, NovaWindowsStartupResult expected)
    {
        await using var test = new Fixture();
        test.Host.StartResult = hostResult;
        Assert.Equal(expected, await test.RunAsync());
        Assert.Equal(0, test.Owner.Initializations);
        Assert.Equal(0, test.RuntimeCreates);
        Assert.Equal(1, test.Owner.Disposals);
        Assert.True(test.Playback.Disposed);
    }

    [Fact]
    public async Task UnconfirmedNatCleanupCannotBeReportedAsSuccessfulApplicationExit()
    {
        await using var test = new Fixture { UnconfirmedCleanup = true };
        Assert.Equal(NovaWindowsStartupResult.Faulted, await test.RunAsync());
        Assert.Equal(NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed,
            test.RealOwner.ReadSnapshot().SafeFailure);
        Assert.Equal(1, test.Owner.Disposals);
        Assert.True(test.Playback.Disposed);
    }

    [Fact]
    public async Task PlaybackDisposalFailureDoesNotSkipNetworkDisposal()
    {
        await using var test = new Fixture();
        test.Playback.FailDisposal = true;
        Assert.Equal(NovaWindowsStartupResult.Faulted, await test.RunAsync());
        Assert.Equal(1, test.Owner.Disposals);
        Assert.True(test.RealOwner.ReadSnapshot().Disposed);
    }

    [Fact]
    public async Task NetworkAdmissionClosesWhileIndependentPlaybackShutdownIsStillPending()
    {
        await using var test = new Fixture();
        test.Playback.HoldDisposal = true;
        var run = test.RunAsync();
        try
        {
            await Await(test.Playback.DisposalEntered.Task);
            Assert.False(run.IsCompleted);
            Assert.Equal(NovaApplicationNetworkFailure.Closing,
                (await test.Surface.ViewModel!.NetworkSettingsAdapter!.SetAutoPortMappingAsync(true)).Failure);
            Assert.Equal(1, test.Owner.Disposals);
        }
        finally { test.Playback.DisposalRelease.TrySetResult(); }
        Assert.Equal(NovaWindowsStartupResult.Completed, await Await(run));
    }

    [Fact]
    public async Task SurfaceFactoryFailureStillDisposesBothRootOwners()
    {
        await using var test = new Fixture();
        Assert.Equal(NovaWindowsStartupResult.Faulted, await test.Composition.RunOwnedGenesisAsync(
            (_, _) => throw new InvalidOperationException("private-surface-error"), recordingSettings: test.Settings));
        Assert.Equal(0, test.Owner.Initializations);
        Assert.Equal(1, test.Owner.Disposals);
        Assert.True(test.Playback.Disposed);
    }

    [Fact]
    public async Task DefaultPrivatePlaybackAdapterRemainsInertAlongsideApplicationNetworkOwner()
    {
        await using var test = new Fixture { UseProductionPlayback = true };
        test.Host.Wait = () =>
        {
            Assert.IsType<GuiNextDailyPlaybackSessionController>(test.CreatedPlayback);
            Assert.Equal(PlaybackSessionState.Stopped, test.Surface.ViewModel!.PlaybackState);
            Assert.NotNull(test.Surface.ViewModel.RecordingHistoryController);
            Assert.False(test.Surface.ViewModel.IsPlaybackWindowOpen);
            Assert.NotNull(test.Surface.ViewModel.NetworkSettingsAdapter);
            Assert.Equal(0, test.Router.Count);
            Assert.Equal(0, test.DriverCreates);
            return Task.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        };
        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
    }

    [Fact]
    public async Task SnapshotReadsDoNotCacheNatProgressBehindApplicationRevision()
    {
        await using var test = new Fixture();
        test.Host.Wait = () =>
        {
            var adapter = test.Surface.ViewModel!.NetworkSettingsAdapter!;
            var before = adapter.ReadSnapshot();
            test.Runtime!.Manager.UpdateNetwork(null);
            var after = adapter.ReadSnapshot();
            Assert.Equal(before.Revision, after.Revision);
            Assert.Equal(before.PublicRelay, after.PublicRelay);
            Assert.True(after.Nat!.Generation > before.Nat!.Generation);
            Assert.Null(after.Nat.Network);
            Assert.Equal(NatDisplayReachability.NotChecked, after.Nat.ExternalReachability);
            Assert.Equal(0, test.Router.Count);
            return Task.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        };
        Assert.Equal(NovaWindowsStartupResult.Completed, await test.RunAsync());
    }

    [Fact]
    public void NativeNetworkSettingsRemainUnavailableAndUseSeparateGenericContract()
    {
        var controller = new NovaWindowsStartupOptions().NetworkSettingsController;
        Assert.Same(GenesisUnavailableNetworkSettingsController.Instance, controller);
        Assert.Equal(GenesisRelayListenerRuntimeState.Unavailable, controller.Snapshot.RuntimeStatus.ListenerState);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task<T> Await<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Nova-network-composition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Store = new NovaSettingsStore(SettingsPath);
            Settings = new GenesisRecordingOutputDirectorySettings(Store, Path.Combine(Root, "recordings"));
            RealOwner = new NovaApplicationNetworkOwner(Store, () => EnvironmentValue,
                options =>
                {
                    RuntimeCreates++;
                    RuntimeFactoryEntered.TrySetResult();
                    if (HoldRuntimeFactory) RuntimeFactoryRelease.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                    if (FailRuntime) throw new InvalidOperationException("private-runtime-error");
                    return Runtime = new CountedRuntime(new NatPortMappingRuntime(options, Router, Network, Clock),
                        () => UnconfirmedCleanup);
                },
                (manager, store, saved, overridden) =>
                {
                    ControlCreates++;
                    return Control = new CountedControl(new NovaPortMappingControl(manager, store, saved, overridden), () => HoldSaves);
                },
                _ =>
                {
                    DriverCreates++;
                    throw new InvalidOperationException("Public relay must not be automatically acquired.");
                });
            Owner = new CountedOwner(RealOwner, Events);
            Host = new Host(Events);
            Surface = new Surface(Events);
            Playback = new Playback(Events);
            Composition = new NovaWindowsStartupComposition(new(), new HostFactory(Host),
                () =>
                {
                    CreatedPlayback = UseProductionPlayback
                        ? NovaWindowsStartupComposition.CreateDefaultGenesisPlaybackController(
                            new GenesisGuiNextPlaybackPathSource(), Settings, new GenesisRecordingHistoryStore(Path.Combine(Root, "history.json")))
                        : Playback;
                    return CreatedPlayback;
                },
                () => { OwnerCreates++; return Owner; });
        }

        public string Root { get; }
        public string SettingsPath => Path.Combine(Root, "settings.json");
        public NovaSettingsStore Store { get; }
        public GenesisRecordingOutputDirectorySettings Settings { get; }
        public NovaApplicationNetworkOwner RealOwner { get; }
        public CountedOwner Owner { get; }
        public NovaWindowsStartupComposition Composition { get; }
        public List<string> Events { get; } = [];
        public Host Host { get; }
        public Surface Surface { get; }
        public Playback Playback { get; }
        public IPlaybackSessionController? CreatedPlayback { get; private set; }
        public FakeNatRouter Router { get; } = new();
        public Network Network { get; } = new();
        public ManualNatTimeProvider Clock { get; } = new();
        public CountedRuntime? Runtime { get; private set; }
        public CountedControl? Control { get; private set; }
        public int OwnerCreates { get; private set; }
        public int RuntimeCreates { get; private set; }
        public int ControlCreates { get; private set; }
        public int DriverCreates { get; private set; }
        public string? EnvironmentValue { get; init; }
        public bool HoldRuntimeFactory { get; init; }
        public bool FailRuntime { get; init; }
        public bool HoldSaves { get; init; }
        public bool UnconfirmedCleanup { get; init; }
        public bool UseProductionPlayback { get; init; }
        public TaskCompletionSource RuntimeFactoryEntered { get; } = Signal();
        public TaskCompletionSource RuntimeFactoryRelease { get; } = Signal();
        public Task<NovaWindowsStartupResult> RunAsync(CancellationToken token = default) =>
            Composition.RunOwnedGenesisAsync((_, _) => Surface, Settings, token).AsTask();
        public async ValueTask DisposeAsync()
        {
            RuntimeFactoryRelease.TrySetResult();
            Control?.Release.TrySetResult();
            if (!RealOwner.ReadSnapshot().Disposed) await RealOwner.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class CountedOwner(NovaApplicationNetworkOwner owner, List<string> events) : INovaApplicationNetworkOwner
    {
        public int Initializations { get; private set; }
        public int Disposals { get; private set; }
        public int Writes { get; private set; }
        public TaskCompletionSource DisposalEntered { get; } = Signal();
        public Task<NovaApplicationNetworkInitializationResult> InitializeAsync() { Initializations++; return owner.InitializeAsync(); }
        public NovaApplicationNetworkSnapshot ReadSnapshot() => owner.ReadSnapshot();
        public Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled) { Writes++; return owner.SetAutoPortMappingAsync(enabled); }
        public Task<NovaPublicRelayAcquireResult> AcquirePublicRelayAsync(NovaPublicRelayRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not a composition operation.");
        public ValueTask DisposeAsync()
        {
            Disposals++;
            events.Add("NetworkDispose");
            var disposal = owner.DisposeAsync();
            DisposalEntered.TrySetResult();
            return disposal;
        }
    }

    private sealed class CountedRuntime(NatPortMappingRuntime runtime, Func<bool> unconfirmed) : INovaApplicationNatRuntime
    {
        public NatPortMappingManager Manager => runtime.Manager;
        public NatShutdownOutcome? ShutdownOutcome => runtime.ShutdownOutcome is null ? null :
            unconfirmed() ? NatShutdownOutcome.CleanupUnconfirmed : runtime.ShutdownOutcome;
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { Disposals++; return runtime.DisposeAsync(); }
    }

    private sealed class CountedControl(NovaPortMappingControl control, Func<bool> hold) : INovaApplicationPortMappingControl
    {
        public TaskCompletionSource Persisted { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public int Disposals { get; private set; }
        public async Task SetEnabledAsync(bool enabled)
        {
            try { await control.SetEnabledAsync(enabled); }
            finally { Persisted.TrySetResult(); }
            if (hold()) await Release.Task;
        }
        public void Dispose() { Disposals++; control.Dispose(); }
    }

    private sealed class Network : INatNetworkSource
    {
        public int Reads { get; private set; }
        public NatNetworkIdentity? GetCurrentNetwork() { Reads++; return NatPortMappingTestHarness.Home; }
    }

    private sealed class HostFactory(Host host) : INovaWindowsStartupHostFactory
    {
        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options) => host;
    }

    private sealed class Host(List<string> events) : INovaWindowsStartupHost, IGenesisMainWindowDispatcher
    {
        public long WindowToken => 7144;
        public PlaybackWindowsPhysicalRunnerResult StartResult { get; set; } = PlaybackWindowsPhysicalRunnerResult.Started;
        public Func<Task<PlaybackWindowsPhysicalRunnerResult>> Wait { get; set; } = () => Task.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(CancellationToken cancellationToken)
        {
            events.Add("HostStart");
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(StartResult);
        }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(CancellationToken cancellationToken)
        {
            events.Add("HostWait");
            return new(Wait());
        }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(CancellationToken cancellationToken)
        {
            events.Add("HostStop");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }
        public ValueTask DisposeAsync() { events.Add("HostDispose"); return ValueTask.CompletedTask; }
        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation, CancellationToken cancellationToken) =>
            new(GenesisMainWindowDispatcherResult.Completed, await operation(WindowToken, cancellationToken));
    }

    private sealed class Surface(List<string> events) : IGenesisMainWindowSurfaceTarget
    {
        public GenesisMainViewModel? ViewModel { get; private set; }
        public Lifetime Lifetime { get; } = new(() => events.Add("SurfaceClose"));
        public ValueTask<IAsyncDisposable?> OpenAsync(long windowToken, GenesisMainViewModel viewModel, CancellationToken cancellationToken)
        {
            events.Add("SurfaceOpen");
            ViewModel = viewModel;
            Assert.True(viewModel.NetworkSettingsAdapter!.ReadSnapshot().Initialized);
            return ValueTask.FromResult<IAsyncDisposable?>(Lifetime);
        }
    }

    private sealed class Lifetime(Action close) : IAsyncDisposable
    {
        private bool closed;
        public ValueTask DisposeAsync()
        {
            if (!closed) { closed = true; close(); }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Playback(List<string> events) : IPlaybackSessionController, IAsyncDisposable
    {
        public PlaybackSessionSnapshot Snapshot => PlaybackSessionSnapshot.Stopped;
        public bool Disposed { get; private set; }
        public bool FailDisposal { get; set; }
        public bool HoldDisposal { get; set; }
        public TaskCompletionSource DisposalEntered { get; } = Signal();
        public TaskCompletionSource DisposalRelease { get; } = Signal();
        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(string source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No physical playback.");
        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(PlaybackSessionCommandResult.Completed, Snapshot));
        public async ValueTask DisposeAsync()
        {
            events.Add("PlaybackDispose");
            DisposalEntered.TrySetResult();
            if (HoldDisposal) await DisposalRelease.Task;
            Disposed = true;
            if (FailDisposal) throw new InvalidOperationException("private-playback-cleanup-error");
        }
    }
}

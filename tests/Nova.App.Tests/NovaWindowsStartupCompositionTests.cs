using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaWindowsStartupCompositionTests
{
    [Fact]
    public async Task RunUsesOneSharedHostAndCleansUpAfterNativeClose()
    {
        var events = new List<string>();
        var host = new FakeHost(events);
        var factory = new FakeFactory(host, events);
        var composition = new NovaWindowsStartupComposition(new(), factory);

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(["Create", "Start", "Wait", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task StartupFailureStillStopsAndDisposesPartialHost()
    {
        var events = new List<string>();
        var host = new FakeHost(
            events,
            startResult: PlaybackWindowsPhysicalRunnerResult.Faulted);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Faulted, result);
        Assert.Equal(["Create", "Start", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task CancellationEndsRunAndPerformsFiniteShutdown()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var host = new FakeHost(
            events,
            wait: async token =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return PlaybackWindowsPhysicalRunnerResult.Stopped;
            });
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync(cancellation.Token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NovaWindowsStartupResult.Cancelled, result);
        Assert.Equal(["Create", "Start", "Wait", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task StartedHostStopFaultOverridesCancelledExit()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var host = new FakeHost(
            events,
            wait: _ =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Cancelled);
            },
            stopResult: PlaybackWindowsPhysicalRunnerResult.Faulted);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync(cancellation.Token);

        Assert.Equal(NovaWindowsStartupResult.Faulted, result);
        Assert.Equal(["Create", "Start", "Wait", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task UnsupportedPlatformFailsClosedWithoutRunningApplicationBody()
    {
        var events = new List<string>();
        var host = new FakeHost(
            events,
            startResult: PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.PlatformUnsupported, result);
        Assert.Equal(0, host.WaitCount);
        Assert.Equal(["Create", "Start", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task DefaultGenesisPlaybackControllerIsDailyAdapterAndInertUntilPlay()
    {
        using var fixture = new RecordingMetadataFixture();
        await using var controller = Assert.IsType<GuiNextDailyPlaybackSessionController>(
            NovaWindowsStartupComposition.CreateDefaultGenesisPlaybackController(
                fixture.Store, fixture.Settings));

        Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
        Assert.IsAssignableFrom<IGenesisManualRecordingSessionController>(controller);
        var recording = controller.GetSnapshot(new GenesisChannelItem(
            "selected station",
            "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87",
            ChannelId: "531DC8DFC7FB42928AC2C0A626517A87"));
        Assert.True(recording.IsCandidateValid);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle, recording.State);
    }

    [Fact]
    public async Task SavedPlayerModeIsReadForEachNewPlay()
    {
        using var fixture = new RecordingMetadataFixture();
        var pathSource = new GenesisGuiNextPlaybackPreferencesSource();
        await using var controller = NovaWindowsStartupComposition
            .CreateDefaultGenesisPlaybackController(pathSource,
                fixture.Settings, fixture.Store);

        Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);

        var missingDirectory = Path.Combine(Path.GetTempPath(), $"NovaMissing-{Guid.NewGuid():N}");
        var preferences = new GenesisGuiNextPlaybackPreferences(
            Path.Combine(missingDirectory, "PCRPlayer64.exe"),
            GenesisPlaybackPlayerMode.Internal,
            Path.Combine(missingDirectory, "libmpv-2.dll"));
        var request = new PlaybackSessionRequest(
            "pcp://127.0.0.1:7144/channel/0123456789ABCDEF0123456789ABCDEF",
            "Settings path composition",
            "0123456789ABCDEF0123456789ABCDEF");

        pathSource.Set(preferences);
        var internalResult = await controller.PlayAsync(request);
        Assert.Equal(PlaybackSessionCommandResult.Faulted, internalResult.Result);
        Assert.Contains("libmpv DLL", internalResult.Snapshot.ErrorMessage, StringComparison.Ordinal);

        pathSource.Set(preferences with { PlayerMode = GenesisPlaybackPlayerMode.External });
        var externalResult = await controller.PlayAsync(request);
        Assert.Equal(PlaybackSessionCommandResult.Faulted, externalResult.Result);
        Assert.Contains("--pcrplayer-executable", externalResult.Snapshot.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitStartupPlayerOverridesTheSavedChoice(bool useInternal)
    {
        using var fixture = new RecordingMetadataFixture();
        var pathSource = new GenesisGuiNextPlaybackPreferencesSource();
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"NovaMissing-{Guid.NewGuid():N}");
        var executablePath = Path.Combine(missingDirectory, "PCRPlayer64.exe");
        var libraryPath = Path.Combine(missingDirectory, "libmpv-2.dll");
        pathSource.Set(new(executablePath,
            useInternal ? GenesisPlaybackPlayerMode.External : GenesisPlaybackPlayerMode.Internal, libraryPath));
        await using var controller = NovaWindowsStartupComposition.CreateDefaultGenesisPlaybackController(
            pathSource, fixture.Settings, fixture.Store,
            explicitPlayerPath: useInternal ? null : executablePath,
            libMpvLibraryPath: useInternal ? libraryPath : null);

        var result = await controller.PlayAsync(new PlaybackSessionRequest(
            "pcp://127.0.0.1:7144/channel/0123456789ABCDEF0123456789ABCDEF",
            "Explicit startup player",
            "0123456789ABCDEF0123456789ABCDEF"));

        Assert.Equal(PlaybackSessionCommandResult.Faulted, result.Result);
        Assert.Contains(useInternal ? "libmpv DLL" : "--pcrplayer-executable",
            result.Snapshot.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenesisStartupOwnsAndDisposesItsPlaybackController()
    {
        var events = new List<string>();
        var host = new FakeHost(
            events,
            startResult: PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported);
        var playbackController = new DisposableFakePlaybackController(events);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events),
            () => playbackController);

        var result = await composition.RunGenesisAsync();

        Assert.Equal(NovaWindowsStartupResult.PlatformUnsupported, result);
        Assert.True(playbackController.Disposed);
        Assert.Equal(
            ["Create", "Start", "Stop", "Dispose", "DisposePlayback"],
            events);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShutdownCleanupFailuresStayWithinBoundedResult(
        bool stopThrows,
        bool disposeThrows)
    {
        var events = new List<string>();
        var host = new FakeHost(
            events,
            stopThrows: stopThrows,
            disposeThrows: disposeThrows);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Faulted, result);
        Assert.Equal(["Create", "Start", "Wait", "Stop", "Dispose"], events);
    }

    [Fact]
    public void DefaultApplicationGraphHasNoPhysicalCommandOrFfmpegBinaryReference()
    {
        var project = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        var program = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "Program.cs"));

        Assert.Contains(@"..\Nova.Windows\Nova.Windows.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova.Cli", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova.Playback", project, StringComparison.Ordinal);
        Assert.DoesNotContain("FFmpeg\\**\\*", project, StringComparison.Ordinal);
        Assert.DoesNotContain(".dll", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".exe", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".zip", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Physical", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Readiness", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Probe", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostic", program, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }

    private sealed class FakeFactory : INovaWindowsStartupHostFactory
    {
        private readonly FakeHost _host;
        private readonly List<string> _events;

        public FakeFactory(FakeHost host, List<string> events)
        {
            _host = host;
            _events = events;
        }

        public int CreateCount { get; private set; }

        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
        {
            Assert.Equal("PeerCast Nova", options.Title);
            CreateCount++;
            _events.Add("Create");
            return _host;
        }
    }

    private sealed class FakeHost : INovaWindowsStartupHost
    {
        private readonly List<string> _events;
        private readonly PlaybackWindowsPhysicalRunnerResult _startResult;
        private readonly Func<CancellationToken, ValueTask<PlaybackWindowsPhysicalRunnerResult>>
            _wait;
        private readonly PlaybackWindowsPhysicalRunnerResult _stopResult;
        private readonly bool _stopThrows;
        private readonly bool _disposeThrows;

        public FakeHost(
            List<string> events,
            PlaybackWindowsPhysicalRunnerResult startResult =
                PlaybackWindowsPhysicalRunnerResult.Started,
            Func<CancellationToken, ValueTask<PlaybackWindowsPhysicalRunnerResult>>? wait = null,
            PlaybackWindowsPhysicalRunnerResult stopResult =
                PlaybackWindowsPhysicalRunnerResult.Stopped,
            bool stopThrows = false,
            bool disposeThrows = false)
        {
            _events = events;
            _startResult = startResult;
            _wait = wait ?? (_ => ValueTask.FromResult(
                PlaybackWindowsPhysicalRunnerResult.Stopped));
            _stopResult = stopResult;
            _stopThrows = stopThrows;
            _disposeThrows = disposeThrows;
        }

        public int WaitCount { get; private set; }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Start");
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_startResult);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Wait");
            WaitCount++;
            return _wait(cancellationToken);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Stop");
            if (_stopThrows)
            {
                throw new InvalidOperationException("Host stop failed.");
            }

            return ValueTask.FromResult(_stopResult);
        }

        public ValueTask DisposeAsync()
        {
            _events.Add("Dispose");
            if (_disposeThrows)
            {
                throw new InvalidOperationException("Host disposal failed.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingMetadataFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Nova-startup-metadata-tests", Guid.NewGuid().ToString("N"));
        public GenesisRecordingHistoryStore Store => new(Path.Combine(_root, "history.json"));
        public GenesisRecordingOutputDirectorySettings Settings => new(new(Path.Combine(_root, "settings.json")), Path.Combine(_root, "recordings"));
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }

    private sealed class DisposableFakePlaybackController(List<string> events) :
        IPlaybackSessionController,
        IAsyncDisposable
    {
        public PlaybackSessionSnapshot Snapshot { get; } = PlaybackSessionSnapshot.Stopped;

        public bool Disposed { get; private set; }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Rejected,
                Snapshot));

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            events.Add("DisposePlayback");
            return ValueTask.CompletedTask;
        }
    }
}

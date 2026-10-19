using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackStartupCompositionTests
{
    [Fact]
    public async Task PreplayingGenesisStateProjectsPlaybackInsideSharedHostLifetime()
    {
        var events = new List<string>();
        var host = new RecordingHost(events);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new RecordingHostFactory(host, events));
        var mainSurface = new PreplayingMainSurface(events);
        var playbackSurface = new RecordingPlaybackSurface(events);

        var result = await composition.RunGenesisAsync(
            mainSurface,
            playbackSurface,
            new FakePlaybackSessionController());

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(
            [
                "Create",
                "Start",
                "DispatchGenesis",
                "OpenGenesis",
                "DispatchGenesis",
                "OpenPlayback",
                "Wait",
                "ClosePlayback",
                "CloseGenesis",
                "Stop",
                "Dispose",
            ],
            events);
        Assert.Equal(7144, playbackSurface.LastOwnerWindowToken);
        Assert.NotNull(playbackSurface.LastState);
        Assert.True(playbackSurface.LastState.IsOpen);
        Assert.Equal("pcp://example", playbackSurface.LastState.Source);
        Assert.Equal(GenesisPlaybackWindowMode.Normal, playbackSurface.LastState.Mode);
    }

    [Fact]
    public async Task StartupWithoutPlaybackSurfaceKeepsPlayAndShellStopWithoutCoordinator()
    {
        var events = new List<string>();
        var host = new RecordingHost(events);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new RecordingHostFactory(host, events));
        var mainSurface = new PreplayingMainSurface(events, stopPlaybackAfterStart: true);

        var result = await composition.RunGenesisAsync(
            mainSurface,
            new FakePlaybackSessionController());

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(
            [
                "Create",
                "Start",
                "DispatchGenesis",
                "OpenGenesis",
                "ShellStop",
                "Wait",
                "CloseGenesis",
                "Stop",
                "Dispose",
            ],
            events);
        Assert.Equal(PlaybackSessionState.Playing, mainSurface.PlaybackStateAfterPlay);
        Assert.False(mainSurface.PlaybackWindowOpenAfterPlay);
        Assert.Equal(PlaybackSessionState.Stopped, mainSurface.PlaybackStateAfterStop);
        Assert.False(mainSurface.PlaybackWindowOpenAfterStop);
    }

    private sealed class RecordingHostFactory : INovaWindowsStartupHostFactory
    {
        private readonly RecordingHost _host;
        private readonly List<string> _events;

        public RecordingHostFactory(RecordingHost host, List<string> events)
        {
            _host = host;
            _events = events;
        }

        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
        {
            _events.Add("Create");
            return _host;
        }
    }

    private sealed class RecordingHost :
        INovaWindowsStartupHost,
        IGenesisMainWindowDispatcher
    {
        private readonly List<string> _events;

        public RecordingHost(List<string> events)
        {
            _events = events;
        }

        public long WindowToken => 7144;

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Start");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Started);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Wait");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Stop");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
            CancellationToken cancellationToken)
        {
            _events.Add("DispatchGenesis");
            var lifetime = await operation(WindowToken, cancellationToken)
                .ConfigureAwait(false);
            return new(GenesisMainWindowDispatcherResult.Completed, lifetime);
        }

        public ValueTask DisposeAsync()
        {
            _events.Add("Dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PreplayingMainSurface : IGenesisMainWindowSurfaceTarget
    {
        private readonly List<string> _events;
        private readonly bool _stopPlaybackAfterStart;

        public PreplayingMainSurface(
            List<string> events,
            bool stopPlaybackAfterStart = false)
        {
            _events = events;
            _stopPlaybackAfterStart = stopPlaybackAfterStart;
        }

        public PlaybackSessionState? PlaybackStateAfterPlay { get; private set; }

        public bool PlaybackWindowOpenAfterPlay { get; private set; }

        public PlaybackSessionState? PlaybackStateAfterStop { get; private set; }

        public bool PlaybackWindowOpenAfterStop { get; private set; }

        public async ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            _events.Add("OpenGenesis");
            viewModel.SourceText = "pcp://example";
            await viewModel.PlayAsync(cancellationToken).ConfigureAwait(false);
            PlaybackStateAfterPlay = viewModel.PlaybackState;
            PlaybackWindowOpenAfterPlay = viewModel.IsPlaybackWindowOpen;

            if (_stopPlaybackAfterStart)
            {
                _events.Add("ShellStop");
                await GenesisGuiNextPlaybackCommandDispatch.StopAsync(
                        viewModel,
                        new GenesisMainWindowInteraction(viewModel),
                        cancellationToken)
                    .ConfigureAwait(false);
                PlaybackStateAfterStop = viewModel.PlaybackState;
                PlaybackWindowOpenAfterStop = viewModel.IsPlaybackWindowOpen;
            }

            return new RecordingLifetime(_events, "CloseGenesis");
        }
    }

    private sealed class RecordingPlaybackSurface : IGenesisPlaybackWindowSurfaceTarget
    {
        private readonly List<string> _events;

        public RecordingPlaybackSurface(List<string> events)
        {
            _events = events;
        }

        public long LastOwnerWindowToken { get; private set; }

        public GenesisPlaybackWindowState? LastState { get; private set; }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            _events.Add("OpenPlayback");
            LastOwnerWindowToken = ownerWindowToken;
            LastState = state;
            return ValueTask.FromResult<IAsyncDisposable?>(
                new RecordingLifetime(_events, "ClosePlayback"));
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        private readonly List<string> _events;
        private readonly string _closeEvent;

        public RecordingLifetime(List<string> events, string closeEvent)
        {
            _events = events;
            _closeEvent = closeEvent;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add(_closeEvent);
            return ValueTask.CompletedTask;
        }
    }
}

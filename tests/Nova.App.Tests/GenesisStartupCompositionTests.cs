using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisStartupCompositionTests
{
    [Fact]
    public async Task GenesisRunProjectsSurfaceInsideSharedHostLifetime()
    {
        var events = new List<string>();
        var host = new GenesisHost(events);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new GenesisHostFactory(host, events));
        var surface = new GenesisSurfaceTarget(events);

        var result = await composition.RunGenesisAsync(
            surface,
            new FakePlaybackSessionController());

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(
            [
                "Create",
                "Start",
                "DispatchGenesis",
                "OpenGenesis",
                "Wait",
                "CloseGenesis",
                "Stop",
                "Dispose",
            ],
            events);
        Assert.Equal(7144, surface.LastWindowToken);
        Assert.NotNull(surface.LastViewModel);
        Assert.Equal(PlaybackSessionState.Stopped, surface.LastViewModel.PlaybackState);
    }

    [Fact]
    public async Task GenesisProjectionRejectionFailsClosedBeforeWait()
    {
        var events = new List<string>();
        var host = new GenesisHost(events);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new GenesisHostFactory(host, events));

        var result = await composition.RunGenesisAsync(
            new GenesisSurfaceTarget(events, reject: true),
            new FakePlaybackSessionController());

        Assert.Equal(NovaWindowsStartupResult.Faulted, result);
        Assert.DoesNotContain("Wait", events);
        Assert.Equal(
            [
                "Create",
                "Start",
                "DispatchGenesis",
                "OpenGenesis",
                "Stop",
                "Dispose",
            ],
            events);
    }

    [Fact]
    public async Task StartupDefersInitialShowOnlyWhenGenesisSurfaceIsProjected()
    {
        var genesisEvents = new List<string>();
        var genesisFactory = new GenesisHostFactory(new GenesisHost(genesisEvents), genesisEvents);
        var genesisComposition = new NovaWindowsStartupComposition(new(), genesisFactory);

        Assert.Equal(
            NovaWindowsStartupResult.Completed,
            await genesisComposition.RunGenesisAsync(new GenesisSurfaceTarget(genesisEvents), new FakePlaybackSessionController()));
        Assert.True(genesisFactory.LastOptions?.DeferInitialShow);

        var headlessEvents = new List<string>();
        var headlessFactory = new GenesisHostFactory(new GenesisHost(headlessEvents), headlessEvents);
        var headlessComposition = new NovaWindowsStartupComposition(new(), headlessFactory);

        Assert.Equal(NovaWindowsStartupResult.Completed, await headlessComposition.RunAsync());
        Assert.False(headlessFactory.LastOptions?.DeferInitialShow ?? true);
    }

    [Fact]
    public void RevealWindowZeroTokenIsSafeWithoutWindowsHost()
    {
        GenesisMainWindowResizeGuard.RevealWindow(0);
    }

    private sealed class GenesisHostFactory : INovaWindowsStartupHostFactory
    {
        private readonly GenesisHost _host;
        private readonly List<string> _events;

        public GenesisHostFactory(GenesisHost host, List<string> events)
        {
            _host = host;
            _events = events;
        }

        public PlaybackWindowsPhysicalRunnerOptions? LastOptions { get; private set; }

        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
        {
            Assert.Equal("PeerCast Nova", options.Title);
            Assert.Equal(1_120, options.ClientWidth);
            Assert.Equal(620, options.ClientHeight);
            LastOptions = options;
            _events.Add("Create");
            return _host;
        }
    }

    private sealed class GenesisHost :
        INovaWindowsStartupHost,
        IGenesisMainWindowDispatcher
    {
        private readonly List<string> _events;

        public GenesisHost(List<string> events)
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
            return lifetime is null
                ? new(GenesisMainWindowDispatcherResult.Completed, null)
                : new(GenesisMainWindowDispatcherResult.Completed, lifetime);
        }

        public ValueTask DisposeAsync()
        {
            _events.Add("Dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GenesisSurfaceTarget : IGenesisMainWindowSurfaceTarget
    {
        private readonly List<string> _events;
        private readonly bool _reject;

        public GenesisSurfaceTarget(List<string> events, bool reject = false)
        {
            _events = events;
            _reject = reject;
        }

        public long LastWindowToken { get; private set; }

        public GenesisMainViewModel? LastViewModel { get; private set; }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            _events.Add("OpenGenesis");
            LastWindowToken = windowToken;
            LastViewModel = viewModel;
            return ValueTask.FromResult<IAsyncDisposable?>(
                _reject ? null : new GenesisSurfaceLifetime(_events));
        }
    }

    private sealed class GenesisSurfaceLifetime : IAsyncDisposable
    {
        private readonly List<string> _events;

        public GenesisSurfaceLifetime(List<string> events)
        {
            _events = events;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add("CloseGenesis");
            return ValueTask.CompletedTask;
        }
    }
}

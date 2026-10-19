namespace PeerCast.Nova.App.Tests;

public sealed class SystemGenesisPlaybackWindowSurfaceTargetTests
{
    [Fact]
    public async Task SupportedOpenStateCreatesOneNativeSurface()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisPlaybackWindowSurfaceTarget(factory, static () => true);
        var state = GenesisPlaybackWindowState.Open("pcp://example");

        var lifetime = await target.OpenAsync(7144, state, CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(7144, factory.LastOwnerWindowToken);
        Assert.Same(state, factory.LastState);
        await lifetime.DisposeAsync();
        Assert.True(factory.Lifetime.Disposed);
    }

    [Fact]
    public async Task UnsupportedPlatformRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisPlaybackWindowSurfaceTarget(factory, static () => false);

        var lifetime = await target.OpenAsync(
            7144,
            GenesisPlaybackWindowState.Open("pcp://example"),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task ClosedStateRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisPlaybackWindowSurfaceTarget(factory, static () => true);

        var lifetime = await target.OpenAsync(
            7144,
            GenesisPlaybackWindowState.Closed,
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task ZeroOwnerWindowRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisPlaybackWindowSurfaceTarget(factory, static () => true);

        var lifetime = await target.OpenAsync(
            0,
            GenesisPlaybackWindowState.Open("pcp://example"),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task CancellationRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisPlaybackWindowSurfaceTarget(factory, static () => true);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var lifetime = await target.OpenAsync(
            7144,
            GenesisPlaybackWindowState.Open("pcp://example"),
            cancellationSource.Token);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public void PlaybackWindowTitleDoesNotSplitUnicodeSurrogatePair()
    {
        var prefix = new string('a', 95);
        var source = $"{prefix}😀tail";

        var title = GenesisPlaybackWindowNativeSurface.BuildTitle(source);

        Assert.Equal($"PeerCast Nova - {prefix}…", title);
    }

    [Fact]
    public void FullscreenBoundsUseOwnerMonitor()
    {
        var primaryMetricsRead = false;

        var bounds = GenesisPlaybackWindowNativeSurface.ResolveFullscreenBounds(
            (nint)7144,
            owner =>
            {
                Assert.Equal((nint)7144, owner);
                return new GenesisPlaybackWindowNativeSurface.WindowBounds(
                    1920,
                    -200,
                    2560,
                    1440);
            },
            _ =>
            {
                primaryMetricsRead = true;
                return 1;
            });

        Assert.False(primaryMetricsRead);
        Assert.Equal(
            new GenesisPlaybackWindowNativeSurface.WindowBounds(
                1920,
                -200,
                2560,
                1440),
            bounds);
    }

    [Fact]
    public void FullscreenBoundsFallBackToPrimaryMetrics()
    {
        var bounds = GenesisPlaybackWindowNativeSurface.ResolveFullscreenBounds(
            (nint)7144,
            static _ => null,
            metric => metric switch
            {
                0 => 1920,
                1 => 1080,
                _ => 0,
            });

        Assert.Equal(
            new GenesisPlaybackWindowNativeSurface.WindowBounds(
                0,
                0,
                1920,
                1080),
            bounds);
    }

    private sealed class RecordingFactory : IGenesisPlaybackWindowNativeSurfaceFactory
    {
        public RecordingLifetime Lifetime { get; } = new();

        public int CreateCount { get; private set; }

        public long LastOwnerWindowToken { get; private set; }

        public GenesisPlaybackWindowState? LastState { get; private set; }

        public IAsyncDisposable? Create(
            long ownerWindowToken,
            GenesisPlaybackWindowState state)
        {
            CreateCount++;
            LastOwnerWindowToken = ownerWindowToken;
            LastState = state;
            return Lifetime;
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

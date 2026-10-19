using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelAutoRefreshIntervalTests
{
    [Fact]
    public void StartupOptionsDefaultToSixtySeconds()
    {
        var options = new NovaWindowsStartupOptions();

        Assert.Equal(TimeSpan.FromSeconds(60), options.ChannelAutoRefreshInterval);
    }

    [Fact]
    public void StartupOptionsAcceptExplicitInterval()
    {
        var interval = TimeSpan.FromMinutes(2);
        var options = new NovaWindowsStartupOptions
        {
            ChannelAutoRefreshInterval = interval,
        };

        Assert.Equal(interval, options.ChannelAutoRefreshInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CompositionRejectsNonPositiveIntervalBeforeHostCreation(int seconds)
    {
        var factory = new RejectingHostFactory();
        var options = new NovaWindowsStartupOptions
        {
            ChannelAutoRefreshInterval = TimeSpan.FromSeconds(seconds),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NovaWindowsStartupComposition(options, factory));
        Assert.False(factory.CreateCalled);
    }

    [Fact]
    public async Task InteractiveSurfaceUsesConfiguredIntervalForAutomaticRefresh()
    {
        var catalog = new CountingCatalog();
        var target = new SystemGenesisInteractiveMainWindowSurfaceTarget(
            new RecordingSurfaceTarget(),
            new RecordingBridgeFactory(),
            catalog,
            TimeSpan.FromMilliseconds(25),
            static _ => NoopLifetime.Instance,
            static cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            },
            static (_, _) => GenesisChannelNotificationRuntime.Empty);

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.NotNull(lifetime);
        await catalog.SecondRead.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(catalog.ReadCount >= 2);

        await lifetime.DisposeAsync();
    }

    private sealed class CountingCatalog : IGenesisChannelCatalog
    {
        private readonly TaskCompletionSource<bool> _secondRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public Task SecondRead => _secondRead.Task;

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _readCount) >= 2)
            {
                _secondRead.TrySetResult(true);
            }

            return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
        }
    }

    private sealed class RecordingSurfaceTarget : IGenesisMainWindowSurfaceTarget
    {
        public ValueTask<IAsyncDisposable?> OpenAsync(
            long windowToken,
            GenesisMainViewModel viewModel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IAsyncDisposable?>(NoopLifetime.Instance);
        }
    }

    private sealed class RecordingBridgeFactory : IGenesisMainWindowInteractionBridgeFactory
    {
        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel) =>
            NoopLifetime.Instance;
    }

    private sealed class RejectingHostFactory : INovaWindowsStartupHostFactory
    {
        public bool CreateCalled { get; private set; }

        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
        {
            CreateCalled = true;
            throw new InvalidOperationException("Host creation should not be reached.");
        }
    }

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

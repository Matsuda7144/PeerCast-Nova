namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogAutoRefreshCoordinationTests
{
    [Fact]
    public async Task ManualRefreshQueuedDuringAutomaticRefreshDoesNotOverlap()
    {
        var catalog = new BlockingFirstReadCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(catalog, viewModel);

        try
        {
            session.StartAutoRefresh(TimeSpan.FromMilliseconds(20));
            await catalog.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var manualRefresh = session.RefreshAsync().AsTask();

            Assert.Equal(1, catalog.ReadCount);
            Assert.Equal(1, catalog.MaxConcurrentReads);
            Assert.False(catalog.SecondReadStarted.Task.IsCompleted);

            catalog.ReleaseFirstRead.TrySetResult(true);

            Assert.Equal(
                GenesisChannelCatalogRefreshResult.Completed,
                await manualRefresh.WaitAsync(TimeSpan.FromSeconds(1)));
            await catalog.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, catalog.MaxConcurrentReads);
        }
        finally
        {
            catalog.ReleaseFirstRead.TrySetResult(true);
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AutomaticRefreshContinuesAfterFaultedAttempt()
    {
        var recovered = new GenesisChannelItem(
            "Recovered",
            "pcp://example/recovered");
        var catalog = new FailThenSucceedCatalog(new([recovered]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(catalog, viewModel);
        var faultedObserved = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        session.DiagnosticsChanged += (_, _) =>
        {
            if (session.Diagnostics.LastResult ==
                GenesisChannelCatalogRefreshResult.Faulted)
            {
                faultedObserved.TrySetResult(true);
            }
        };

        try
        {
            session.StartAutoRefresh(TimeSpan.FromMilliseconds(20));

            await faultedObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await catalog.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() =>
                viewModel.Channels.Count == 1 &&
                ReferenceEquals(recovered, viewModel.Channels[0]));

            Assert.True(catalog.ReadCount >= 2);
            Assert.Equal(
                GenesisChannelCatalogRefreshResult.Completed,
                session.Diagnostics.LastResult);
            Assert.NotNull(session.Diagnostics.LastSuccessfulAt);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class BlockingFirstReadCatalog : IGenesisChannelCatalog
    {
        private readonly object _sync = new();
        private int _activeReads;
        private int _maxConcurrentReads;
        private int _readCount;

        public TaskCompletionSource<bool> FirstReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SecondReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseFirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount
        {
            get
            {
                lock (_sync)
                {
                    return _readCount;
                }
            }
        }

        public int MaxConcurrentReads
        {
            get
            {
                lock (_sync)
                {
                    return _maxConcurrentReads;
                }
            }
        }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            int readNumber;
            lock (_sync)
            {
                _activeReads++;
                _readCount++;
                readNumber = _readCount;
                _maxConcurrentReads = Math.Max(_maxConcurrentReads, _activeReads);
            }

            try
            {
                if (readNumber == 1)
                {
                    FirstReadStarted.TrySetResult(true);
                    await ReleaseFirstRead.Task.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (readNumber == 2)
                {
                    SecondReadStarted.TrySetResult(true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return GenesisChannelCatalogSnapshot.Empty;
            }
            finally
            {
                lock (_sync)
                {
                    _activeReads--;
                }
            }
        }
    }

    private sealed class FailThenSucceedCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _recoveredSnapshot;
        private int _readCount;

        public FailThenSucceedCatalog(
            GenesisChannelCatalogSnapshot recoveredSnapshot)
        {
            _recoveredSnapshot = recoveredSnapshot;
        }

        public TaskCompletionSource<bool> SecondReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readNumber = Interlocked.Increment(ref _readCount);
            if (readNumber == 1)
            {
                return ValueTask.FromException<GenesisChannelCatalogSnapshot>(
                    new InvalidOperationException("Transient catalog failure."));
            }

            SecondReadStarted.TrySetResult(true);
            return ValueTask.FromResult(_recoveredSnapshot);
        }
    }
}

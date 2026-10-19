using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogSessionTests
{
    [Fact]
    public async Task StartRunsInitialRefresh()
    {
        var catalog = new SequencedCatalog();
        var channel = new GenesisChannelItem("Initial", "pcp://example/initial");
        catalog.Enqueue(new([channel]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        await using var session = GenesisChannelCatalogSession.Start(catalog, viewModel);
        await catalog.WaitForReadsAsync(1);
        await WaitUntilAsync(() => viewModel.Channels.Count == 1);

        Assert.Equal(1, catalog.ReadCount);
        Assert.Same(channel, viewModel.Channels[0]);
    }

    [Fact]
    public async Task ManualRefreshRunsAfterInitialRefresh()
    {
        var catalog = new SequencedCatalog();
        catalog.Enqueue(new([
            new GenesisChannelItem("Initial", "pcp://example/initial"),
        ]));
        var updated = new GenesisChannelItem("Updated", "pcp://example/updated");
        catalog.Enqueue(new([updated]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        await using var session = GenesisChannelCatalogSession.Start(catalog, viewModel);
        await catalog.WaitForReadsAsync(1);
        await WaitUntilAsync(() => viewModel.Channels.Count == 1);

        var result = await session.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Equal(2, catalog.ReadCount);
        Assert.Single(viewModel.Channels);
        Assert.Same(updated, viewModel.Channels[0]);
    }

    [Fact]
    public async Task SuccessfulRefreshRecordsAttemptAndSuccessDiagnostics()
    {
        var catalog = new SequencedCatalog();
        catalog.Enqueue(GenesisChannelCatalogSnapshot.Empty);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(catalog, viewModel);

        Assert.Equal(GenesisChannelCatalogRefreshDiagnostics.Empty, session.Diagnostics);

        var result = await session.RefreshAsync();
        var diagnostics = session.Diagnostics;

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, diagnostics.LastResult);
        Assert.NotNull(diagnostics.LastAttemptedAt);
        Assert.NotNull(diagnostics.LastSuccessfulAt);
        Assert.True(diagnostics.LastSuccessfulAt >= diagnostics.LastAttemptedAt);
    }

    [Fact]
    public async Task SuccessfulCatalogRefreshEvaluatesItsSanitizedSnapshotOnce()
    {
        const string channelId = "531DC8DFC7FB42928AC2C0A626517A87";
        var catalog = new SequencedCatalog();
        catalog.Enqueue(new([
            new GenesisChannelItem(
                "  selected station  ",
                $"pcp://tracker.example:7144/channel/{channelId}",
                Type: "FLV",
                ChannelId: channelId),
        ]));
        var playbackController = new RecordingObserverPlaybackSessionController();
        var viewModel = new GenesisMainViewModel(playbackController);
        await using var session = GenesisChannelCatalogSession.Create(catalog, viewModel);

        var result = await session.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Equal(1, playbackController.ObservationCount);
        var observed = Assert.Single(playbackController.LastChannels!);
        Assert.Equal("selected station", observed.Name);
        Assert.Equal(viewModel.Channels, playbackController.LastChannels);
    }

    [Fact]
    public async Task SuccessfulCatalogRefreshRefreshesRecordingSnapshotWithoutAutomaticObserver()
    {
        var catalog = new SequencedCatalog();
        catalog.Enqueue(GenesisChannelCatalogSnapshot.Empty);
        var playbackController = new SnapshotOnlyRecordingPlaybackController();
        var viewModel = new GenesisMainViewModel(playbackController);
        await using var session = GenesisChannelCatalogSession.Create(catalog, viewModel);

        var result = await session.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Equal(1, playbackController.ActiveRecordingSnapshotReadCount);
        Assert.True(viewModel.ActiveRecordingSnapshotAvailable);
    }

    [Fact]
    public async Task FailedRefreshPreservesLastSuccessfulRefreshTime()
    {
        var catalog = new SucceedThenFailCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(catalog, viewModel);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync());
        var successful = session.Diagnostics;

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Faulted,
            await session.RefreshAsync());
        var failed = session.Diagnostics;

        Assert.NotNull(successful.LastSuccessfulAt);
        Assert.Equal(successful.LastSuccessfulAt, failed.LastSuccessfulAt);
        Assert.NotNull(failed.LastAttemptedAt);
        Assert.True(failed.LastAttemptedAt >= successful.LastAttemptedAt);
        Assert.Equal(GenesisChannelCatalogRefreshResult.Faulted, failed.LastResult);
    }

    [Fact]
    public async Task CancelledRequestBeforeExecutionDoesNotRecordAnAttempt()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await session.RefreshAsync(cancellation.Token);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Cancelled, result);
        Assert.Equal(GenesisChannelCatalogRefreshDiagnostics.Empty, session.Diagnostics);
    }

    [Fact]
    public async Task AutomaticRefreshRunsAfterInitialRefresh()
    {
        var catalog = new SequencedCatalog();
        catalog.Enqueue(new([
            new GenesisChannelItem("Initial", "pcp://example/initial"),
        ]));
        var updated = new GenesisChannelItem("Updated", "pcp://example/updated");
        catalog.Enqueue(new([updated]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        await using var session = GenesisChannelCatalogSession.Create(catalog, viewModel);
        session.StartInitialRefresh();
        session.StartAutoRefresh(TimeSpan.FromMilliseconds(20));

        await catalog.WaitForReadsAsync(2);
        await WaitUntilAsync(() =>
            viewModel.Channels.Count == 1 &&
            ReferenceEquals(updated, viewModel.Channels[0]));

        Assert.Equal(2, catalog.ReadCount);
        Assert.Same(updated, viewModel.Channels[0]);
    }

    [Fact]
    public async Task AutomaticRefreshRejectsNonPositiveInterval()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.StartAutoRefresh(TimeSpan.Zero));
    }

    [Fact]
    public async Task DisposeCancelsPendingInitialRefresh()
    {
        var catalog = new PendingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Start(catalog, viewModel);
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await session.DisposeAsync();

        Assert.True(catalog.Cancelled);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.NotNull(session.Diagnostics.LastAttemptedAt);
        Assert.Null(session.Diagnostics.LastSuccessfulAt);
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Cancelled,
            session.Diagnostics.LastResult);
    }

    [Fact]
    public async Task DisposeCompletesWhenCatalogCancellationCallbackThrows()
    {
        var catalog = new ThrowingCancellationCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Start(catalog, viewModel);
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await session.DisposeAsync();

        Assert.True(catalog.CancellationCallbackInvoked);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Cancelled,
            await session.RefreshAsync());
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

    private sealed class SequencedCatalog : IGenesisChannelCatalog
    {
        private readonly Queue<GenesisChannelCatalogSnapshot> _snapshots = new();
        private readonly object _sync = new();
        private TaskCompletionSource _readChanged = NewReadChanged();
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public void Enqueue(GenesisChannelCatalogSnapshot snapshot)
        {
            lock (_sync)
            {
                _snapshots.Enqueue(snapshot);
            }
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenesisChannelCatalogSnapshot snapshot;
            TaskCompletionSource readChanged;
            lock (_sync)
            {
                snapshot = _snapshots.Dequeue();
                readChanged = _readChanged;
                _readChanged = NewReadChanged();
                Interlocked.Increment(ref _readCount);
            }

            readChanged.TrySetResult();
            return ValueTask.FromResult(snapshot);
        }

        public async Task WaitForReadsAsync(int expected)
        {
            while (ReadCount < expected)
            {
                Task wait;
                lock (_sync)
                {
                    if (ReadCount >= expected)
                    {
                        return;
                    }

                    wait = _readChanged.Task;
                }

                await wait.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        private static TaskCompletionSource NewReadChanged() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SucceedThenFailCatalog : IGenesisChannelCatalog
    {
        private int _readCount;

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Interlocked.Increment(ref _readCount) == 1
                ? ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty)
                : ValueTask.FromException<GenesisChannelCatalogSnapshot>(
                    new InvalidOperationException("Catalog failed."));
        }
    }

    private sealed class PendingCatalog : IGenesisChannelCatalog
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
                return GenesisChannelCatalogSnapshot.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
        }
    }

    private sealed class ThrowingCancellationCatalog : IGenesisChannelCatalog
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationCallbackInvoked { get; private set; }

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() =>
            {
                CancellationCallbackInvoked = true;
                throw new InvalidOperationException("Cancellation callback failed.");
            });
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            return GenesisChannelCatalogSnapshot.Empty;
        }
    }

    private sealed class RecordingObserverPlaybackSessionController :
        IPlaybackSessionController,
        IGenesisAutomaticRecordingCatalogObserver
    {
        public PlaybackSessionSnapshot Snapshot => PlaybackSessionSnapshot.Stopped;

        public int ObservationCount { get; private set; }

        public IReadOnlyList<GenesisChannelItem>? LastChannels { get; private set; }

        public ValueTask<GenesisAutomaticRecordingEvaluationSummary> ObserveAsync(
            IReadOnlyList<GenesisChannelItem> channels,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObservationCount++;
            LastChannels = channels.ToArray();
            return ValueTask.FromResult(
                new GenesisAutomaticRecordingEvaluationSummary(
                    true,
                    channels.Count,
                    channels.Count,
                    0,
                    0,
                    0,
                    false));
        }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = source;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));
        }
    }

    private sealed class SnapshotOnlyRecordingPlaybackController :
        IPlaybackSessionController,
        IGenesisManualRecordingSessionController
    {
        public PlaybackSessionSnapshot Snapshot => PlaybackSessionSnapshot.Stopped;

        public int ActiveRecordingSnapshotReadCount { get; private set; }

        public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel) =>
            GenesisRecordingCoreSnapshot.Unavailable;

        public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId) =>
            GenesisRecordingCoreSnapshot.Unavailable;

        public bool IsKnownManualSession(ChannelId channelId, RecordingSessionId sessionId) =>
            false;

        public ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActiveRecordingSnapshotReadCount++;
            return ValueTask.FromResult(new GenesisActiveRecordingSessionSnapshot(
                true,
                Array.AsReadOnly(Array.Empty<GenesisActiveRecordingSession>())));
        }

        public ValueTask<GenesisManualRecordingSessionSnapshot> GetActiveManualRecordingsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisManualRecordingSessionSnapshot.Unavailable);

        public ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GenesisManualRecordingCommandOutcome(
                GenesisManualRecordingCommandKind.Failed,
                GenesisRecordingCoreSnapshot.Unavailable));

        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GenesisManualRecordingCommandOutcome(
                GenesisManualRecordingCommandKind.NoActiveSession,
                GenesisRecordingCoreSnapshot.Unavailable));

        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
            ChannelId channelId,
            RecordingSessionId expectedSessionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GenesisManualRecordingCommandOutcome(
                GenesisManualRecordingCommandKind.NoActiveSession,
                GenesisRecordingCoreSnapshot.Unavailable));

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));
    }
}

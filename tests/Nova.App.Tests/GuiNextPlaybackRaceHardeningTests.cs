using System.Reflection;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextPlaybackRaceHardeningTests
{
    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

    [Fact]
    public async Task StalePlaybackObserverDoesNotCancelOrDisposeNewOwnership()
    {
        var controller = new NovaDailyHttpFlvPlaybackController(
            null,
            static _ => { },
            static _ => { });
        var oldPlayer = new ControlledPlayerLifetime();
        var newPlayer = new ControlledPlayerLifetime();
        var oldActive = CreateActivePlayback(oldPlayer, out var oldHost);
        var newActive = CreateActivePlayback(newPlayer, out var newHost);
        var newStartupCancellation = new CancellationTokenSource();

        SetPrivateField(controller, "_active", oldActive);
        controller.PlaybackEnded += _ =>
        {
            // PlaybackEnded is raised after the observer's current-owner check.
            // This callback deterministically installs the newer Play ownership
            // before the old observer attempts cleanup.
            SetPrivateField(controller, "_startupCancellation", newStartupCancellation);
            SetPrivateField(controller, "_active", newActive);
        };

        try
        {
            var observer = StartObserver(controller, oldActive);
            oldPlayer.Complete();
            await observer.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(newStartupCancellation.IsCancellationRequested);
            Assert.Same(newActive, GetPrivateField(controller, "_active"));
            Assert.Equal(0, newPlayer.DisposeCount);
            Assert.Equal(0, (int)GetPrivateField(newHost, "_disposed")!);
        }
        finally
        {
            await controller.DisposeAsync();
            await oldHost.DisposeAsync();
            await oldPlayer.DisposeAsync();
            newStartupCancellation.Dispose();
        }
    }

    [Theory]
    [InlineData(PlaybackSessionState.Stopped, null, "Ready")]
    [InlineData(PlaybackSessionState.Faulted, "newer session failed", "Playback error")]
    public async Task DelayedPlayOutcomeCannotOverwriteNewerControllerSnapshot(
        PlaybackSessionState newerState,
        string? newerError,
        string expectedStatus)
    {
        var controller = new ControlledSnapshotPlaybackController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = Source,
        };

        var play = viewModel.PlayAsync().AsTask();
        await controller.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var newerSnapshot = new PlaybackSessionSnapshot(newerState, Source, newerError);
        controller.Publish(newerSnapshot);
        Assert.Equal(newerState, viewModel.PlaybackState);

        controller.CompletePlay(new(
            PlaybackSessionCommandResult.Completed,
            new(PlaybackSessionState.Playing, Source, null)));
        await play.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(newerSnapshot, controller.Snapshot);
        Assert.Equal(newerState, viewModel.PlaybackState);
        Assert.Equal(expectedStatus, viewModel.StatusText);
        Assert.Equal(newerError, viewModel.ErrorMessage);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    private static object CreateActivePlayback(
        ControlledPlayerLifetime player,
        out PcpHttpFlvPlaybackHost host)
    {
        host = new PcpHttpFlvPlaybackHost();
        var activeType = typeof(NovaDailyHttpFlvPlaybackController).GetNestedType(
            "ActivePlayback",
            BindingFlags.NonPublic)!;
        var constructor = activeType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        var active = constructor.Invoke([host]);
        activeType.GetProperty("Player")!.SetValue(active, player);
        return active;
    }

    private static Task StartObserver(
        NovaDailyHttpFlvPlaybackController controller,
        object active)
    {
        var method = typeof(NovaDailyHttpFlvPlaybackController).GetMethod(
            "ObservePlaybackEndAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(controller, [active, "old channel", Source])!;
    }

    private static void SetPrivateField(object instance, string name, object? value) =>
        typeof(NovaDailyHttpFlvPlaybackController)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static object? GetPrivateField(object instance, string name) =>
        instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance);

    private sealed class ControlledPlayerLifetime : IDailyPlaybackPlayerLifetime
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public int DisposeCount { get; private set; }

        public void Complete() => _completion.TrySetResult();

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledSnapshotPlaybackController :
        IPlaybackSessionController,
        IPlaybackSessionSnapshotNotifications
    {
        private readonly TaskCompletionSource<PlaybackSessionCommandOutcome> _playResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? SnapshotChanged;

        public PlaybackSessionSnapshot Snapshot { get; private set; } =
            PlaybackSessionSnapshot.Stopped;

        public TaskCompletionSource PlayStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            _ = source;
            _ = cancellationToken;
            PlayStarted.TrySetResult();
            return new(_playResult.Task);
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }

        public void Publish(PlaybackSessionSnapshot snapshot)
        {
            Snapshot = snapshot;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }

        public void CompletePlay(PlaybackSessionCommandOutcome outcome) =>
            _playResult.TrySetResult(outcome);
    }
}

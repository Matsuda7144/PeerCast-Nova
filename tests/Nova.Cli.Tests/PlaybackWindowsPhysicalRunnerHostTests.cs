using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackWindowsPhysicalRunnerHostTests
{
    [Fact]
    public async Task StartInvokeAndStopUseOneApplicationOwnedThreadAndWindow()
    {
        using var native = new FakeNative();
        await using var host = CreateHost(native);

        var start = await host.StartAsync();
        var invocation = await host.InvokeAsync(_ => ValueTask.FromResult(
            (host.HasAccess, Environment.CurrentManagedThreadId)));

        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, start);
        Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Completed, invocation.Result);
        Assert.True(invocation.Value.HasAccess);
        var running = host.Snapshot;
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Running, running.State);
        Assert.Equal(FakeNative.WindowToken, running.WindowToken);
        Assert.Equal(invocation.Value.CurrentManagedThreadId, running.OwnerManagedThreadId);
        Assert.True(running.HasMessageLoop);
        Assert.False(running.AdmissionClosed);
        Assert.Equal(1, running.AcceptedOperationCount);
        Assert.Equal(1, running.CompletedOperationCount);

        var stop = await host.StopAsync();

        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Stopped, stop);
        var stopped = host.Snapshot;
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, stopped.State);
        Assert.Equal(0, stopped.WindowToken);
        Assert.Equal(0, stopped.PendingOperationCount);
        Assert.False(stopped.HasMessageLoop);
        Assert.True(stopped.AdmissionClosed);
    }

    [Fact]
    public async Task UnsupportedPlatformFailsBeforeCreatingNativeWindow()
    {
        using var native = new FakeNative();
        await using var host = new PlaybackWindowsPhysicalRunnerHost(
            new(),
            native,
            static () => false);

        var result = await host.StartAsync();

        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported, result);
        Assert.Equal(0, native.RunCount);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, host.Snapshot.State);
        Assert.True(host.Snapshot.AdmissionClosed);
    }

    [Fact]
    public async Task InvalidConfigurationFailsClosedBeforeStartingThread()
    {
        using var native = new FakeNative();
        await using var host = new PlaybackWindowsPhysicalRunnerHost(
            new() { ClientWidth = 1 },
            native,
            static () => true);

        var result = await host.StartAsync();

        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.InvalidConfiguration, result);
        Assert.Equal(0, native.RunCount);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Faulted, host.Snapshot.State);
        Assert.True(host.Snapshot.AdmissionClosed);
    }

    [Fact]
    public async Task CancellationBeforeAdmissionDoesNotRetainOrExecuteOperation()
    {
        using var native = new FakeNative();
        await using var host = CreateHost(native);
        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, await host.StartAsync());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executed = false;

        var invocation = await host.InvokeAsync(
            _ =>
            {
                executed = true;
                return ValueTask.FromResult(1);
            },
            cancellation.Token);

        Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Cancelled, invocation.Result);
        Assert.False(executed);
        Assert.Equal(0, host.Snapshot.PendingOperationCount);
        Assert.Equal(1, host.Snapshot.CancelledOperationCount);
    }

    [Fact]
    public async Task BoundedAdmissionRejectsExcessWorkWithoutHiddenRetry()
    {
        using var native = new FakeNative();
        await using var host = CreateHost(
            native,
            new() { MaximumPendingOperations = 1 });
        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, await host.StartAsync());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var first = host.InvokeAsync(
            token =>
            {
                entered.Set();
                release.Wait(token);
                return ValueTask.FromResult(1);
            }).AsTask();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var second = host.InvokeAsync(_ => ValueTask.FromResult(2)).AsTask();
        var rejected = await host.InvokeAsync(_ => ValueTask.FromResult(3));

        Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Faulted, rejected.Result);
        Assert.Equal(1, host.Snapshot.PendingOperationCount);
        release.Set();
        Assert.Equal(
            PlaybackWindowsOutputDispatcherInvocationResult.Completed,
            (await first.WaitAsync(TimeSpan.FromSeconds(5))).Result);
        Assert.Equal(
            PlaybackWindowsOutputDispatcherInvocationResult.Completed,
            (await second.WaitAsync(TimeSpan.FromSeconds(5))).Result);
        Assert.Equal(2, host.Snapshot.AcceptedOperationCount);
        Assert.Equal(2, host.Snapshot.CompletedOperationCount);
        Assert.Equal(1, host.Snapshot.RejectedOperationCount);
    }

    [Fact]
    public async Task StopRejectsNewWorkAndSettlesQueuedWorkAsShutdown()
    {
        using var native = new FakeNative();
        await using var host = CreateHost(native);
        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, await host.StartAsync());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var active = host.InvokeAsync(
            token =>
            {
                entered.Set();
                release.Wait(token);
                return ValueTask.FromResult(1);
            }).AsTask();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var queued = host.InvokeAsync(_ => ValueTask.FromResult(2)).AsTask();
        var stop = host.StopAsync().AsTask();

        Assert.Equal(
            PlaybackWindowsOutputDispatcherInvocationResult.Shutdown,
            (await queued.WaitAsync(TimeSpan.FromSeconds(5))).Result);
        var afterStop = await host.InvokeAsync(_ => ValueTask.FromResult(3));
        Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Shutdown, afterStop.Result);
        release.Set();
        Assert.Equal(
            PlaybackWindowsOutputDispatcherInvocationResult.Completed,
            (await active.WaitAsync(TimeSpan.FromSeconds(5))).Result);
        Assert.Equal(
            PlaybackWindowsPhysicalRunnerResult.Stopped,
            await stop.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, host.Snapshot.PendingOperationCount);
        Assert.True(host.Snapshot.AdmissionClosed);
    }

    [Fact]
    public async Task NativeStartupFailureCompletesStartAsFaulted()
    {
        using var native = new FakeNative(failBeforeStart: true);
        await using var host = CreateHost(native);

        var result = await host.StartAsync();

        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Faulted, result);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Faulted, host.Snapshot.State);
        Assert.True(host.Snapshot.AdmissionClosed);
    }

    [Fact]
    public async Task NativeWindowCloseEndsLoopAndRejectsLaterWork()
    {
        using var native = new FakeNative();
        await using var host = CreateHost(native);
        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, await host.StartAsync());

        native.CloseFromNative();
        await native.RunCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = await host.StopAsync();
        var invocation = await host.InvokeAsync(_ => ValueTask.FromResult(1));

        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Stopped, stop);
        Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Shutdown, invocation.Result);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, host.Snapshot.State);
    }

    private static PlaybackWindowsPhysicalRunnerHost CreateHost(
        FakeNative native,
        PlaybackWindowsPhysicalRunnerOptions? options = null) => new(
            options ?? new(),
            native,
            static () => true);

    private sealed class FakeNative : IPlaybackWindowsPhysicalRunnerNative
    {
        public const long WindowToken = 7_144;
        private readonly AutoResetEvent _work = new(false);
        private readonly ManualResetEvent _stop = new(false);
        private readonly WaitHandle[] _waitHandles;
        private readonly bool _failBeforeStart;
        private bool _isDisposed;

        public FakeNative(bool failBeforeStart = false)
        {
            _failBeforeStart = failBeforeStart;
            _waitHandles = [_work, _stop];
        }

        public int RunCount { get; private set; }

        public TaskCompletionSource RunCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackWindowsPhysicalRunnerNativeRunResult Run(
            PlaybackWindowsPhysicalRunnerOptions options,
            PlaybackWindowsPhysicalRunnerNativeCallbacks callbacks)
        {
            RunCount++;
            try
            {
                if (_failBeforeStart)
                {
                    return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
                }

                callbacks.Started(WindowToken);
                while (true)
                {
                    var signalled = WaitHandle.WaitAny(_waitHandles);
                    if (signalled == 1)
                    {
                        return PlaybackWindowsPhysicalRunnerNativeRunResult.Stopped;
                    }

                    callbacks.DrainWork();
                }
            }
            finally
            {
                RunCompleted.TrySetResult();
            }
        }

        public bool SignalWork()
        {
            if (_isDisposed)
            {
                return false;
            }

            return _work.Set();
        }

        public bool RequestStop()
        {
            if (_isDisposed)
            {
                return false;
            }

            return _stop.Set();
        }

        public void CloseFromNative() => _stop.Set();

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _stop.Set();
            _work.Dispose();
            _stop.Dispose();
        }
    }
}

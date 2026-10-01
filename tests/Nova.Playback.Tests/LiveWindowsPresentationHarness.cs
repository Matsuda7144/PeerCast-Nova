using System.Text.Json;
using PeerCast.Nova.Windows;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

internal sealed record LivePresentationIdentity(string TaskId, string RunId, string Head, string GuardDirectory)
{
    public string WindowTitle => $"PeerCast Nova live fMP4 physical presentation {RunId}";
    public string GuardPath => Path.Combine(GuardDirectory, TaskId + ".physical-once.json");

    public void Validate()
    {
        static bool ValidId(string id) => id.Length is > 0 and <= 128 &&
            id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (!ValidId(TaskId) || !ValidId(RunId) || Head.Length != 40 || !Head.All(Uri.IsHexDigit) ||
            !Path.IsPathFullyQualified(GuardDirectory) || !Directory.Exists(GuardDirectory))
        {
            throw new ArgumentException("Require explicit task/run identity, source SHA and an existing absolute guard directory.");
        }
    }

    public PlaybackWindowsPhysicalRunnerOptions RunnerOptions => new()
    {
        ClientWidth = 640,
        ClientHeight = 360,
        Title = WindowTitle,
        MaximumPendingOperations = 4,
    };
}

internal interface ILiveWindowsPresentationSession : IAsyncDisposable
{
    IPlaybackPcpFmp4PresentationOutputGenerationFactory Output { get; }
    ValueTask StartAsync(CancellationToken cancellationToken);
    void CaptureCompleted();
}

// The physical entry and fake-session lifecycle tests use this same owner.
// The durable CreateNew marker is never removed, even on initialization failure.
internal static class LiveWindowsPresentationHarness
{
    public static async Task RunOnceAsync(LivePresentationIdentity identity,
        Func<LivePresentationIdentity, ILiveWindowsPresentationSession> createSession,
        Func<IPlaybackPcpFmp4PresentationOutputGenerationFactory, Action, CancellationToken, Task> runLive,
        CancellationToken cancellationToken)
    {
        identity.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using (var guard = new FileStream(identity.GuardPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(guard, new
            {
                identity.TaskId,
                identity.RunId,
                sourceHead = identity.Head,
                startUtc = DateTimeOffset.UtcNow,
                authorizedMaximum = 1,
                presentationInitializationAttemptCount = 1,
                boundary = "Before session creation/StartAsync; marker prevents any second launch.",
            });
            guard.Flush(flushToDisk: true);
        }

        var session = createSession(identity);
        Exception? failure = null;
        try
        {
            await session.StartAsync(cancellationToken);
            var completed = false;
            await runLive(session.Output, () =>
            {
                session.CaptureCompleted();
                completed = true;
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(completed, "Live receiver/decoder/output completion must be observed before successful teardown.");
        }
        catch (Exception error) { failure = error; }
        Exception? cleanupFailure = null;
        try { await session.DisposeAsync(); }
        catch (Exception error) { cleanupFailure = error; }
        if (failure is not null && cleanupFailure is not null)
        { throw new AggregateException("Live presentation and teardown failed.", failure, cleanupFailure); }
        if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
        if (cleanupFailure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw(); }
        // Completion is exclusively downstream QUIT -> decoder EOS -> output drain.
        // A cancelled/failed producer must throw; it must never call CaptureCompleted.
    }
}

internal sealed class LiveWindowsPresentationSession(
    LivePresentationIdentity identity, ITestOutputHelper output) : ILiveWindowsPresentationSession
{
    private readonly PlaybackWindowsPhysicalRunnerHost _runner = new(identity.RunnerOptions);
    private PlaybackFmp4PhysicalPresentationAcceptanceTests.PresentationResources? _resources;
    private PlaybackFmp4PhysicalPresentationAcceptanceTests.WindowsLoopbackPresentationFactory? _factory;
    private bool _disposed;

    public PlaybackWindowsPhysicalRunnerSnapshot RunnerSnapshot => _runner.Snapshot;
    public bool CompletedCaptured { get; private set; }
    public IPlaybackPcpFmp4PresentationOutputGenerationFactory Output =>
        _factory ?? throw new InvalidOperationException("Windows output must be initialized explicitly.");

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        Write($"Task-ID: {identity.TaskId}; Run-ID: {identity.RunId}; HEAD: {identity.Head}; initialization-attempt=1");
        Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, await _runner.StartAsync(cancellationToken));
        var start = _runner.Snapshot;
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Running, start.State);
        Assert.True(start.HasMessageLoop); Assert.True(start.WindowToken > 0);
        Write("Runner: " + JsonSerializer.Serialize(start));
        var initialized = await _runner.InvokeAsync(token =>
            PlaybackFmp4PhysicalPresentationAcceptanceTests.PresentationResources.CreateAsync(start.WindowToken, token),
            cancellationToken);
        // Retain ownership even when cancellation changes the dispatch result after creation.
        _resources = initialized.Value;
        Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Completed, initialized.Result);
        Assert.NotNull(_resources);
        _factory = new(_runner, _resources);
    }

    public void CaptureCompleted()
    {
        Assert.False(CompletedCaptured);
        var adapter = Assert.Single(_factory!.Adapters);
        var snapshot = adapter.Snapshot;
        var sink = _resources!.Sink.Snapshot;
        var native = sink.Output.NativeEvidence;
        Write("Presentation completed snapshot: " + JsonSerializer.Serialize(snapshot));
        Write("Windows output snapshot: " + JsonSerializer.Serialize(sink));
        Assert.Equal(PlaybackDecodedFrameWindowsPresentationState.Completed, snapshot.State);
        Assert.Equal(192, snapshot.VideoFrameSubmittedCount);
        Assert.Equal(301, snapshot.AudioFrameSubmittedCount);
        Assert.Equal(192, snapshot.VideoPresentedCount + snapshot.LateVideoDropCount);
        Assert.Equal(301, snapshot.AudioWrittenCount);
        Assert.Equal(snapshot.VideoPresentedCount, native.VideoPresentAcknowledgementCount);
        Assert.True(native.VideoPresentAcknowledgementCount >= 2);
        Assert.Equal(snapshot.AudioWrittenCount, native.AudioWriteAcknowledgementCount);
        Assert.True(native.AudioStartAcknowledgementCount > 0);
        Assert.Equal(308_224, native.AudioAcceptedFrameCount);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceSource.SystemWindows, native.Source);
        Assert.Equal(64, native.VideoWidth); Assert.Equal(48, native.VideoHeight);
        Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, native.VideoPixelFormat);
        Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, native.AudioSampleFormat);
        Assert.Equal(48_000, native.AudioSampleRate); Assert.Equal(2, native.AudioChannelCount);
        Assert.Equal(0, native.VideoDeviceLostCount); Assert.Equal(0, native.AudioDeviceLostCount);
        Assert.False(native.CounterOverflowed);
        Assert.False(native.VideoConfigurationConflict); Assert.False(native.AudioConfigurationConflict);
        Assert.Equal(0, snapshot.ResetCount);
        Assert.Equal(0, snapshot.CurrentOutputInFlight);
        Assert.Equal(1, snapshot.MaximumOutputInFlight);
        Assert.Equal(PlaybackWindowsAudioPaddingReadResult.Available, snapshot.AudioPaddingAtCompletion.Result);
        Assert.Equal(0, snapshot.AudioPaddingAtCompletion.PaddingFrameCount);
        CompletedCaptured = true;
        Write("Human moving video/audio observation: NOT RECORDED; user confirmation required.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        try
        {
            if (_resources is not null)
            {
                var resources = _resources;
                var cleanup = await _runner.InvokeAsync(async _ =>
                {
                    await resources.DisposeAsync();
                    return resources.CleanupSnapshot;
                }, CancellationToken.None);
                Write("Cleanup: " + JsonSerializer.Serialize(cleanup));
                Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Completed, cleanup.Result);
                Assert.Equal(PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed, cleanup.Value.SinkState);
                Assert.Equal(PlaybackWindowsOutputBufferState.Disposed, cleanup.Value.VideoBufferState);
                Assert.Equal(PlaybackWindowsOutputBufferState.Disposed, cleanup.Value.AudioBufferState);
                Assert.Equal(PlaybackWindowsOutputStreamState.Disposed, cleanup.Value.VideoStreamState);
                Assert.Equal(PlaybackWindowsOutputStreamState.Disposed, cleanup.Value.AudioStreamState);
                Assert.Equal(PlaybackWindowsOutputBindingState.Disposed, cleanup.Value.VideoBindingState);
                Assert.Equal(PlaybackWindowsOutputBindingState.Disposed, cleanup.Value.AudioBindingState);
                Assert.Equal(PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState.Disposed,
                    cleanup.Value.ReconfigurationState);
            }
        }
        finally
        {
            try
            {
                try { _factory?.Dispose(); }
                finally
                {
                    var stop = await _runner.StopAsync();
                    Write("Stop result: " + stop);
                    // Faulted startup remains a failed run; stopping it must not conceal the first error.
                    Assert.True(stop is PlaybackWindowsPhysicalRunnerResult.Stopped or PlaybackWindowsPhysicalRunnerResult.Faulted);
                }
                Write("Stopped runner: " + JsonSerializer.Serialize(_runner.Snapshot));
                Assert.False(_runner.Snapshot.HasMessageLoop);
            }
            finally { await _runner.DisposeAsync(); }
        }
        Write("Disposed runner: " + JsonSerializer.Serialize(_runner.Snapshot));
    }

    private void Write(string message) { output.WriteLine(message); Console.WriteLine(message); }
}

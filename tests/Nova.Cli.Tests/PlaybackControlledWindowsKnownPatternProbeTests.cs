using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsKnownPatternProbeTests
{
    [Fact]
    public void Pattern_is_exact_opaque_non_black_four_quadrant_bgra()
    {
        var frame = PlaybackControlledWindowsKnownPattern.Create(7);
        var colors = new HashSet<uint>();
        var nonBlackPixels = 0;
        var span = frame.Buffer.Span;

        for (var offset = 0; offset < span.Length; offset += 4)
        {
            var blue = span[offset];
            var green = span[offset + 1];
            var red = span[offset + 2];
            var alpha = span[offset + 3];
            Assert.Equal(byte.MaxValue, alpha);
            if (blue != 0 || green != 0 || red != 0)
            {
                nonBlackPixels++;
            }

            colors.Add((uint)(blue | (green << 8) | (red << 16) | (alpha << 24)));
        }

        Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, frame.PixelFormat);
        Assert.Equal(320, frame.Width);
        Assert.Equal(240, frame.Height);
        Assert.Equal(1_280, frame.Stride);
        Assert.Equal(307_200, frame.Buffer.Length);
        Assert.Equal(76_800, nonBlackPixels);
        Assert.Equal(4, colors.Count);
        Assert.Equal(7, frame.Generation);
    }

    [Fact]
    public async Task Accepted_present_cleans_output_before_runner_and_records_evidence()
    {
        var operations = new FakeOperations();
        await using var probe = new PlaybackControlledWindowsKnownPatternProbe(
            new() { ObservationHoldDuration = TimeSpan.Zero },
            operations);

        var result = await probe.RunAsync();
        var snapshot = probe.Snapshot;

        Assert.Equal(PlaybackControlledWindowsKnownPatternProbeResult.Completed, result);
        Assert.Equal(PlaybackControlledWindowsKnownPatternProbeState.Completed, snapshot.State);
        Assert.Equal(1, snapshot.PresentAttemptCount);
        Assert.Equal(1, snapshot.PresentAcknowledgementCount);
        Assert.Equal(307_200, snapshot.AcceptedByteCount);
        Assert.Equal(0, snapshot.WouldBlockCount);
        Assert.Equal(0, snapshot.ResizeCount);
        Assert.Equal(0, snapshot.DeviceLostCount);
        Assert.True(snapshot.OutputCleanupCompleted);
        Assert.True(snapshot.BorrowedWindowPreserved);
        Assert.True(snapshot.BorrowedDispatcherPreserved);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, snapshot.RunnerState);
        Assert.Equal(
            ["runner:start", "output:init", "present", "output:cleanup", "owners:verify",
                "runner:stop"],
            operations.Log);
        Assert.Equal(1, operations.PresentCount);
    }

    [Fact]
    public async Task Cancellation_during_hold_cleans_output_before_runner_without_second_present()
    {
        var operations = new FakeOperations();
        await using var probe = new PlaybackControlledWindowsKnownPatternProbe(
            new() { ObservationHoldDuration = TimeSpan.FromSeconds(30) },
            operations);
        using var cancellation = new CancellationTokenSource();

        var run = probe.RunAsync(cancellation.Token).AsTask();
        await operations.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        for (var index = 0; index < 1_000 &&
            probe.Snapshot.State !=
                PlaybackControlledWindowsKnownPatternProbeState.HoldingObservation;
            index++)
        {
            await Task.Yield();
        }

        var stateDuringHold = probe.Snapshot.State;
        cancellation.Cancel();
        var result = await run;

        Assert.Equal(
            PlaybackControlledWindowsKnownPatternProbeState.HoldingObservation,
            stateDuringHold);
        Assert.Equal(PlaybackControlledWindowsKnownPatternProbeResult.Cancelled, result);
        Assert.Equal(1, operations.PresentCount);
        Assert.Equal(
            ["runner:start", "output:init", "present", "output:cleanup", "runner:stop"],
            operations.Log);
    }

    [Fact]
    public async Task Hold_above_bound_fails_before_runner_start()
    {
        var operations = new FakeOperations();
        await using var probe = new PlaybackControlledWindowsKnownPatternProbe(
            new()
            {
                ObservationHoldDuration =
                    PlaybackControlledWindowsKnownPatternProbeOptions
                        .MaximumObservationHoldDuration + TimeSpan.FromMilliseconds(1),
            },
            operations);

        var result = await probe.RunAsync();

        Assert.Equal(
            PlaybackControlledWindowsKnownPatternProbeResult.InvalidConfiguration,
            result);
        Assert.Empty(operations.Log);
    }

    [Fact]
    public void Canonical_record_is_fixed_and_path_free()
    {
        var snapshot = new PlaybackControlledWindowsKnownPatternProbeSnapshot(
            PlaybackControlledWindowsKnownPatternProbeState.Completed,
            PlaybackControlledWindowsKnownPatternProbeResult.Completed,
            PlaybackControlledWindowsKnownPattern.Id,
            320,
            240,
            1_280,
            76_800,
            1,
            1,
            307_200,
            0,
            0,
            0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            OutputCleanupCompleted: true,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true);

        var record = PlaybackControlledWindowsKnownPatternProbeRecordCodec.Create(snapshot);
        var text = string.Join('\n', record);

        Assert.Equal(PlaybackControlledWindowsKnownPatternProbeRecordCodec.Header, record[0]);
        Assert.Equal(PlaybackControlledWindowsKnownPatternProbeRecordCodec.Footer, record[^1]);
        Assert.Contains("Result: Completed", record);
        Assert.Contains("Pattern: Bgra32QuadrantsV1", record);
        Assert.Contains("Present-Attempts: 1", record);
        Assert.Contains("Present-Acknowledgements: 1", record);
        Assert.Contains("FFmpeg-Runtime-Used: False", record);
        Assert.Contains("External-Media-Or-Payload-Published: False", record);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/", text, StringComparison.Ordinal);
    }

    private sealed class FakeOperations : IPlaybackControlledWindowsKnownPatternProbeOperations
    {
        private bool _stopped;
        private bool _cleanup;

        public List<string> Log { get; } = [];

        public int PresentCount { get; private set; }

        public TaskCompletionSource Presented { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackControlledWindowsKnownPatternProbeOwnedSnapshot Snapshot => new(
            new(
                _stopped
                    ? PlaybackWindowsPhysicalRunnerState.Stopped
                    : PlaybackWindowsPhysicalRunnerState.Running,
                _stopped
                    ? PlaybackWindowsPhysicalRunnerResult.Stopped
                    : PlaybackWindowsPhysicalRunnerResult.Started,
                WindowToken: _stopped ? 0 : 7144,
                OwnerManagedThreadId: _stopped ? 0 : 7,
                PendingOperationCount: 0,
                AcceptedOperationCount: 3,
                CompletedOperationCount: 3,
                CancelledOperationCount: 0,
                RejectedOperationCount: 0,
                HasMessageLoop: !_stopped,
                AdmissionClosed: _stopped),
            new(
                _cleanup
                    ? PlaybackWindowsOutputBufferState.Disposed
                    : PlaybackWindowsOutputBufferState.Ready,
                _cleanup
                    ? PlaybackWindowsOutputBufferState.Disposed
                    : PlaybackWindowsOutputBufferState.Ready,
                VideoGeneration: 1,
                AudioGeneration: 1,
                AudioStarted: false)
            {
                NativeEvidence = Evidence(),
            });

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("runner:start");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Started);
        }

        public ValueTask<PlaybackControlledWindowsKnownPatternOutputResult> InitializeOutputAsync(
            long generation,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(1, generation);
            Log.Add("output:init");
            return ValueTask.FromResult(
                PlaybackControlledWindowsKnownPatternOutputResult.Initialized);
        }

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> PresentAsync(
            PlaybackWindowsVideoFrameBuffer frame,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(PlaybackControlledWindowsKnownPattern.ByteCount, frame.Buffer.Length);
            PresentCount++;
            Log.Add("present");
            Presented.TrySetResult();
            return ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed);
        }

        public ValueTask CleanupOutputAsync()
        {
            if (!_cleanup)
            {
                _cleanup = true;
                Log.Add("output:cleanup");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<PlaybackControlledWindowsKnownPatternProbePreservation>
            VerifyBorrowedOwnersAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("owners:verify");
            return ValueTask.FromResult(new PlaybackControlledWindowsKnownPatternProbePreservation(
                OutputCleanupCompleted: _cleanup,
                WindowPreserved: !_stopped,
                DispatcherPreserved: !_stopped));
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync()
        {
            if (!_stopped)
            {
                _stopped = true;
                Log.Add("runner:stop");
            }

            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static PlaybackWindowsOutputNativeEvidenceSnapshot Evidence() => new(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: PlaybackControlledWindowsKnownPattern.Width,
            VideoHeight: PlaybackControlledWindowsKnownPattern.Height,
            VideoStride: PlaybackControlledWindowsKnownPattern.Stride,
            VideoGeneration: 1,
            VideoPresentAttemptCount: 1,
            VideoPresentAcknowledgementCount: 1,
            VideoAcceptedByteCount: PlaybackControlledWindowsKnownPattern.ByteCount,
            VideoWouldBlockCount: 0,
            VideoResizeCount: 0,
            VideoDeviceLostCount: 0,
            AudioConfigurationConflict: false,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            AudioGeneration: 1,
            AudioWriteAttemptCount: 0,
            AudioWriteAcknowledgementCount: 0,
            AudioAcceptedFrameCount: 0,
            AudioAcceptedByteCount: 0,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 0,
            AudioDeviceLostCount: 0);
    }
}

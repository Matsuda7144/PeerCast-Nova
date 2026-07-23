using System.Buffers.Binary;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsKnownAudioToneProbeTests
{
    [Fact]
    public void Tone_is_exact_bounded_finite_stereo_float32_interleaved()
    {
        var tone = PlaybackControlledWindowsKnownAudioTone.Create(7);
        var samples = new float[PlaybackControlledWindowsKnownAudioTone.SampleFrames, 2];
        var span = tone.Buffer.Span;
        var peak = 0F;

        for (var frame = 0; frame < samples.GetLength(0); frame++)
        {
            for (var channel = 0; channel < samples.GetLength(1); channel++)
            {
                var offset = ((frame * 2) + channel) * sizeof(float);
                var value = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(
                        span.Slice(offset, sizeof(float))));
                samples[frame, channel] = value;
                Assert.True(float.IsFinite(value));
                Assert.InRange(
                    value,
                    -PlaybackControlledWindowsKnownAudioTone.PeakAmplitude,
                    PlaybackControlledWindowsKnownAudioTone.PeakAmplitude);
                peak = Math.Max(peak, Math.Abs(value));
            }

            Assert.Equal(samples[frame, 0], samples[frame, 1]);
        }

        Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, tone.SampleFormat);
        Assert.Equal(48_000, tone.SampleRate);
        Assert.Equal(2, tone.ChannelCount);
        Assert.Equal(3_840, tone.FrameCount);
        Assert.Equal(30_720, tone.Buffer.Length);
        Assert.Equal(7, tone.Generation);
        Assert.Equal(0F, samples[0, 0]);
        Assert.Equal(0F, samples[PlaybackControlledWindowsKnownAudioTone.SampleFrames - 1, 0]);
        Assert.True(Math.Abs(samples[12, 0]) < Math.Abs(samples[228, 0]));
        Assert.True(Math.Abs(samples[
            PlaybackControlledWindowsKnownAudioTone.SampleFrames - 13, 0]) <
            Math.Abs(samples[
                PlaybackControlledWindowsKnownAudioTone.SampleFrames - 229, 0]));
        Assert.InRange(peak, 0.19F, 0.20F);

        // 1 kHz at 48 kHz repeats every 48 frames outside the fades.
        Assert.InRange(Math.Abs(samples[480, 0] - samples[528, 0]), 0F, 0.000001F);
        Assert.InRange(Math.Abs(samples[492, 0] - samples[540, 0]), 0F, 0.000001F);
    }

    [Fact]
    public async Task Accepted_write_cleans_output_before_runner_and_preserves_borrowed_owners()
    {
        var operations = new FakeOperations();
        await using var probe = CreateProbe(operations);

        var result = await probe.RunAsync();
        var snapshot = probe.Snapshot;

        Assert.Equal(PlaybackControlledWindowsKnownAudioToneProbeResult.Completed, result);
        Assert.Equal(PlaybackControlledWindowsKnownAudioToneProbeEvidence.Accepted, snapshot.Evidence);
        Assert.Equal(1, snapshot.WriteAttempts);
        Assert.Equal(1, snapshot.WritesAcknowledged);
        Assert.Equal(1, snapshot.StartAcknowledged);
        Assert.Equal(0, snapshot.RetainedWouldBlock);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, snapshot.RunnerState);
        Assert.True(snapshot.OutputCleanupCompleted);
        Assert.True(snapshot.BorrowedWindowPreserved);
        Assert.True(snapshot.BorrowedDispatcherPreserved);
        Assert.Equal(
            ["runner:start", "output:init", "write", "output:cleanup", "owners:verify",
                "runner:stop"],
            operations.Log);
        Assert.Equal(1, operations.WriteCount);

        Assert.Equal(
            PlaybackControlledWindowsKnownAudioToneProbeResult.AlreadyRunning,
            await probe.RunAsync());
        Assert.Equal(1, operations.WriteCount);
    }

    [Fact]
    public async Task WouldBlock_is_terminal_without_retry()
    {
        var operations = new FakeOperations
        {
            WriteResult = PlaybackWindowsOutputBufferProcessingResult.WouldBlock,
            NativeEvidence = Evidence(
                attempts: 1,
                acknowledgements: 0,
                frames: 0,
                bytes: 0,
                wouldBlock: 1,
                starts: 0),
        };
        await using var probe = CreateProbe(operations);

        Assert.Equal(
            PlaybackControlledWindowsKnownAudioToneProbeResult.WriteRejected,
            await probe.RunAsync());
        Assert.Equal(1, operations.WriteCount);
        Assert.DoesNotContain("owners:verify", operations.Log);
        Assert.Equal(
            ["runner:start", "output:init", "write", "output:cleanup", "runner:stop"],
            operations.Log);
    }

    [Fact]
    public async Task Missing_start_acknowledgement_fails_closed()
    {
        var operations = new FakeOperations
        {
            NativeEvidence = Evidence(starts: 0),
        };
        await using var probe = CreateProbe(operations);

        Assert.Equal(
            PlaybackControlledWindowsKnownAudioToneProbeResult.EvidenceRejected,
            await probe.RunAsync());
        Assert.Equal(1, operations.WriteCount);
    }

    [Fact]
    public async Task Cancellation_during_hold_cleans_up_without_second_write()
    {
        var operations = new FakeOperations();
        await using var probe = new PlaybackControlledWindowsKnownAudioToneProbe(
            new() { ObservationHoldDuration = TimeSpan.FromSeconds(30) },
            operations);
        using var cancellation = new CancellationTokenSource();

        var run = probe.RunAsync(cancellation.Token).AsTask();
        await operations.Written.Task.WaitAsync(TimeSpan.FromSeconds(1));
        for (var index = 0; index < 1_000 &&
            probe.Snapshot.State !=
                PlaybackControlledWindowsKnownAudioToneProbeState.HoldingObservation;
            index++)
        {
            await Task.Yield();
        }

        cancellation.Cancel();
        Assert.Equal(
            PlaybackControlledWindowsKnownAudioToneProbeResult.Cancelled,
            await run);
        Assert.Equal(1, operations.WriteCount);
        Assert.Equal(
            ["runner:start", "output:init", "write", "output:cleanup", "runner:stop"],
            operations.Log);
    }

    [Fact]
    public void Canonical_record_round_trips_and_rejects_mutation()
    {
        var lines = PlaybackControlledWindowsKnownAudioToneProbeRecordCodec.Create(
            AcceptedSnapshot());

        Assert.True(PlaybackControlledWindowsKnownAudioToneProbeRecordCodec.TryParse(
            lines,
            out var parsed));
        Assert.Equal(3_840, parsed.SampleFrames);
        Assert.Equal(30_720, parsed.PayloadBytes);
        Assert.DoesNotContain(":\\", string.Join('\n', lines), StringComparison.Ordinal);

        var changed = lines.ToArray();
        changed[9] = "Sample-Frames: 3839";
        Assert.False(PlaybackControlledWindowsKnownAudioToneProbeRecordCodec.TryParse(
            changed,
            out _));
    }

    private static PlaybackControlledWindowsKnownAudioToneProbe CreateProbe(
        FakeOperations operations) =>
        new(new() { ObservationHoldDuration = TimeSpan.Zero }, operations);

    private static PlaybackControlledWindowsKnownAudioToneProbeSnapshot AcceptedSnapshot() => new(
        PlaybackControlledWindowsKnownAudioToneProbeState.Completed,
        PlaybackControlledWindowsKnownAudioToneProbeResult.Completed,
        PlaybackControlledWindowsKnownAudioToneProbeEvidence.Accepted,
        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
        48_000,
        2,
        1_000,
        80,
        3_840,
        30_720,
        1,
        1,
        1,
        0,
        PlaybackWindowsPhysicalRunnerState.Stopped,
        true,
        true,
        true);

    private static PlaybackWindowsOutputNativeEvidenceSnapshot Evidence(
        long attempts = 1,
        long acknowledgements = 1,
        long frames = 3_840,
        long bytes = 30_720,
        long wouldBlock = 0,
        long starts = 1) => new(
        PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
        CounterOverflowed: false,
        VideoConfigurationConflict: false,
        PlaybackDecodedVideoPixelFormat.Bgra32,
        VideoWidth: 0,
        VideoHeight: 0,
        VideoStride: 0,
        VideoGeneration: -1,
        VideoPresentAttemptCount: 0,
        VideoPresentAcknowledgementCount: 0,
        VideoAcceptedByteCount: 0,
        VideoWouldBlockCount: 0,
        VideoResizeCount: 0,
        VideoDeviceLostCount: 0,
        AudioConfigurationConflict: false,
        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
        AudioSampleRate: 48_000,
        AudioChannelCount: 2,
        AudioGeneration: 1,
        AudioWriteAttemptCount: attempts,
        AudioWriteAcknowledgementCount: acknowledgements,
        AudioAcceptedFrameCount: frames,
        AudioAcceptedByteCount: bytes,
        AudioWouldBlockCount: wouldBlock,
        AudioStartAcknowledgementCount: starts,
        AudioDeviceLostCount: 0);

    private sealed class FakeOperations : IPlaybackControlledWindowsKnownAudioToneProbeOperations
    {
        private bool _stopped;
        private bool _cleanup;

        public List<string> Log { get; } = [];

        public int WriteCount { get; private set; }

        public TaskCompletionSource Written { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackWindowsOutputBufferProcessingResult WriteResult { get; init; } =
            PlaybackWindowsOutputBufferProcessingResult.Processed;

        public PlaybackWindowsOutputNativeEvidenceSnapshot NativeEvidence { get; init; } =
            Evidence();

        public PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot Snapshot => new(
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
                AudioStarted: WriteResult == PlaybackWindowsOutputBufferProcessingResult.Processed)
            {
                NativeEvidence = NativeEvidence,
            });

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("runner:start");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Started);
        }

        public ValueTask<PlaybackControlledWindowsKnownAudioToneOutputResult>
            InitializeOutputAsync(long generation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(1, generation);
            Log.Add("output:init");
            return ValueTask.FromResult(
                PlaybackControlledWindowsKnownAudioToneOutputResult.Initialized);
        }

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> WriteAsync(
            PlaybackWindowsAudioSampleBuffer samples,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(PlaybackControlledWindowsKnownAudioTone.PayloadBytes,
                samples.Buffer.Length);
            WriteCount++;
            Log.Add("write");
            Written.TrySetResult();
            return ValueTask.FromResult(WriteResult);
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

        public ValueTask<PlaybackControlledWindowsKnownAudioToneProbePreservation>
            VerifyBorrowedOwnersAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("owners:verify");
            return ValueTask.FromResult(
                new PlaybackControlledWindowsKnownAudioToneProbePreservation(
                    _cleanup,
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
    }
}

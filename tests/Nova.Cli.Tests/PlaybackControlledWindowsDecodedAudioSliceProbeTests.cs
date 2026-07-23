using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsDecodedAudioSliceProbeTests
{
    [Fact]
    public async Task Accepted_slice_is_written_once_and_cleanup_precedes_runner_stop()
    {
        var operations = new FakeOperations();
        await using var probe = CreateProbe(operations);

        var result = await probe.RunAsync();
        var snapshot = probe.Snapshot;

        Assert.Equal(
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.Completed,
            result);
        Assert.Equal(
            PlaybackControlledWindowsDecodedAudioSliceProbeEvidence.Accepted,
            snapshot.Evidence);
        Assert.Equal(4, snapshot.Slice.DecodedFramesConsumed);
        Assert.Equal(3_840, snapshot.Slice.OutputSampleFrames);
        Assert.Equal(30_720, snapshot.Slice.PayloadBytes);
        Assert.Equal(1, snapshot.WriteAttempts);
        Assert.Equal(1, snapshot.WritesAcknowledged);
        Assert.Equal(1, snapshot.StartAcknowledged);
        Assert.Equal(0, snapshot.RetainedWouldBlock);
        Assert.Equal(0, snapshot.VideoPresentAttempts);
        Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, snapshot.RunnerState);
        Assert.True(snapshot.OutputCleanupCompleted);
        Assert.True(snapshot.BorrowedWindowPreserved);
        Assert.True(snapshot.BorrowedDispatcherPreserved);
        Assert.Equal(
            ["decode", "runner:start", "output:init", "write", "output:cleanup",
                "owners:verify", "runner:stop"],
            operations.Log);
        Assert.Equal(1, operations.DecodeCount);
        Assert.Equal(1, operations.WriteCount);

        Assert.Equal(
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.AlreadyRunning,
            await probe.RunAsync());
        Assert.Equal(1, operations.WriteCount);
    }

    [Fact]
    public async Task Decode_rejection_never_starts_output_and_partial_payload_is_not_used()
    {
        var operations = new FakeOperations
        {
            Decode = new(
                Completed: false,
                default,
                AcceptedSlice() with
                {
                    Result = PlaybackDecodedAudioSliceCollectorResult.Accepting,
                    OutputSampleFrames = 3_072,
                    PayloadBytes = 24_576,
                    IsComplete = false,
                    PayloadPublished = false,
                }),
        };
        await using var probe = CreateProbe(operations);

        Assert.Equal(
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.DecodeRejected,
            await probe.RunAsync());
        Assert.Equal(["decode"], operations.Log);
        Assert.Equal(0, operations.WriteCount);
    }

    [Fact]
    public async Task WouldBlock_is_terminal_without_retry_or_second_write()
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
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.WriteRejected,
            await probe.RunAsync());
        Assert.Equal(1, operations.WriteCount);
        Assert.Equal(
            ["decode", "runner:start", "output:init", "write", "output:cleanup",
                "runner:stop"],
            operations.Log);
    }

    [Fact]
    public async Task Missing_start_acknowledgement_and_video_present_fail_closed()
    {
        var missingStart = new FakeOperations
        {
            NativeEvidence = Evidence(starts: 0),
        };
        await using (var probe = CreateProbe(missingStart))
        {
            Assert.Equal(
                PlaybackControlledWindowsDecodedAudioSliceProbeResult.EvidenceRejected,
                await probe.RunAsync());
        }

        var videoPresent = new FakeOperations
        {
            NativeEvidence = Evidence(videoPresents: 1),
        };
        await using (var probe = CreateProbe(videoPresent))
        {
            Assert.Equal(
                PlaybackControlledWindowsDecodedAudioSliceProbeResult.EvidenceRejected,
                await probe.RunAsync());
        }
    }

    [Fact]
    public async Task Cancellation_during_hold_cleans_up_without_more_decode_or_write()
    {
        var operations = new FakeOperations();
        await using var probe = new PlaybackControlledWindowsDecodedAudioSliceProbe(
            Options() with { ObservationHoldDuration = TimeSpan.FromSeconds(30) },
            operations);
        using var cancellation = new CancellationTokenSource();

        var run = probe.RunAsync(cancellation.Token).AsTask();
        await operations.Written.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        Assert.Equal(
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.Cancelled,
            await run);
        Assert.Equal(1, operations.DecodeCount);
        Assert.Equal(1, operations.WriteCount);
        Assert.Equal(
            ["decode", "runner:start", "output:init", "write", "output:cleanup",
                "runner:stop"],
            operations.Log);
    }

    [Fact]
    public void Canonical_record_round_trips_and_rejects_every_mutation()
    {
        var lines =
            PlaybackControlledWindowsDecodedAudioSliceProbeRecordCodec.Create(
                AcceptedProbeSnapshot());

        Assert.True(
            PlaybackControlledWindowsDecodedAudioSliceProbeRecordCodec.TryParse(
                lines,
                out var parsed));
        Assert.Equal(4, parsed.Slice.DecodedFramesConsumed);
        Assert.Equal(30_720, parsed.Slice.PayloadBytes);
        Assert.DoesNotContain(":\\", string.Join('\n', lines), StringComparison.Ordinal);

        for (var index = 0; index < lines.Count; index++)
        {
            var changed = lines.ToArray();
            changed[index] += "x";
            Assert.False(
                PlaybackControlledWindowsDecodedAudioSliceProbeRecordCodec.TryParse(
                    changed,
                    out _));
        }
    }

    private static PlaybackControlledWindowsDecodedAudioSliceProbe CreateProbe(
        FakeOperations operations) =>
        new(Options(), operations);

    private static PlaybackControlledWindowsDecodedAudioSliceProbeOptions Options() =>
        new()
        {
            RuntimeDirectory = "external-runtime",
            FixturePath = "external-fixture",
            ObservationHoldDuration = TimeSpan.Zero,
        };

    private static PlaybackDecodedAudioSliceCollectorSnapshot AcceptedSlice() => new(
        PlaybackDecodedAudioSliceCollectorResult.Completed,
        Generation: 0,
        DecodedFramesConsumed: 4,
        FullDecodedFramesCopied: 3,
        FinalFrameSampleFramesCopied: 768,
        OutputSampleFrames: 3_840,
        PayloadBytes: 30_720,
        IsComplete: true,
        PayloadPublished: true);

    private static PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot
        AcceptedProbeSnapshot() => new(
            PlaybackControlledWindowsDecodedAudioSliceProbeState.Completed,
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.Completed,
            PlaybackControlledWindowsDecodedAudioSliceProbeEvidence.Accepted,
            AcceptedSlice(),
            WriteAttempts: 1,
            WritesAcknowledged: 1,
            StartAcknowledged: 1,
            RetainedWouldBlock: 0,
            VideoPresentAttempts: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            OutputCleanupCompleted: true,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true);

    private static PlaybackWindowsOutputNativeEvidenceSnapshot Evidence(
        long attempts = 1,
        long acknowledgements = 1,
        long frames = 3_840,
        long bytes = 30_720,
        long wouldBlock = 0,
        long starts = 1,
        long videoPresents = 0) => new(
        PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
        CounterOverflowed: false,
        VideoConfigurationConflict: false,
        PlaybackDecodedVideoPixelFormat.Bgra32,
        VideoWidth: 0,
        VideoHeight: 0,
        VideoStride: 0,
        VideoGeneration: -1,
        VideoPresentAttemptCount: videoPresents,
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

    private sealed class FakeOperations :
        IPlaybackControlledWindowsDecodedAudioSliceProbeOperations
    {
        private bool _stopped;
        private bool _cleanup;

        public List<string> Log { get; } = [];

        public int DecodeCount { get; private set; }

        public int WriteCount { get; private set; }

        public TaskCompletionSource<bool> Written { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackControlledWindowsDecodedAudioSliceDecode Decode { get; init; } =
            new(
                Completed: true,
                new(
                    PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                    48_000,
                    2,
                    3_840,
                    new byte[30_720],
                    1),
                AcceptedSlice());

        public PlaybackWindowsOutputBufferProcessingResult WriteResult { get; init; } =
            PlaybackWindowsOutputBufferProcessingResult.Processed;

        public PlaybackWindowsOutputNativeEvidenceSnapshot NativeEvidence { get; init; } =
            Evidence();

        public PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot Snapshot => new(
            new(
                _stopped
                    ? PlaybackWindowsPhysicalRunnerState.Stopped
                    : PlaybackWindowsPhysicalRunnerState.Running,
                PlaybackWindowsPhysicalRunnerResult.Started,
                WindowToken: 1,
                OwnerManagedThreadId: _stopped ? 0 : 7,
                PendingOperationCount: 0,
                AcceptedOperationCount: 3,
                CompletedOperationCount: 3,
                CancelledOperationCount: 0,
                RejectedOperationCount: 0,
                HasMessageLoop: !_stopped,
                AdmissionClosed: _stopped),
            new(
                PlaybackWindowsOutputBufferState.Ready,
                PlaybackWindowsOutputBufferState.Ready,
                VideoGeneration: -1,
                AudioGeneration: 1,
                AudioStarted: true)
            {
                NativeEvidence = NativeEvidence,
            });

        public ValueTask<PlaybackControlledWindowsDecodedAudioSliceDecode> DecodeAsync(
            string runtimeDirectory,
            string fixturePath,
            int maximumDecodeOperations,
            int fixturePacketBytes,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DecodeCount++;
            Log.Add("decode");
            return ValueTask.FromResult(Decode);
        }

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
            Log.Add("output:init");
            return ValueTask.FromResult(
                PlaybackControlledWindowsKnownAudioToneOutputResult.Initialized);
        }

        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> WriteAsync(
            PlaybackWindowsAudioSampleBuffer samples,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(3_840, samples.FrameCount);
            Assert.Equal(30_720, samples.Buffer.Length);
            WriteCount++;
            Log.Add("write");
            Written.TrySetResult(true);
            return ValueTask.FromResult(WriteResult);
        }

        public ValueTask CleanupOutputAsync()
        {
            _cleanup = true;
            Log.Add("output:cleanup");
            return ValueTask.CompletedTask;
        }

        public ValueTask<PlaybackControlledWindowsKnownAudioToneProbePreservation>
            VerifyBorrowedOwnersAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("owners:verify");
            return ValueTask.FromResult(new
                PlaybackControlledWindowsKnownAudioToneProbePreservation(
                    _cleanup,
                    WindowPreserved: true,
                    DispatcherPreserved: true));
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync()
        {
            _stopped = true;
            Log.Add("runner:stop");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceCheckpointTests
{
    private static readonly string FixtureSha256 = new('A', 64);
    private static readonly string[] ExpectedSuccessfulLog =
    [
        "runner:start",
        "lifecycle:init",
        "fixture",
        "input:complete",
        "receive:Audio",
        "deliver",
        "receive:Audio",
        "deliver",
        "receive:Audio",
        "deliver",
        "receive:Audio",
        "deliver",
        "snapshot:1",
        "snapshot:2",
        "lifecycle:stop",
        "owners:verify",
        "runner:stop",
        "snapshot:3",
    ];

    [Fact]
    public async Task Completed_audio_run_retains_both_checkpoints_without_extra_operations()
    {
        var holdStart = CreateLifecycle(sequenceBase: 0, paddingBase: 0);
        var preCleanup = CreateLifecycle(sequenceBase: 10, paddingBase: 100);
        var operations = new FakeOperations(holdStart, preCleanup);
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            new()
            {
                Kind = PlaybackControlledWindowsOutputValidationKind.Audio,
                RuntimeDirectory = "C:\\controlled-runtime",
                FixturePath = "C:\\controlled-fixture.flv",
                FixtureSha256 = FixtureSha256,
                MaximumDecodeOperations = 4,
                MaximumDeliveryOperations = 4,
                ObservationHoldDuration = TimeSpan.FromMilliseconds(1),
            },
            operations);

        var result = await composition.RunAsync();
        var snapshot = composition.Snapshot;

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.Completed, result);
        Assert.Equal(
            PlaybackControlledWindowsAudioRenderEvidenceProjection.Capture(holdStart),
            snapshot.HoldStartAudioRenderEvidence);
        Assert.Equal(
            PlaybackControlledWindowsAudioRenderEvidenceProjection.Capture(preCleanup),
            snapshot.PreCleanupAudioRenderEvidence);
        Assert.Equal(3, operations.SnapshotReadCount);
        Assert.Equal(4, operations.ReceiveCount);
        Assert.Equal(4, operations.DeliveryCount);
        Assert.Equal(ExpectedSuccessfulLog, operations.Log);
    }

    [Fact]
    public async Task Rejected_delivery_leaves_checkpoints_absent()
    {
        var operations = new FakeOperations(
            CreateLifecycle(sequenceBase: 0, paddingBase: 0),
            CreateLifecycle(sequenceBase: 10, paddingBase: 100))
        {
            DeliveryResult =
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock,
        };
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            new()
            {
                Kind = PlaybackControlledWindowsOutputValidationKind.Audio,
                RuntimeDirectory = "C:\\controlled-runtime",
                FixturePath = "C:\\controlled-fixture.flv",
                FixtureSha256 = FixtureSha256,
                MaximumDecodeOperations = 4,
                MaximumDeliveryOperations = 4,
            },
            operations);

        var result = await composition.RunAsync();
        var snapshot = composition.Snapshot;

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected, result);
        Assert.Equal(default, snapshot.HoldStartAudioRenderEvidence);
        Assert.Equal(default, snapshot.PreCleanupAudioRenderEvidence);
        Assert.Equal(1, operations.SnapshotReadCount);
        Assert.Equal(1, operations.ReceiveCount);
        Assert.Equal(1, operations.DeliveryCount);
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot CreateLifecycle(
        long sequenceBase,
        int paddingBase)
    {
        var native = new PlaybackWindowsOutputNativeEvidenceSnapshot(
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
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            AudioGeneration: 7,
            AudioWriteAttemptCount: 4,
            AudioWriteAcknowledgementCount: 4,
            AudioAcceptedFrameCount: 4_096,
            AudioAcceptedByteCount: 16_384,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 1,
            AudioDeviceLostCount: 0)
        {
            AudioRenderTrace = CreateAudioRenderTrace(paddingBase),
        };
        return new(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
            DecoderGeneration: 7,
            DiscontinuityGeneration: 0,
            OutputReadyGeneration: 7,
            VideoWidth: 2,
            VideoHeight: 2,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            HasActiveOperation: false,
            PlaybackWindowsOutputProbeResult.Available,
            PlaybackWindowsOutputActivationResult.Activated,
            default,
            default,
            new PlaybackWindowsOutputBufferSnapshot(
                PlaybackWindowsOutputBufferState.Ready,
                PlaybackWindowsOutputBufferState.Ready,
                VideoGeneration: 7,
                AudioGeneration: 7,
                AudioStarted: true)
            {
                NativeEvidence = native,
            })
        {
            DeliveryTrace = CreateDeliveryTrace(sequenceBase),
        };
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot CreateDeliveryTrace(
        long sequenceBase) =>
        new(
            Count: 4,
            Overflowed: false,
            First: Delivery(sequenceBase, TimeSpan.Zero),
            Second: Delivery(sequenceBase + 1, TimeSpan.FromMilliseconds(21)),
            Third: Delivery(sequenceBase + 2, TimeSpan.FromMilliseconds(42)),
            Fourth: Delivery(sequenceBase + 3, TimeSpan.FromMilliseconds(64)));

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation Delivery(
        long sequence,
        TimeSpan presentationOffset) =>
        new(
            Sequence: sequence,
            MediaKind: PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            PresentationTimestampOffsetTicks: presentationOffset.Ticks,
            HasTimingDecision: true,
            TimingResult: PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            Result: PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            Completion: PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);

    private static PlaybackWindowsAudioRenderTraceSnapshot CreateAudioRenderTrace(
        int paddingBase) =>
        new(
            Count: 4,
            Overflowed: false,
            First: Write(paddingBase, startRequested: true),
            Second: Write(paddingBase + 100, startRequested: false),
            Third: Write(paddingBase + 200, startRequested: false),
            Fourth: Write(paddingBase + 300, startRequested: false),
            PreStop: default);

    private static PlaybackWindowsAudioRenderWriteObservation Write(
        int padding,
        bool startRequested) =>
        new(
            RequestedFrameCount: 1024,
            RequestedByteCount: 4096,
            HasCapacityFrameCount: true,
            CapacityFrameCount: 4800,
            HasPaddingFrameCount: true,
            PaddingFrameCount: padding,
            HasAvailableFrameCount: true,
            AvailableFrameCount: 4800 - padding,
            StartRequested: startRequested,
            GetBufferAcknowledged: true,
            ReleaseBufferAcknowledged: true,
            StartAcknowledged: startRequested,
            Result: PlaybackWindowsAudioRenderWriteResult.Processed);

    private sealed class FakeOperations(
        PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot holdStart,
        PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot preCleanup) :
        IPlaybackControlledWindowsPhysicalRunOperations
    {
        public PlaybackApprovedExternalRuntimeWindowsLifecycleResult DeliveryResult { get; init; } =
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered;

        public int SnapshotReadCount { get; private set; }

        public int ReceiveCount { get; private set; }

        public int DeliveryCount { get; private set; }

        public List<string> Log { get; } = [];

        public PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot
        {
            get
            {
                SnapshotReadCount++;
                Log.Add($"snapshot:{SnapshotReadCount}");
                return new(
                    new(
                        PlaybackWindowsPhysicalRunnerState.Running,
                        PlaybackWindowsPhysicalRunnerResult.Started,
                        WindowToken: 7144,
                        OwnerManagedThreadId: 7,
                        PendingOperationCount: 0,
                        AcceptedOperationCount: 1,
                        CompletedOperationCount: 1,
                        CancelledOperationCount: 0,
                        RejectedOperationCount: 0,
                        HasMessageLoop: true,
                        AdmissionClosed: false),
                    SnapshotReadCount == 1 ? holdStart : preCleanup);
            }
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("runner:start");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Started);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
            InitializeLifecycleAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("lifecycle:init");
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized);
        }

        public ValueTask<PcpControlledFlvFixtureReplayReport> ReplayFixtureAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("fixture");
            return ValueTask.FromResult(new PcpControlledFlvFixtureReplayReport(
                PcpControlledFlvFixtureReplayResult.Completed,
                FixtureSha256,
                PayloadByteCount: 128,
                PacketCount: 1));
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> CompleteInputAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("input:complete");
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.EncodedInputFinalized);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind kind,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReceiveCount++;
            Log.Add($"receive:{kind}");
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind kind,
            CancellationToken token) =>
            ReceiveDecodedAsync(kind, token);

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DeliveryCount++;
            Log.Add("deliver");
            return ValueTask.FromResult(DeliveryResult);
        }

        public ValueTask StopLifecycleAsync()
        {
            Log.Add("lifecycle:stop");
            return ValueTask.CompletedTask;
        }

        public ValueTask<PlaybackControlledWindowsPhysicalRunPreservation>
            VerifyBorrowedOwnersAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("owners:verify");
            return ValueTask.FromResult(new PlaybackControlledWindowsPhysicalRunPreservation(
                WindowPreserved: true,
                DispatcherPreserved: true,
                OwnerThreadTeardown: true));
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync()
        {
            Log.Add("runner:stop");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

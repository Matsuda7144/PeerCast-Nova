using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsPhysicalRunCompositionTests
{
    private static readonly string FixtureSha256 = new('A', 64);

    [Fact]
    public async Task Video_run_admits_evidence_and_cleans_lifecycle_before_runner()
    {
        var operations = new FakeOperations(VideoLifecycle());
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Video,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.Completed, result);
        Assert.Equal(PlaybackControlledWindowsPhysicalRunState.Completed, composition.Snapshot.State);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            composition.Snapshot.EvidenceResult);
        Assert.Equal(1, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(1, composition.Snapshot.DeliveredCount);
        Assert.Equal(["runner:start", "lifecycle:init", "fixture", "input:complete",
            "receive:Video", "deliver", "lifecycle:stop", "owners:verify", "runner:stop"],
            operations.Log);
    }

    [Fact]
    public async Task Audio_run_admits_exact_write_and_first_start_evidence()
    {
        var operations = new FakeOperations(AudioLifecycle())
        {
            DeliveryResult = PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
        };
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Audio,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.Completed, result);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            composition.Snapshot.EvidenceResult);
        Assert.True(composition.Snapshot.BorrowedWindowPreserved);
        Assert.True(composition.Snapshot.BorrowedDispatcherPreserved);
    }

    [Fact]
    public async Task Digest_rejection_stops_lifecycle_then_runner_without_decode()
    {
        var operations = new FakeOperations(VideoLifecycle())
        {
            ReplayResult = PcpControlledFlvFixtureReplayResult.DigestMismatch,
        };
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Video,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.FixtureRejected, result);
        Assert.DoesNotContain("receive:Video", operations.Log);
        Assert.True(operations.Log.IndexOf("lifecycle:stop") <
            operations.Log.IndexOf("runner:stop"));
    }

    [Fact]
    public async Task WouldBlock_is_caller_visible_and_is_not_retried()
    {
        var operations = new FakeOperations(VideoLifecycle(wouldBlock: true))
        {
            DeliveryResult =
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock,
        };
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Video,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected, result);
        Assert.Equal(1, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(1, composition.Snapshot.RetainedWouldBlockCount);
        Assert.Equal(1, operations.Log.Count(item => item == "deliver"));
    }

    [Fact]
    public async Task Borrowed_owner_contradiction_rejects_evidence()
    {
        var operations = new FakeOperations(VideoLifecycle())
        {
            Preservation = new(false, true, true),
        };
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Video,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected, result);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.CleanupRejected,
            composition.Snapshot.EvidenceResult);
    }

    [Fact]
    public async Task Invalid_configuration_fails_before_runner_start()
    {
        var operations = new FakeOperations(VideoLifecycle());
        var options = Options(PlaybackControlledWindowsOutputValidationKind.Video) with
        {
            RuntimeDirectory = "relative",
        };
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration, result);
        Assert.Empty(operations.Log);
    }

    [Fact]
    public async Task Decode_limit_is_finite_and_cleanup_still_runs()
    {
        var operations = new FakeOperations(VideoLifecycle())
        {
            ReceiveResult = PlaybackApprovedExternalRuntimeWindowsLifecycleResult.NeedMoreInput,
            DrainResult = PlaybackApprovedExternalRuntimeWindowsLifecycleResult.NeedMoreInput,
        };
        var options = Options(PlaybackControlledWindowsOutputValidationKind.Video) with
        {
            MaximumDecodeOperations = 2,
        };
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DecodeRejected, result);
        Assert.Equal(2, composition.Snapshot.DecodeOperationCount);
        Assert.Contains("lifecycle:stop", operations.Log);
        Assert.Contains("runner:stop", operations.Log);
    }

    private static PlaybackControlledWindowsPhysicalRunComposition Create(
        PlaybackControlledWindowsOutputValidationKind kind,
        FakeOperations operations) => new(Options(kind), operations);

    private static PlaybackControlledWindowsPhysicalRunOptions Options(
        PlaybackControlledWindowsOutputValidationKind kind) => new()
        {
            Kind = kind,
            RuntimeDirectory = "C:\\controlled-runtime",
            FixturePath = "C:\\controlled-fixture.flv",
            FixtureSha256 = FixtureSha256,
            MaximumDecodeOperations = 4,
            MaximumDeliveryOperations = 4,
        };

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot VideoLifecycle(
        bool wouldBlock = false) => Lifecycle(new PlaybackWindowsOutputNativeEvidenceSnapshot(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: 2,
            VideoHeight: 2,
            VideoStride: 8,
            VideoGeneration: 7,
            VideoPresentAttemptCount: 1,
            VideoPresentAcknowledgementCount: wouldBlock ? 0 : 1,
            VideoAcceptedByteCount: wouldBlock ? 0 : 16,
            VideoWouldBlockCount: wouldBlock ? 1 : 0,
            VideoResizeCount: 0,
            VideoDeviceLostCount: 0,
            AudioConfigurationConflict: false,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            AudioSampleRate: 0,
            AudioChannelCount: 0,
            AudioGeneration: -1,
            AudioWriteAttemptCount: 0,
            AudioWriteAcknowledgementCount: 0,
            AudioAcceptedFrameCount: 0,
            AudioAcceptedByteCount: 0,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 0,
            AudioDeviceLostCount: 0));

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot AudioLifecycle() =>
        Lifecycle(new PlaybackWindowsOutputNativeEvidenceSnapshot(
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
            AudioWriteAttemptCount: 1,
            AudioWriteAcknowledgementCount: 1,
            AudioAcceptedFrameCount: 1_024,
            AudioAcceptedByteCount: 4_096,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 1,
            AudioDeviceLostCount: 0));

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot Lifecycle(
        PlaybackWindowsOutputNativeEvidenceSnapshot native) => new(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered,
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
            });

    private sealed class FakeOperations(
        PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot lifecycle) :
        IPlaybackControlledWindowsPhysicalRunOperations
    {
        public List<string> Log { get; } = [];
        public PcpControlledFlvFixtureReplayResult ReplayResult { get; init; } =
            PcpControlledFlvFixtureReplayResult.Completed;
        public PlaybackApprovedExternalRuntimeWindowsLifecycleResult ReceiveResult { get; init; } =
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred;
        public PlaybackApprovedExternalRuntimeWindowsLifecycleResult DrainResult { get; init; } =
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred;
        public PlaybackApprovedExternalRuntimeWindowsLifecycleResult DeliveryResult { get; init; } =
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered;
        public PlaybackControlledWindowsPhysicalRunPreservation Preservation { get; init; } =
            new(true, true, true);

        public PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot => new(
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
            lifecycle);

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
                ReplayResult,
                FixtureSha256,
                PayloadByteCount: 128,
                PacketCount: ReplayResult == PcpControlledFlvFixtureReplayResult.Completed ? 1 : 0));
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
            Log.Add($"receive:{kind}");
            return ValueTask.FromResult(ReceiveResult);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind kind,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add($"drain:{kind}");
            return ValueTask.FromResult(DrainResult);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
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
            return ValueTask.FromResult(Preservation);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync()
        {
            Log.Add("runner:stop");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

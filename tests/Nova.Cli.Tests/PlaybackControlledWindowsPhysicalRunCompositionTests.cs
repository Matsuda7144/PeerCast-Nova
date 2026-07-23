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
    public async Task Audio_run_admits_four_ordinary_writes_and_one_start()
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
        Assert.Equal(4, composition.Snapshot.DecodeOperationCount);
        Assert.Equal(4, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(4, composition.Snapshot.DeliveredCount);
        Assert.Equal(4, operations.Log.Count(item => item == "receive:Audio"));
        Assert.Equal(4, operations.Log.Count(item => item == "deliver"));
        Assert.True(composition.Snapshot.BorrowedWindowPreserved);
        Assert.True(composition.Snapshot.BorrowedDispatcherPreserved);
    }

    [Theory]
    [InlineData(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamEndOfStream)]
    [InlineData(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamAlreadyEnded)]
    public async Task Audio_EOS_drains_two_already_scheduled_frames_to_four_deliveries(
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult endResult)
    {
        var operations = new FakeOperations(AudioLifecycle())
        {
            DeliveryResult = PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
        };
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        operations.ReceiveResults.Enqueue(endResult);
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Audio,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.Completed, result);
        Assert.Equal(3, composition.Snapshot.DecodeOperationCount);
        Assert.Equal(4, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(4, composition.Snapshot.DeliveredCount);
        Assert.Equal(3, operations.Log.Count(item => item == "receive:Audio"));
        Assert.Equal(4, operations.Log.Count(item => item == "deliver"));
        Assert.Empty(operations.ReceiveResults);
    }

    [Fact]
    public async Task Audio_EOS_stops_on_empty_scheduler_without_retry()
    {
        var operations = new FakeOperations(AudioLifecycle());
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamEndOfStream);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Empty);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Audio,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected, result);
        Assert.Equal(3, composition.Snapshot.DecodeOperationCount);
        Assert.Equal(3, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(2, composition.Snapshot.DeliveredCount);
        Assert.Equal(3, operations.Log.Count(item => item == "deliver"));
        Assert.Single(operations.DeliveryResults);
    }

    [Fact]
    public async Task Audio_EOS_WouldBlock_is_terminal_without_retry()
    {
        var operations = new FakeOperations(AudioLifecycle());
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamEndOfStream);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Audio,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected, result);
        Assert.Equal(3, composition.Snapshot.DecodeOperationCount);
        Assert.Equal(3, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(2, composition.Snapshot.DeliveredCount);
        Assert.Equal(1, composition.Snapshot.RetainedWouldBlockCount);
        Assert.Equal(3, operations.Log.Count(item => item == "deliver"));
        Assert.Single(operations.DeliveryResults);
    }

    [Fact]
    public async Task Video_EOS_does_not_drain_scheduler()
    {
        var operations = new FakeOperations(VideoLifecycle());
        operations.ReceiveResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamEndOfStream);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered);
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Video,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected, result);
        Assert.Equal(1, composition.Snapshot.DecodeOperationCount);
        Assert.Equal(0, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(0, composition.Snapshot.DeliveredCount);
        Assert.DoesNotContain("deliver", operations.Log);
        Assert.Single(operations.DeliveryResults);
    }

    [Fact]
    public async Task Audio_WouldBlock_after_first_write_is_terminal_without_retry()
    {
        var operations = new FakeOperations(AudioLifecycle());
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock);
        await using var composition = Create(
            PlaybackControlledWindowsOutputValidationKind.Audio,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected, result);
        Assert.Equal(2, composition.Snapshot.DecodeOperationCount);
        Assert.Equal(2, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(1, composition.Snapshot.DeliveredCount);
        Assert.Equal(1, composition.Snapshot.RetainedWouldBlockCount);
        Assert.Equal(2, operations.Log.Count(item => item == "deliver"));
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(4, 3)]
    public async Task Audio_run_rejects_operation_budget_below_four_before_runner(
        int maximumDecodeOperations,
        int maximumDeliveryOperations)
    {
        var operations = new FakeOperations(AudioLifecycle());
        var options = Options(PlaybackControlledWindowsOutputValidationKind.Audio) with
        {
            MaximumDecodeOperations = maximumDecodeOperations,
            MaximumDeliveryOperations = maximumDeliveryOperations,
        };
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration, result);
        Assert.Empty(operations.Log);
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
            AudioWriteAttemptCount: 4,
            AudioWriteAcknowledgementCount: 4,
            AudioAcceptedFrameCount: 4_096,
            AudioAcceptedByteCount: 16_384,
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
        public Queue<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ReceiveResults
        {
            get;
        } = new();
        public Queue<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainResults
        {
            get;
        } = new();
        public Queue<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliveryResults
        {
            get;
        } = new();
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
            return ValueTask.FromResult(
                ReceiveResults.Count > 0 ? ReceiveResults.Dequeue() : ReceiveResult);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind kind,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add($"drain:{kind}");
            return ValueTask.FromResult(
                DrainResults.Count > 0 ? DrainResults.Dequeue() : DrainResult);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("deliver");
            return ValueTask.FromResult(
                DeliveryResults.Count > 0 ? DeliveryResults.Dequeue() : DeliveryResult);
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

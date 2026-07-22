using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsPhysicalObservationHoldTests
{
    private static readonly string FixtureSha256 = new('A', 64);

    [Fact]
    public async Task Hold_keeps_presented_lifecycle_alive_until_cancellation_then_cleans_up()
    {
        var operations = new FakeOperations();
        var options = Options() with
        {
            ObservationHoldDuration = TimeSpan.FromSeconds(30),
        };
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            operations);
        using var cancellation = new CancellationTokenSource();

        var run = composition.RunAsync(cancellation.Token).AsTask();
        await operations.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        for (var index = 0; index < 1_000 &&
            composition.Snapshot.State != PlaybackControlledWindowsPhysicalRunState.HoldingObservation;
            index++)
        {
            await Task.Yield();
        }

        var stateDuringHold = composition.Snapshot.State;
        var lifecycleStoppedDuringHold = operations.Log.Contains("lifecycle:stop");
        var runnerStoppedDuringHold = operations.Log.Contains("runner:stop");
        cancellation.Cancel();
        var result = await run;

        Assert.Equal(
            PlaybackControlledWindowsPhysicalRunState.HoldingObservation,
            stateDuringHold);
        Assert.False(lifecycleStoppedDuringHold);
        Assert.False(runnerStoppedDuringHold);
        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.Cancelled, result);
        Assert.Equal(PlaybackControlledWindowsPhysicalRunState.Faulted, composition.Snapshot.State);
        Assert.Equal(1, composition.Snapshot.DeliveryAttemptCount);
        Assert.Equal(1, composition.Snapshot.DeliveredCount);
        Assert.Equal(
            ["runner:start", "lifecycle:init", "fixture", "input:complete",
                "receive:Video", "deliver", "lifecycle:stop", "runner:stop"],
            operations.Log);
    }

    [Fact]
    public async Task Hold_above_bound_fails_before_runner_start()
    {
        var operations = new FakeOperations();
        var options = Options() with
        {
            ObservationHoldDuration =
                PlaybackControlledWindowsPhysicalRunOptions.MaximumObservationHoldDuration +
                TimeSpan.FromMilliseconds(1),
        };
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            operations);

        var result = await composition.RunAsync();

        Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration, result);
        Assert.Empty(operations.Log);
    }

    private static PlaybackControlledWindowsPhysicalRunOptions Options() => new()
    {
        Kind = PlaybackControlledWindowsOutputValidationKind.Video,
        RuntimeDirectory = "C:\\controlled-runtime",
        FixturePath = "C:\\controlled-fixture.flv",
        FixtureSha256 = FixtureSha256,
        MaximumDecodeOperations = 4,
        MaximumDeliveryOperations = 4,
    };

    private sealed class FakeOperations : IPlaybackControlledWindowsPhysicalRunOperations
    {
        public List<string> Log { get; } = [];
        public TaskCompletionSource Delivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            Lifecycle());

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
            Log.Add($"receive:{kind}");
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind kind,
            CancellationToken token) =>
            throw new InvalidOperationException("Drain is not expected.");

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("deliver");
            Delivered.TrySetResult();
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered);
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

        private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot Lifecycle() => new(
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
                NativeEvidence = new PlaybackWindowsOutputNativeEvidenceSnapshot(
                    PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
                    CounterOverflowed: false,
                    VideoConfigurationConflict: false,
                    PlaybackDecodedVideoPixelFormat.Bgra32,
                    VideoWidth: 2,
                    VideoHeight: 2,
                    VideoStride: 8,
                    VideoGeneration: 7,
                    VideoPresentAttemptCount: 1,
                    VideoPresentAcknowledgementCount: 1,
                    VideoAcceptedByteCount: 16,
                    VideoWouldBlockCount: 0,
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
                    AudioDeviceLostCount: 0),
            });
    }
}

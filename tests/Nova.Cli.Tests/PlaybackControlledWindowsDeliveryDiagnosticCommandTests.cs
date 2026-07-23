using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsDeliveryDiagnosticCommandTests
{
    private static readonly string FixtureSha256 = new('A', 64);

    [Fact]
    public async Task Command_retains_exact_outer_delivery_results_without_acceptance_claim()
    {
        var output = new StringWriter();
        var operations = new FakeOperations();
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult
                .RetainedAudioRecoveryRequired);

        var result = await PlaybackControlledWindowsDeliveryDiagnosticCommand.RunAsync(
            Arguments("audio"),
            output,
            _ => operations);

        Assert.Equal(
            PlaybackControlledWindowsDeliveryDiagnosticCommand
                .DiagnosticRecordProducedWithRunFailureExitCode,
            result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected,
            result.RunResult);
        Assert.True(
            PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.TryParse(
                output.ToString(),
                out var record));
        Assert.NotNull(record);
        Assert.Equal(
            PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected,
            record.RunResult);
        Assert.Equal(2, record.DecodeOperationCount);
        Assert.Equal(2, record.DeliveryAttemptCount);
        Assert.Equal(1, record.DeliveredCount);
        Assert.Equal(0, record.RetainedWouldBlockCount);
        Assert.Equal(
            [
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult
                    .RetainedAudioRecoveryRequired,
            ],
            record.DeliveryResults);
        Assert.False(record.DeliveryResultOverflowed);
        Assert.False(record.ExternalBinaryOrPayloadPublished);
        Assert.True(record.PhysicalRunsExecutedByThisCommand);
        Assert.False(record.AudioAcceptanceClaimed);
    }

    [Theory]
    [InlineData(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait)]
    [InlineData(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ClockNotStarted)]
    [InlineData(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ResetRequired)]
    [InlineData(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected)]
    public async Task Command_preserves_each_fixed_terminal_delivery_result(
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult terminalResult)
    {
        var output = new StringWriter();
        var operations = new FakeOperations();
        operations.DeliveryResults.Enqueue(terminalResult);

        var result = await PlaybackControlledWindowsDeliveryDiagnosticCommand.RunAsync(
            Arguments("audio"),
            output,
            _ => operations);

        Assert.Equal(
            PlaybackControlledWindowsDeliveryDiagnosticCommand
                .DiagnosticRecordProducedWithRunFailureExitCode,
            result.ExitCode);
        Assert.True(
            PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.TryParse(
                output.ToString(),
                out var record));
        Assert.NotNull(record);
        Assert.Equal([terminalResult], record.DeliveryResults);
    }

    [Fact]
    public async Task Command_counts_would_block_without_resuming_or_hiding_it()
    {
        var output = new StringWriter();
        var operations = new FakeOperations();
        operations.DeliveryResults.Enqueue(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock);

        await PlaybackControlledWindowsDeliveryDiagnosticCommand.RunAsync(
            Arguments("audio"),
            output,
            _ => operations);

        Assert.True(
            PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.TryParse(
                output.ToString(),
                out var record));
        Assert.NotNull(record);
        Assert.Equal(1, record.RetainedWouldBlockCount);
        Assert.Equal(
            [PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock],
            record.DeliveryResults);
        Assert.Single(operations.Log, item => item == "deliver");
    }

    [Fact]
    public async Task Command_rejects_video_without_starting_operations()
    {
        var output = new StringWriter();
        var operationsCreated = false;

        var result = await PlaybackControlledWindowsDeliveryDiagnosticCommand.RunAsync(
            Arguments("video"),
            output,
            _ =>
            {
                operationsCreated = true;
                return new FakeOperations();
            });

        Assert.Equal(
            PlaybackControlledWindowsDeliveryDiagnosticCommand.InvalidConfigurationExitCode,
            result.ExitCode);
        Assert.False(operationsCreated);
        Assert.Equal("Result: InvalidConfiguration" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Codec_rejects_unlisted_trailing_delivery_result()
    {
        var record = PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.Create(
            Snapshot(
                PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected,
                attempts: 1,
                delivered: 0,
                retainedWouldBlock: 0),
            FixtureSha256,
            [PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ClockNotStarted],
            deliveryResultOverflowed: false);
        var serialized = PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.Serialize(record)
            .Replace(
                "Delivery-Result-2: None",
                "Delivery-Result-2: OutputRejected",
                StringComparison.Ordinal);

        Assert.False(
            PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.TryParse(
                serialized,
                out _));
    }

    private static string[] Arguments(string kind) =>
    [
        PlaybackControlledWindowsDeliveryDiagnosticCommand.Argument,
        "--runtime-directory",
        "C:\\controlled-runtime",
        "--controlled-fixture",
        "C:\\controlled-fixture.flv",
        "--fixture-sha256",
        FixtureSha256,
        "--output-kind",
        kind,
        "--max-decode-operations",
        "4",
        "--max-delivery-operations",
        "4",
        "--fixture-packet-bytes",
        "65536",
        "--observation-hold-ms",
        "0",
    ];

    private static PlaybackControlledWindowsPhysicalRunSnapshot Snapshot(
        PlaybackControlledWindowsPhysicalRunResult result,
        int attempts,
        int delivered,
        int retainedWouldBlock) => new(
            PlaybackControlledWindowsPhysicalRunState.Faulted,
            result,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: attempts,
            DeliveryAttemptCount: attempts,
            DeliveredCount: delivered,
            RetainedWouldBlockCount: retainedWouldBlock,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            PlaybackControlledWindowsOutputEvidenceResult.None,
            BorrowedWindowPreserved: false,
            BorrowedDispatcherPreserved: false);

    private sealed class FakeOperations : IPlaybackControlledWindowsPhysicalRunOperations
    {
        private PlaybackWindowsPhysicalRunnerState _runnerState =
            PlaybackWindowsPhysicalRunnerState.Created;
        private PlaybackApprovedExternalRuntimeWindowsLifecycleState _lifecycleState =
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Created;

        public Queue<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliveryResults
        {
            get;
        } = new();

        public List<string> Log { get; } = [];

        public PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot => new(
            new(
                _runnerState,
                _runnerState == PlaybackWindowsPhysicalRunnerState.Stopped
                    ? PlaybackWindowsPhysicalRunnerResult.Stopped
                    : PlaybackWindowsPhysicalRunnerResult.Started,
                WindowToken: 7144,
                OwnerManagedThreadId: 7,
                PendingOperationCount: 0,
                AcceptedOperationCount: 1,
                CompletedOperationCount: 1,
                CancelledOperationCount: 0,
                RejectedOperationCount: 0,
                HasMessageLoop: _runnerState == PlaybackWindowsPhysicalRunnerState.Running,
                AdmissionClosed: _runnerState == PlaybackWindowsPhysicalRunnerState.Stopped),
            new(
                _lifecycleState,
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.None,
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
                default));

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("runner:start");
            _runnerState = PlaybackWindowsPhysicalRunnerState.Running;
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Started);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
            InitializeLifecycleAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("lifecycle:init");
            _lifecycleState = PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready;
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
                PayloadByteCount: 66_939,
                PacketCount: 2));
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
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add($"drain:{kind}");
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("deliver");
            return ValueTask.FromResult(
                DeliveryResults.Count > 0
                    ? DeliveryResults.Dequeue()
                    : PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered);
        }

        public ValueTask StopLifecycleAsync()
        {
            Log.Add("lifecycle:stop");
            _lifecycleState = PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed;
            return ValueTask.CompletedTask;
        }

        public ValueTask<PlaybackControlledWindowsPhysicalRunPreservation>
            VerifyBorrowedOwnersAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Log.Add("owners:verify");
            return ValueTask.FromResult(
                new PlaybackControlledWindowsPhysicalRunPreservation(true, true, true));
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync()
        {
            Log.Add("runner:stop");
            _runnerState = PlaybackWindowsPhysicalRunnerState.Stopped;
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

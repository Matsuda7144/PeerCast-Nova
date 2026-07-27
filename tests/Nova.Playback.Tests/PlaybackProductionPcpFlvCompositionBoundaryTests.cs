using static PeerCast.Nova.Playback.Tests.PlaybackPcpFlvAwaitedDecodeCompositionTestSupport;
using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackProductionPcpFlvCompositionBoundaryTests
{
    [Fact]
    public async Task Pending_candidate_fails_after_description_without_accepting_packet()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator
        {
            CreationResult =
                PlaybackProductionDecoderGenerationFactoryResult.LicenseReviewPending,
        };
        await using var boundary = CreateBoundary(creator, output.Output);

        var exception = await Assert.ThrowsAsync<PlaybackProductionPcpFlvCompositionException>(
            () => boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default).AsTask());

        Assert.Equal(PlaybackProductionPcpFlvCompositionResult.LicenseReviewPending, exception.Result);
        Assert.Equal(1, creator.CreateCount);
        Assert.Empty(creator.Generations);
        Assert.Equal(0, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(PlaybackProductionPcpFlvCompositionState.Faulted, boundary.Snapshot.State);
        Assert.True(creator.IsDisposed);
    }

    [Fact]
    public async Task Bootstrap_and_interleaved_packets_create_one_exact_generation()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);

        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        await boundary.ConsumeAsync(
            CreatePacket(1, ConcatForTest(BuildVideoNaluTag(), BuildAudioRawTag())),
            default);

        var generation = Assert.Single(creator.Generations);
        Assert.Equal(2, generation.PacketOperations.SubmitCount);
        Assert.Equal(2, boundary.Snapshot.AcceptedPacketCount);
        Assert.True(boundary.Snapshot.HasDecoderGeneration);
        Assert.Equal(0, boundary.Snapshot.DecoderGeneration);
        Assert.Same(generation.Source, generation.Generation.SourceSession);
        Assert.Equal(PlaybackProductionPcpFlvCompositionResult.PacketAccepted, boundary.Snapshot.Result);
    }

    [Fact]
    public async Task Pending_submission_relief_transfers_before_one_retry()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var generation = Assert.Single(creator.Generations);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        generation.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);

        await boundary.ConsumeAsync(CreatePacket(1, BuildVideoNaluTag()), default);

        Assert.Equal(1, generation.Root.TransferCount);
        Assert.Equal(1, generation.PacketOperations.RetryCount);
        Assert.Equal(1, generation.Root.RetainedFrameCount);
        Assert.Equal(1, boundary.Snapshot.TransferredFrameCount);
        Assert.False(boundary.Snapshot.HasActiveDecodedCompletion);
        Assert.False(boundary.Snapshot.HasActiveSubmission);
        Assert.False(boundary.Snapshot.HasReliefRetry);
    }

    [Fact]
    public async Task Ordinary_receive_transfers_one_completion_outside_packet_callback()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var generation = Assert.Single(creator.Generations);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));

        var result = await boundary.ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind.Video);

        Assert.Equal(PlaybackProductionPcpFlvCompositionResult.FrameTransferred, result);
        Assert.Equal(1, generation.Root.TransferCount);
        Assert.Equal(1, generation.Root.RetainedFrameCount);
        Assert.Equal(1, boundary.Snapshot.TransferredFrameCount);
    }

    [Fact]
    public async Task Caller_driven_delivery_retains_then_commits_one_item()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var generation = Assert.Single(creator.Generations);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.FrameTransferred,
            await boundary.ReceiveDecodedAsync(PlaybackBoundedDemuxMediaKind.Video));
        generation.Root.DeliveryResults.Enqueue(
            PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock);
        generation.Root.DeliveryResults.Enqueue(
            PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered);

        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.RetainedWouldBlock,
            await boundary.DeliverOneAsync());
        Assert.Equal(1, generation.Root.RetainedFrameCount);
        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.VideoDelivered,
            await boundary.DeliverOneAsync());

        Assert.Equal(0, generation.Root.RetainedFrameCount);
        Assert.Equal(1, boundary.Snapshot.DeliveredFrameCount);
    }

    [Fact]
    public async Task Scheduler_admission_rejection_is_terminal_without_retry()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var generation = Assert.Single(creator.Generations);
        generation.Root.TransferResults.Enqueue(
            PlaybackDecodedCompletionSchedulerIngressResult.EnqueueRejected);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));

        var exception = await Assert.ThrowsAsync<PlaybackProductionPcpFlvCompositionException>(
            () => boundary.ReceiveDecodedAsync(
                PlaybackBoundedDemuxMediaKind.Video).AsTask());

        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.SchedulerAdmissionRejected,
            exception.Result);
        Assert.Equal(PlaybackProductionPcpFlvCompositionState.Faulted, boundary.Snapshot.State);
        Assert.Equal(0, generation.PacketOperations.RetryCount);
        Assert.True(generation.Root.IsDisposed);
    }

    [Fact]
    public async Task Explicit_finalization_and_independent_drain_reach_decoders_drained()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var generation = Assert.Single(creator.Generations);

        await boundary.CompleteInputAsync();

        Assert.Equal(
            PlaybackProductionPcpFlvCompositionState.EncodedInputFinalized,
            boundary.Snapshot.State);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));
        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.StreamEndOfStream,
            await boundary.DrainDecodedAsync(PlaybackBoundedDemuxMediaKind.Video));
        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.StreamEndOfStream,
            await boundary.DrainDecodedAsync(PlaybackBoundedDemuxMediaKind.Audio));

        Assert.Equal(
            PlaybackProductionPcpFlvCompositionState.DecodersDrained,
            boundary.Snapshot.State);
        Assert.True(boundary.Snapshot.VideoEndOfStream);
        Assert.True(boundary.Snapshot.AudioEndOfStream);
    }

    [Fact]
    public async Task Discontinuity_stops_old_generation_before_replacement_creation()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var first = Assert.Single(creator.Generations);

        await boundary.ConsumeAsync(
            CreatePacket(1, BuildBootstrap(), discontinuity: true),
            default);

        Assert.Equal(2, creator.Generations.Count);
        var second = creator.Generations[1];
        Assert.True(first.Root.IsDisposed);
        Assert.NotSame(first.Source, second.Source);
        Assert.Equal(1, second.Generation.DecoderGeneration);
        Assert.Equal(1, second.Generation.DiscontinuityGeneration);
        Assert.True(
            creator.Log.IndexOf("stop:end:0") < creator.Log.IndexOf("create:1:1"));
        Assert.Equal(2, boundary.Snapshot.AcceptedPacketCount);
    }

    [Fact]
    public async Task Cancellation_after_old_stop_never_revives_or_partially_commits()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        await using var boundary = CreateBoundary(creator, output.Output);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var first = Assert.Single(creator.Generations);
        first.Root.StopRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var pending = boundary.ConsumeAsync(
            CreatePacket(1, BuildBootstrap(), discontinuity: true),
            cancellation.Token).AsTask();
        await first.Root.StopEntered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        first.Root.StopRelease.TrySetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, creator.CreateCount);
        Assert.Single(creator.Generations);
        Assert.True(first.Root.IsDisposed);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(PlaybackProductionPcpFlvCompositionState.Faulted, boundary.Snapshot.State);
        Assert.Equal(PlaybackProductionPcpFlvCompositionResult.Cancelled, boundary.Snapshot.Result);
    }

    [Fact]
    public async Task Stop_disposes_owned_generation_and_factory_but_not_borrowed_output()
    {
        using var output = new OutputHarness();
        var creator = new FakeCreator();
        var reporter = new RecordingStageReporter();
        await using var boundary = CreateBoundary(creator, output.Output, reporter);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);
        var generation = Assert.Single(creator.Generations);

        await boundary.StopAsync();

        Assert.True(generation.Root.IsDisposed);
        Assert.True(creator.IsDisposed);
        Assert.Equal(PlaybackProductionPcpFlvCompositionState.Disposed, boundary.Snapshot.State);
        Assert.NotEqual(
            PlaybackWindowsOutputBufferState.Disposed,
            output.Output.Snapshot.VideoState);
        Assert.NotEqual(
            PlaybackWindowsOutputBufferState.Disposed,
            output.Output.Snapshot.AudioState);
        Assert.Equal(
            [
                PlaybackWindowsAudioNativeStage.ProductionGenerationCleanupEntering,
                PlaybackWindowsAudioNativeStage.ProductionGenerationCleanupCompleted,
                PlaybackWindowsAudioNativeStage.ProductionParserCleanupEntering,
                PlaybackWindowsAudioNativeStage.ProductionParserCleanupCompleted,
                PlaybackWindowsAudioNativeStage.ProductionFactoryCleanupEntering,
                PlaybackWindowsAudioNativeStage.ProductionFactoryCleanupCompleted,
            ],
            reporter.Stages);
    }

    private static PlaybackProductionPcpFlvCompositionBoundary CreateBoundary(
        FakeCreator creator,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        IPlaybackWindowsAudioNativeStageReporter? reporter = null) =>
        new(
            ChannelId,
            SessionId,
            "C:\\verified-runtime",
            output,
            creator,
            reporter);

    private sealed class RecordingStageReporter : IPlaybackWindowsAudioNativeStageReporter
    {
        public List<PlaybackWindowsAudioNativeStage> Stages { get; } = [];

        public void Report(PlaybackWindowsAudioNativeStage stage) => Stages.Add(stage);
    }

    private static byte[] ConcatForTest(params byte[][] values)
    {
        var result = new byte[values.Sum(static value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result.AsSpan(offset));
            offset += value.Length;
        }

        return result;
    }

    private sealed class FakeCreator : IPlaybackProductionDecoderGenerationCreator
    {
        public PlaybackProductionDecoderGenerationFactoryResult CreationResult { get; set; } =
            PlaybackProductionDecoderGenerationFactoryResult.Created;

        public List<GenerationHarness> Generations { get; } = [];

        public List<string> Log { get; } = [];

        public int CreateCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
            string runtimeDirectory,
            PlaybackBoundedDemuxSession? sourceSession,
            PlaybackWindowsOutputBufferProcessingBoundary? output,
            int decoderGeneration,
            int discontinuityGeneration,
            CancellationToken cancellationToken)
        {
            CreateCount = checked(CreateCount + 1);
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("C:\\verified-runtime", runtimeDirectory);
            Assert.NotNull(output);
            if (CreationResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return ValueTask.FromResult(
                    new PlaybackProductionDecoderGenerationCreation(CreationResult, null));
            }

            var source = Assert.IsType<PlaybackBoundedDemuxSession>(sourceSession);
            Log.Add($"create:{decoderGeneration}:{discontinuityGeneration}");
            var packetOperations = new FakePacketOperations(Log);
            var receiveOperations = new FakeReceiveOperations();
            var nativeSession = new PlaybackBoundedFlvNativeDecoderSession(
                source,
                source.Video.StreamId,
                source.Audio.StreamId,
                source.Video.TimeBase,
                source.Audio.TimeBase,
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264, "video", Log),
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac, "audio", Log));
            var decode = new PlaybackSourceBoundOneFrameDecodeBoundary(
                source,
                nativeSession,
                decoderGeneration,
                discontinuityGeneration,
                options: null,
                packetOperations,
                receiveOperations);
            var root = new FakeRoot(
                source,
                decoderGeneration,
                discontinuityGeneration,
                decode,
                Log);
            var generation = new PlaybackProductionDecoderGeneration(
                source,
                decoderGeneration,
                discontinuityGeneration,
                root);
            Generations.Add(new(
                source,
                packetOperations,
                receiveOperations,
                root,
                generation));
            return ValueTask.FromResult(
                new PlaybackProductionDecoderGenerationCreation(
                    PlaybackProductionDecoderGenerationFactoryResult.Created,
                    generation));
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                Log.Add("factory");
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record GenerationHarness(
        PlaybackBoundedDemuxSession Source,
        FakePacketOperations PacketOperations,
        FakeReceiveOperations ReceiveOperations,
        FakeRoot Root,
        PlaybackProductionDecoderGeneration Generation);

    private sealed class FakeRoot :
        IPlaybackProductionDecoderGenerationRoot,
        IPlaybackProductionDecoderGenerationIngressForwarder
    {
        private readonly PlaybackSourceBoundOneFrameDecodeBoundary _decode;
        private readonly List<string> _log;
        private readonly List<(PlaybackDecoderFfmpegManagedFrame Frame,
            PlaybackBoundedDemuxMediaKind MediaKind)> _retained = [];
        private readonly TaskCompletionSource<bool> _stopEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private long _nextVideoSequence;
        private long _nextAudioSequence;
        private long _nextSchedulerSequence;
        private long _transferCount;
        private bool _stopping;

        public FakeRoot(
            PlaybackBoundedDemuxSession sourceSession,
            int decoderGeneration,
            int discontinuityGeneration,
            PlaybackSourceBoundOneFrameDecodeBoundary decode,
            List<string> log)
        {
            SourceSession = sourceSession;
            DecoderGeneration = decoderGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
            _decode = decode;
            _log = log;
        }

        public PlaybackBoundedDemuxSession SourceSession { get; }

        public int DecoderGeneration { get; }

        public int DiscontinuityGeneration { get; }

        public bool IsDisposed { get; private set; }

        public int TransferCount => checked((int)_transferCount);

        public int RetainedFrameCount => _retained.Count;

        public Queue<PlaybackDecodedCompletionSchedulerIngressResult> TransferResults { get; } = [];

        public Queue<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliveryResults { get; } = [];

        public Queue<PlaybackGenerationMatchedSchedulerResetResult> ResetResults { get; } = [];

        public Task StopEntered => _stopEntered.Task;

        public TaskCompletionSource<bool>? StopRelease { get; set; }

        public PlaybackSourceBoundOneFrameDecodeSnapshot? DecodeSnapshot =>
            IsDisposed ? null : _decode.Snapshot;

        public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot
        {
            get
            {
                var ingress = new PlaybackDecodedCompletionSchedulerIngressSnapshot(
                    IsDisposed
                        ? PlaybackDecodedCompletionSchedulerIngressState.Disposed
                        : PlaybackDecodedCompletionSchedulerIngressState.Accepting,
                    IsDisposed
                        ? PlaybackDecodedCompletionSchedulerIngressResult.Disposed
                        : PlaybackDecodedCompletionSchedulerIngressResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    _nextVideoSequence,
                    _nextAudioSequence,
                    _nextSchedulerSequence,
                    _transferCount,
                    DiscontinuityGeneration > 0 && _transferCount == 0,
                    new(
                        _retained.Count,
                        0,
                        0,
                        _retained.Sum(static item => ManagedBytes(item.Frame))));
                return new(
                    IsDisposed
                        ? PlaybackGenerationMatchedSchedulerDeliveryState.Disposed
                        : PlaybackGenerationMatchedSchedulerDeliveryState.Accepting,
                    IsDisposed
                        ? PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed
                        : PlaybackGenerationMatchedSchedulerDeliveryResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    true,
                    DiscontinuityGeneration > 0 && _transferCount == 0,
                    false,
                    0,
                    false,
                    ingress);
            }
        }

        public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
            ReferenceEquals(SourceSession, sourceSession);

        public PlaybackFiniteOneEventPacketSubmissionResult BeginSubmission(
            PlaybackBoundedDemuxReadCompletion? sourceCompletion,
            CancellationToken cancellationToken) =>
            IsDisposed
                ? PlaybackFiniteOneEventPacketSubmissionResult.Disposed
                : _decode.BeginSubmission(sourceCompletion, cancellationToken);

        public PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
            PlaybackBoundedDemuxMediaKind mediaKind,
            int streamId,
            PlaybackSourceBoundOneFrameDecodeMode mode,
            CancellationToken cancellationToken,
            PlaybackSourceBoundOneFrameDecodeBudget budget) =>
            IsDisposed
                ? new(PlaybackSourceBoundOneFrameDecodeResult.Disposed, null, default)
                : _decode.ReceiveOne(
                    SourceSession,
                    DecoderGeneration,
                    mediaKind,
                    streamId,
                    mode,
                    cancellationToken,
                    budget);

        public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
            PlaybackSourceBoundDecodedFrameCompletion? completion,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                completion?.Dispose();
                return PlaybackDecodedCompletionSchedulerIngressResult.Disposed;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PlaybackDecodedCompletionSchedulerIngressResult.Cancelled;
            }

            var selected = TransferResults.Count == 0
                ? PlaybackDecodedCompletionSchedulerIngressResult.Transferred
                : TransferResults.Dequeue();
            if (completion is null)
            {
                return PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion;
            }

            if (selected == PlaybackDecodedCompletionSchedulerIngressResult.Cancelled)
            {
                return selected;
            }

            var mediaKind = completion.MediaKind;
            var sequence = completion.Sequence;
            if (!completion.TryDetachFrame(out var frame) || frame is null)
            {
                completion.Dispose();
                return PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion;
            }

            completion.Dispose();
            if (selected != PlaybackDecodedCompletionSchedulerIngressResult.Transferred)
            {
                frame.Dispose();
                return selected;
            }

            _retained.Add((frame, mediaKind));
            _transferCount++;
            _nextSchedulerSequence++;
            if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
            {
                _nextVideoSequence = checked(sequence + 1);
            }
            else
            {
                _nextAudioSequence = checked(sequence + 1);
            }

            return selected;
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return ValueTask.FromResult(
                    PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromResult(
                    PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
            }

            var result = DeliveryResults.Count == 0
                ? PlaybackGenerationMatchedSchedulerDeliveryResult.Empty
                : DeliveryResults.Dequeue();
            if (result is PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered or
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered or
                PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDropped)
            {
                Assert.NotEmpty(_retained);
                var item = _retained[0];
                _retained.RemoveAt(0);
                item.Frame.Dispose();
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return ValueTask.FromResult(
                    PlaybackGenerationMatchedSchedulerResetResult.Disposed);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromResult(
                    PlaybackGenerationMatchedSchedulerResetResult.Cancelled);
            }

            return ValueTask.FromResult(ResetResults.Count == 0
                ? PlaybackGenerationMatchedSchedulerResetResult.Reset
                : ResetResults.Dequeue());
        }

        public async ValueTask StopAsync()
        {
            if (IsDisposed || _stopping)
            {
                return;
            }

            _stopping = true;
            _log.Add($"stop:start:{DecoderGeneration}");
            _stopEntered.TrySetResult(true);
            if (StopRelease is not null)
            {
                await StopRelease.Task.ConfigureAwait(false);
            }

            DisposeCore();
            _log.Add($"stop:end:{DecoderGeneration}");
        }

        public void Dispose()
        {
            DisposeCore();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private void DisposeCore()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            foreach (var item in _retained)
            {
                item.Frame.Dispose();
            }

            _retained.Clear();
            _decode.Dispose();
        }

        private static long ManagedBytes(PlaybackDecoderFfmpegManagedFrame frame) =>
            frame.Planes.Sum(static plane => (long)plane.Buffer.Length);
    }

    private sealed class FakeContextOwner(
        PlaybackDecoderFfmpegCodecId codecId,
        string name,
        List<string> log) : IPlaybackBoundedFlvNativeContextOwner
    {
        private int _isDisposed;

        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                log.Add(name);
            }
        }
    }

    private sealed class OutputHarness : IDisposable
    {
        public OutputHarness()
        {
            Activation = new PlaybackWindowsOutputActivationBoundary();
            Binding = new PlaybackWindowsOutputResourceBindingBoundary(Activation);
            Initialization = new PlaybackWindowsOutputStreamInitializationBoundary(
                Activation,
                Binding);
            Output = new PlaybackWindowsOutputBufferProcessingBoundary(
                Activation,
                Binding,
                Initialization);
        }

        public PlaybackWindowsOutputActivationBoundary Activation { get; }

        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; }

        public PlaybackWindowsOutputStreamInitializationBoundary Initialization { get; }

        public PlaybackWindowsOutputBufferProcessingBoundary Output { get; }

        public void Dispose()
        {
            Output.Dispose();
            Initialization.Dispose();
            Binding.Dispose();
            Activation.Dispose();
        }
    }
}

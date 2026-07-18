namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsOutputSchedulerAdapterBoundaryTests
{
    [Fact]
    public async Task DispatchesOneVideoAndConsumesOnlyOnSuccess()
    {
        var item = Item(VideoFrame(), PlaybackSchedulerOutputKind.Video);
        var source = new RecordingSource(item);
        var dispatcher = new RecordingDispatcher();
        using var adapter = Create(source, dispatcher);

        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed,
            await adapter.PumpOneAsync(default));
        Assert.True(item.Consumed);
        Assert.Equal(1, dispatcher.VideoCalls);
    }

    [Theory]
    [InlineData(PlaybackDecoderFfmpegFrameFormat.AudioSigned16)]
    [InlineData(PlaybackDecoderFfmpegFrameFormat.AudioFloat32)]
    public async Task DispatchesOneInterleavedAudioItem(
        PlaybackDecoderFfmpegFrameFormat format)
    {
        var item = Item(AudioFrame(format), PlaybackSchedulerOutputKind.Audio);
        var dispatcher = new RecordingDispatcher();
        using var adapter = Create(new RecordingSource(item), dispatcher);
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed,
            await adapter.PumpOneAsync(default));
        Assert.True(item.Consumed);
        Assert.Equal(1, dispatcher.AudioCalls);
    }

    [Fact]
    public async Task AlternatesPreferenceWhenBothKindsAreReady()
    {
        var source = new RecordingSource(
            Item(VideoFrame(), PlaybackSchedulerOutputKind.Video),
            Item(AudioFrame(), PlaybackSchedulerOutputKind.Audio),
            Item(VideoFrame(), PlaybackSchedulerOutputKind.Video));
        using var adapter = Create(source, new RecordingDispatcher());

        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed,
            await adapter.PumpOneAsync(default));
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed,
            await adapter.PumpOneAsync(default));
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed,
            await adapter.PumpOneAsync(default));
        Assert.Equal([PlaybackSchedulerOutputKind.Video, PlaybackSchedulerOutputKind.Audio,
            PlaybackSchedulerOutputKind.Video], source.Preferences);
    }

    [Fact]
    public async Task EmptyQueueHasFixedResult()
    {
        using var adapter = Create(new RecordingSource(), new RecordingDispatcher());
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.Empty,
            await adapter.PumpOneAsync(default));
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.WouldBlock,
        PlaybackWindowsOutputSchedulerAdapterResult.WouldBlock)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.ResizeRequired,
        PlaybackWindowsOutputSchedulerAdapterResult.ResizeRequired)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.DeviceLost,
        PlaybackWindowsOutputSchedulerAdapterResult.DeviceLost)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.InitializationRequired,
        PlaybackWindowsOutputSchedulerAdapterResult.InitializationRequired)]
    public async Task RetryableResultsRetainSchedulerItem(
        PlaybackWindowsOutputBufferProcessingResult processorResult,
        PlaybackWindowsOutputSchedulerAdapterResult expected)
    {
        var item = Item(VideoFrame(), PlaybackSchedulerOutputKind.Video);
        var dispatcher = new RecordingDispatcher { VideoResult = processorResult };
        using var adapter = Create(new RecordingSource(item), dispatcher);
        Assert.Equal(expected, await adapter.PumpOneAsync(default));
        Assert.False(item.Consumed);
        Assert.False(item.Frame.IsDisposed);
    }

    [Fact]
    public async Task InvalidLayoutGenerationAndUndefinedValuesFailClosedAndRetain()
    {
        var invalid = Item(VideoFrame(width: 2), PlaybackSchedulerOutputKind.Video);
        using var adapter = Create(new RecordingSource(invalid), new RecordingDispatcher());
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem,
            await adapter.PumpOneAsync(default));
        Assert.False(invalid.Consumed);

        var undefined = Item(VideoFrame(), (PlaybackSchedulerOutputKind)99);
        using var undefinedAdapter = Create(new RecordingSource(undefined), new RecordingDispatcher());
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.InvalidItem,
            await undefinedAdapter.PumpOneAsync(default));

        var generationDispatcher = new RecordingDispatcher();
        using var generation = Create(new RecordingSource(
            Item(VideoFrame(), PlaybackSchedulerOutputKind.Video)), generationDispatcher);
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResetResult.Reset,
            await generation.ResetAsync(8, 12, default));
        Assert.Equal(8, generation.Snapshot.VideoGeneration);
        Assert.Equal(12, generation.Snapshot.AudioGeneration);
        Assert.Equal(["audio:12", "video:8"], generationDispatcher.Resets);
    }

    [Fact]
    public async Task UndefinedSourceOutputAndExceptionsFailClosed()
    {
        using var undefined = Create(new UndefinedSource(), new RecordingDispatcher());
        using var throwing = Create(new ThrowingSource(), new RecordingDispatcher());
        using var undefinedOutput = Create(new RecordingSource(
            Item(VideoFrame(), PlaybackSchedulerOutputKind.Video)),
            new RecordingDispatcher
            { VideoResult = (PlaybackWindowsOutputBufferProcessingResult)99 });
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.Failure,
            await undefined.PumpOneAsync(default));
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.Failure,
            await throwing.PumpOneAsync(default));
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.Failure,
            await undefinedOutput.PumpOneAsync(default));
    }

    [Fact]
    public async Task CancellationAndNonWindowsAvoidDispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        using var cancelled = Create(new RecordingSource(), dispatcher);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.Cancelled,
            await cancelled.PumpOneAsync(cancellation.Token));

        using var unsupported = new PlaybackWindowsOutputSchedulerAdapterBoundary(
            new RecordingSource(Item(VideoFrame(), PlaybackSchedulerOutputKind.Video)),
            dispatcher, 7, 11, static () => false);
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.InvalidEnvironment,
            await unsupported.PumpOneAsync(default));
        Assert.Equal(0, dispatcher.VideoCalls);
    }

    [Fact]
    public async Task RepeatedResetAndDisposeAreIdempotentAndWaitForInflightPump()
    {
        var dispatcher = new RecordingDispatcher { BlockVideo = true };
        var adapter = Create(new RecordingSource(
            Item(VideoFrame(), PlaybackSchedulerOutputKind.Video)), dispatcher);
        var pump = Task.Run(async () => await adapter.PumpOneAsync(default));
        await dispatcher.Entered.Task;
        var dispose = Task.Run(adapter.Dispose);
        Assert.False(dispose.IsCompleted);
        dispatcher.Release.TrySetResult();
        await Task.WhenAll(pump, dispose);
        adapter.Dispose();
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.Disposed,
            await adapter.PumpOneAsync(default));
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResetResult.Disposed,
            await adapter.ResetAsync(8, 12, default));
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputBufferResetResult.Failure,
        PlaybackWindowsOutputSchedulerAdapterResetResult.Failure)]
    [InlineData(PlaybackWindowsOutputBufferResetResult.Cancelled,
        PlaybackWindowsOutputSchedulerAdapterResetResult.Cancelled)]
    [InlineData(PlaybackWindowsOutputBufferResetResult.Disposed,
        PlaybackWindowsOutputSchedulerAdapterResetResult.Disposed)]
    public async Task VideoResetFailureRollsAudioBackAndKeepsOldGeneration(
        PlaybackWindowsOutputBufferResetResult videoResult,
        PlaybackWindowsOutputSchedulerAdapterResetResult expected)
    {
        var item = Item(AudioFrame(), PlaybackSchedulerOutputKind.Audio);
        var dispatcher = new RecordingDispatcher();
        dispatcher.VideoResetResults.Enqueue(videoResult);
        using var adapter = Create(new RecordingSource(item), dispatcher);

        Assert.Equal(expected, await adapter.ResetAsync(8, 12, default));
        Assert.Equal(new(7, 11, true,
            PlaybackWindowsOutputSchedulerAdapterState.Ready), adapter.Snapshot);
        Assert.Equal(["audio:12", "video:8", "audio:11"], dispatcher.Resets);
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed,
            await adapter.PumpOneAsync(default));
        Assert.Equal([11L], dispatcher.AudioGenerations);
    }

    [Fact]
    public async Task RollbackFailureEntersFixedFaultRetainsItemAndExplicitResetRecovers()
    {
        var item = Item(AudioFrame(), PlaybackSchedulerOutputKind.Audio);
        var source = new RecordingSource(item);
        var dispatcher = new RecordingDispatcher();
        dispatcher.AudioResetResults.Enqueue(PlaybackWindowsOutputBufferResetResult.Reset);
        dispatcher.AudioResetResults.Enqueue(PlaybackWindowsOutputBufferResetResult.Failure);
        dispatcher.VideoResetResults.Enqueue(PlaybackWindowsOutputBufferResetResult.Failure);
        using var adapter = Create(source, dispatcher);

        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResetResult.PartialResetFault,
            await adapter.ResetAsync(8, 12, default));
        Assert.Equal(new(7, 12, true,
            PlaybackWindowsOutputSchedulerAdapterState.ResetFaulted), adapter.Snapshot);
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.ResetRequired,
            await adapter.PumpOneAsync(default));
        Assert.False(item.Consumed);
        Assert.False(item.Frame.IsDisposed);

        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResetResult.Reset,
            await adapter.ResetAsync(9, 13, default));
        Assert.Equal(new(9, 13, true,
            PlaybackWindowsOutputSchedulerAdapterState.Ready), adapter.Snapshot);
        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed,
            await adapter.PumpOneAsync(default));
        Assert.Equal([13L], dispatcher.AudioGenerations);
    }

    [Fact]
    public async Task ProductionSchedulerSeamAcknowledgesWithoutOwningScheduler()
    {
        var policy = new PlaybackPolicyClockBoundary(
            new FixedClock(), new PlaybackPolicyOptions());
        using var scheduler = new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions(), policy);
        var frame = VideoFrame();
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(frame, 1, PlaybackDecodedFrameDiscontinuityKind.None));
        using var adapter = new PlaybackWindowsOutputSchedulerAdapterBoundary(
            new BoundedPlaybackWindowsOutputSchedulerSource(scheduler),
            new RecordingDispatcher(), 7, 11, static () => true);

        Assert.Equal(PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed,
            await adapter.PumpOneAsync(default));
        Assert.Equal(0, scheduler.GetSnapshot().QueueLength);
        Assert.True(frame.IsDisposed);
        adapter.Dispose();
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(AudioFrame(), 2, PlaybackDecodedFrameDiscontinuityKind.None));
    }

    private static PlaybackWindowsOutputSchedulerAdapterBoundary Create(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackWindowsOutputBufferDispatcher dispatcher) =>
        new(source, dispatcher, 7, 11, static () => true);

    private static RecordingReservation Item(PlaybackDecoderFfmpegManagedFrame frame,
        PlaybackSchedulerOutputKind kind) => new(frame, kind);

    private static PlaybackDecoderFfmpegManagedFrame VideoFrame(int width = 1) => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            width, 1, 0, 0, 0,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 4, 1, 4, new byte[4])]);

    private static PlaybackDecoderFfmpegManagedFrame AudioFrame(
        PlaybackDecoderFfmpegFrameFormat format = PlaybackDecoderFfmpegFrameFormat.AudioSigned16)
    {
        var bytes = format == PlaybackDecoderFfmpegFrameFormat.AudioSigned16 ? 2 : 4;
        return new(new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Audio, format, 0, 0, 48_000, 2, 2,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
            [new(0, 2 * 2 * bytes, 1, 2 * 2 * bytes, new byte[2 * 2 * bytes])]);
    }

    private sealed class RecordingSource(params RecordingReservation[] items) :
        IPlaybackWindowsOutputSchedulerSource
    {
        public List<PlaybackSchedulerOutputKind> Preferences { get; } = [];
        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind preferredKind, CancellationToken cancellationToken)
        {
            Preferences.Add(preferredKind);
            var item = items.FirstOrDefault(value => !value.Consumed && value.Kind == preferredKind) ??
                items.FirstOrDefault(value => !value.Consumed);
            return ValueTask.FromResult(item is null
                ? new PlaybackWindowsOutputSchedulerTake(PlaybackWindowsOutputSchedulerTakeResult.Empty, null)
                : new PlaybackWindowsOutputSchedulerTake(PlaybackWindowsOutputSchedulerTakeResult.Reserved, item));
        }
    }

    private sealed class RecordingReservation(
        PlaybackDecoderFfmpegManagedFrame frame, PlaybackSchedulerOutputKind kind) :
        IPlaybackWindowsOutputSchedulerReservation
    {
        public PlaybackSchedulerOutputKind Kind { get; } = kind;
        public PlaybackDecoderFfmpegManagedFrame Frame { get; } = frame;
        public long Sequence => 1;
        public PlaybackDecodedFrameDiscontinuityKind Discontinuity => PlaybackDecodedFrameDiscontinuityKind.None;
        public bool Consumed { get; private set; }
        public void Complete(bool consume) => Consumed = consume;
        public void Dispose() { }
    }

    private sealed class RecordingDispatcher : IPlaybackWindowsOutputBufferDispatcher
    {
        public PlaybackWindowsOutputBufferProcessingResult VideoResult { get; init; } = PlaybackWindowsOutputBufferProcessingResult.Processed;
        public int VideoCalls { get; private set; }
        public int AudioCalls { get; private set; }
        public bool BlockVideo { get; init; }
        public List<string> Resets { get; } = [];
        public List<long> AudioGenerations { get; } = [];
        public Queue<PlaybackWindowsOutputBufferResetResult> VideoResetResults { get; } = [];
        public Queue<PlaybackWindowsOutputBufferResetResult> AudioResetResults { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
            PlaybackWindowsVideoFrameBuffer frame, CancellationToken cancellationToken)
        { VideoCalls++; Entered.TrySetResult(); if (BlockVideo) await Release.Task; return VideoResult; }
        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
            PlaybackWindowsAudioSampleBuffer samples, CancellationToken cancellationToken)
        { AudioCalls++; AudioGenerations.Add(samples.Generation); return ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed); }
        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
            long generation, CancellationToken cancellationToken)
        { Resets.Add($"video:{generation}"); return ValueTask.FromResult(VideoResetResults.TryDequeue(out var result) ? result : PlaybackWindowsOutputBufferResetResult.Reset); }
        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
            long generation, CancellationToken cancellationToken)
        { Resets.Add($"audio:{generation}"); return ValueTask.FromResult(AudioResetResults.TryDequeue(out var result) ? result : PlaybackWindowsOutputBufferResetResult.Reset); }
    }

    private sealed class UndefinedSource : IPlaybackWindowsOutputSchedulerSource
    {
        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PlaybackWindowsOutputSchedulerTake(
                (PlaybackWindowsOutputSchedulerTakeResult)99, null));
    }

    private sealed class ThrowingSource : IPlaybackWindowsOutputSchedulerSource
    {
        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind kind, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class FixedClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }
}

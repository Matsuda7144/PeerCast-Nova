namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackTimedSchedulerDispatchBoundaryTests
{
    private static readonly PlaybackTimedSchedulerDispatchOptions Options = new()
    {
        MaximumDecisionCount = 3,
        MaximumTotalWait = TimeSpan.FromMilliseconds(100),
        MinimumDelay = TimeSpan.FromMilliseconds(1),
        MaximumSingleDelay = TimeSpan.FromMilliseconds(50),
    };

    [Theory]
    [InlineData((int)PlaybackSchedulerOutputKind.Video,
        PlaybackTimedSchedulerDispatchResult.VideoProcessed)]
    [InlineData((int)PlaybackSchedulerOutputKind.Audio,
        PlaybackTimedSchedulerDispatchResult.AudioProcessed)]
    public async Task DispatchOneAsync_ProcessNowConsumesOnlyAfterSuccessfulDispatch(
        int kindValue,
        PlaybackTimedSchedulerDispatchResult expected)
    {
        var kind = (PlaybackSchedulerOutputKind)kindValue;
        var reservation = Item(kind);
        var source = new RecordingSource(reservation);
        var dispatcher = new RecordingDispatcher();
        var timing = Timing(Decision(PlaybackAvTimingDecisionResult.ProcessNow));
        using var boundary = Create(source, dispatcher, timing);

        Assert.Equal(expected, await boundary.DispatchOneAsync(Options));
        Assert.True(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
        Assert.Single(timing.Items);
        Assert.Equal(kind == PlaybackSchedulerOutputKind.Video ? 7 : 11,
            timing.Items[0].Generation);
    }

    [Fact]
    public async Task DispatchOneAsync_WaitDelaysExactlyThenRedecidesSameReservation()
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var timing = Timing(
            Decision(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20)),
            Decision(PlaybackAvTimingDecisionResult.ProcessNow));
        var delay = new RecordingDelay();
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), timing, delay);

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.VideoProcessed,
            await boundary.DispatchOneAsync(Options));
        Assert.Equal([TimeSpan.FromMilliseconds(20)], delay.Durations);
        Assert.Equal(2, timing.Items.Count);
        Assert.Equal(timing.Items[0], timing.Items[1]);
        Assert.True(reservation.Consumed);
    }

    [Theory]
    [InlineData(1, 100, 50)]
    [InlineData(3, 100, 10)]
    public async Task DispatchOneAsync_WaitBoundsRetainItem(
        int maximumDecisions,
        int maximumTotalWaitMilliseconds,
        int maximumSingleDelayMilliseconds)
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var timing = Timing(
            Decision(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20)),
            Decision(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20)),
            Decision(PlaybackAvTimingDecisionResult.ProcessNow));
        var delay = new RecordingDelay();
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), timing, delay);
        var options = Options with
        {
            MaximumDecisionCount = maximumDecisions,
            MaximumTotalWait = TimeSpan.FromMilliseconds(maximumTotalWaitMilliseconds),
            MaximumSingleDelay = TimeSpan.FromMilliseconds(maximumSingleDelayMilliseconds),
        };

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.WaitLimitReached,
            await boundary.DispatchOneAsync(options));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Fact]
    public async Task DispatchOneAsync_MaximumTotalWaitRetainsItemBeforeExcessDelay()
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var timing = Timing(
            Decision(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(6)),
            Decision(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(6)));
        var delay = new RecordingDelay();
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), timing, delay);
        var options = Options with
        {
            MaximumTotalWait = TimeSpan.FromMilliseconds(10),
            MaximumSingleDelay = TimeSpan.FromMilliseconds(10),
        };

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.WaitLimitReached,
            await boundary.DispatchOneAsync(options));
        Assert.Equal([TimeSpan.FromMilliseconds(6)], delay.Durations);
        Assert.False(reservation.Consumed);
    }

    [Theory]
    [InlineData(PlaybackAvTimingDecisionResult.VideoLate,
        PlaybackTimedSchedulerDispatchResult.VideoLate)]
    [InlineData(PlaybackAvTimingDecisionResult.AudioLate,
        PlaybackTimedSchedulerDispatchResult.AudioLate)]
    [InlineData(PlaybackAvTimingDecisionResult.DiscontinuityResetRequired,
        PlaybackTimedSchedulerDispatchResult.DiscontinuityResetRequired)]
    [InlineData(PlaybackAvTimingDecisionResult.GenerationMismatch,
        PlaybackTimedSchedulerDispatchResult.GenerationMismatch)]
    [InlineData(PlaybackAvTimingDecisionResult.ClockNotStarted,
        PlaybackTimedSchedulerDispatchResult.ClockNotStarted)]
    public async Task DispatchOneAsync_NonProcessDecisionRetainsItem(
        PlaybackAvTimingDecisionResult decision,
        PlaybackTimedSchedulerDispatchResult expected)
    {
        var reservation = Item(decision == PlaybackAvTimingDecisionResult.AudioLate
            ? PlaybackSchedulerOutputKind.Audio : PlaybackSchedulerOutputKind.Video);
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), Timing(Decision(decision)));

        Assert.Equal(expected, await boundary.DispatchOneAsync(Options));
        Assert.False(reservation.Consumed);
        Assert.Equal(1, reservation.CompleteCalls);
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.WouldBlock,
        PlaybackTimedSchedulerDispatchResult.WouldBlock)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.ResizeRequired,
        PlaybackTimedSchedulerDispatchResult.ResizeRequired)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.DeviceLost,
        PlaybackTimedSchedulerDispatchResult.DeviceLost)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.InitializationRequired,
        PlaybackTimedSchedulerDispatchResult.InitializationRequired)]
    [InlineData(PlaybackWindowsOutputBufferProcessingResult.BindingRequired,
        PlaybackTimedSchedulerDispatchResult.BindingRequired)]
    public async Task DispatchOneAsync_RetryableDispatchResultRetainsItem(
        PlaybackWindowsOutputBufferProcessingResult dispatchResult,
        PlaybackTimedSchedulerDispatchResult expected)
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var dispatcher = new RecordingDispatcher { VideoResult = dispatchResult };
        using var boundary = Create(new RecordingSource(reservation), dispatcher,
            Timing(Decision(PlaybackAvTimingDecisionResult.ProcessNow)));

        Assert.Equal(expected, await boundary.DispatchOneAsync(Options));
        Assert.False(reservation.Consumed);
    }

    [Fact]
    public async Task DispatchOneAsync_EmptyDoesNotCreateReservation()
    {
        using var boundary = Create(new RecordingSource(), new RecordingDispatcher(),
            Timing(Decision(PlaybackAvTimingDecisionResult.ProcessNow)));
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.Empty,
            await boundary.DispatchOneAsync(Options));
    }

    [Fact]
    public async Task DispatchOneAsync_SuccessAloneAdvancesFairnessPreference()
    {
        var video = Item(PlaybackSchedulerOutputKind.Video);
        var audio = Item(PlaybackSchedulerOutputKind.Audio);
        var source = new RecordingSource(video, audio);
        using var boundary = Create(source, new RecordingDispatcher(), Timing(
            Decision(PlaybackAvTimingDecisionResult.ProcessNow),
            Decision(PlaybackAvTimingDecisionResult.ProcessNow)));

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.VideoProcessed,
            await boundary.DispatchOneAsync(Options));
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.AudioProcessed,
            await boundary.DispatchOneAsync(Options));
        Assert.Equal([PlaybackSchedulerOutputKind.Video, PlaybackSchedulerOutputKind.Audio],
            source.Preferences);
    }

    [Fact]
    public async Task DispatchOneAsync_CancellationInterruptsWaitAndRetainsItem()
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var delay = new BlockingDelay();
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), Timing(Decision(
                PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20))), delay);
        using var cancellation = new CancellationTokenSource();
        var dispatch = boundary.DispatchOneAsync(Options, cancellation.Token).AsTask();
        await delay.Entered.Task;
        cancellation.Cancel();

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.Cancelled, await dispatch);
        Assert.False(reservation.Consumed);
    }

    [Fact]
    public async Task DispatchOneAsync_ConcurrentCallIsRejected()
    {
        var delay = new BlockingDelay();
        using var boundary = Create(new RecordingSource(Item(PlaybackSchedulerOutputKind.Video)),
            new RecordingDispatcher(), Timing(Decision(
                PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20))), delay);
        var first = boundary.DispatchOneAsync(Options).AsTask();
        await delay.Entered.Task;

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.AlreadyRunning,
            await boundary.DispatchOneAsync(Options));
        delay.Release.SetResult();
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.WaitLimitReached, await first);
    }

    [Fact]
    public async Task ResetAsync_CancelsWaitThenDelegatesTransactionalReset()
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var delay = new BlockingDelay();
        var timing = Timing(Decision(
            PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20)));
        timing.ResetResults.Enqueue(PlaybackAvTimingResetResult.Reset);
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), timing, delay);
        var dispatch = boundary.DispatchOneAsync(Options).AsTask();
        await delay.Entered.Task;

        Assert.Equal(PlaybackTimedSchedulerResetResult.Reset,
            await boundary.ResetAsync(13, 17));
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.Cancelled, await dispatch);
        Assert.False(reservation.Consumed);
        Assert.Equal(new PlaybackTimedSchedulerDispatchSnapshot(13, 17, true,
            PlaybackTimedSchedulerDispatchState.Ready), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_PartialFaultRequiresExplicitRecovery()
    {
        var timing = Timing(Decision(PlaybackAvTimingDecisionResult.ProcessNow));
        timing.ResetResults.Enqueue(PlaybackAvTimingResetResult.PartialResetFault);
        timing.ResetResults.Enqueue(PlaybackAvTimingResetResult.Reset);
        var source = new RecordingSource(Item(PlaybackSchedulerOutputKind.Video));
        using var boundary = Create(source, new RecordingDispatcher(), timing);

        Assert.Equal(PlaybackTimedSchedulerResetResult.PartialResetFault,
            await boundary.ResetAsync(13, 17));
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.DiscontinuityResetRequired,
            await boundary.DispatchOneAsync(Options));
        Assert.Equal(PlaybackTimedSchedulerResetResult.Reset,
            await boundary.ResetAsync(19, 23));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Dispose_CancelsWaitWaitsOperationAndDoesNotOwnLowerBoundaries()
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var source = new RecordingSource(reservation);
        var delay = new BlockingDelay();
        var timing = Timing(Decision(
            PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20)));
        var boundary = Create(source, new RecordingDispatcher(), timing, delay);
        var dispatch = boundary.DispatchOneAsync(Options).AsTask();
        await delay.Entered.Task;
        var dispose = Task.Run(boundary.Dispose);

        await Task.WhenAll(dispatch, dispose);
        var dispatchResult = await dispatch;
        boundary.Dispose();
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.Cancelled, dispatchResult);
        Assert.False(reservation.Consumed);
        Assert.False(source.Disposed);
        Assert.False(timing.Disposed);
        Assert.Equal(PlaybackTimedSchedulerDispatchState.Disposed, boundary.Snapshot.State);
    }

    [Fact]
    public async Task DispatchOneAsync_NonWindowsDoesNotReserveOrTouchNativeDispatcher()
    {
        var source = new RecordingSource(Item(PlaybackSchedulerOutputKind.Video));
        var dispatcher = new RecordingDispatcher();
        using var boundary = new PlaybackTimedSchedulerDispatchBoundary(source, dispatcher,
            Timing(Decision(PlaybackAvTimingDecisionResult.ProcessNow)),
            new RecordingDelay(), 7, 11, static () => false);

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.InvalidEnvironment,
            await boundary.DispatchOneAsync(Options));
        Assert.Equal(0, source.Calls);
        Assert.Equal(0, dispatcher.VideoCalls);
    }

    [Fact]
    public async Task ProductionReservationSeamConsumesOnceWithoutOwningScheduler()
    {
        var policy = new PlaybackPolicyClockBoundary(new FixedClock(), new PlaybackPolicyOptions());
        using var scheduler = new BoundedPlaybackScheduler(new PlaybackSchedulerOptions(), policy);
        var frame = VideoFrame();
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(frame, 1, PlaybackDecodedFrameDiscontinuityKind.None));
        using var boundary = Create(new BoundedPlaybackWindowsOutputSchedulerSource(scheduler),
            new RecordingDispatcher(), Timing(Decision(PlaybackAvTimingDecisionResult.ProcessNow)));

        Assert.Equal(PlaybackTimedSchedulerDispatchResult.VideoProcessed,
            await boundary.DispatchOneAsync(Options));
        Assert.True(frame.IsDisposed);
        Assert.Equal(0, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(AudioFrame(), 2, PlaybackDecodedFrameDiscontinuityKind.None));
    }

    [Fact]
    public async Task InvalidOptionsAndUndefinedDecisionFailClosedWithoutConsumption()
    {
        var reservation = Item(PlaybackSchedulerOutputKind.Video);
        var timing = Timing(Decision((PlaybackAvTimingDecisionResult)99));
        using var boundary = Create(new RecordingSource(reservation),
            new RecordingDispatcher(), timing);
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.InvalidConfiguration,
            await boundary.DispatchOneAsync(Options with { MaximumDecisionCount = 0 }));
        Assert.Equal(PlaybackTimedSchedulerDispatchResult.Failure,
            await boundary.DispatchOneAsync(Options));
        Assert.False(reservation.Consumed);
    }

    private static PlaybackTimedSchedulerDispatchBoundary Create(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackWindowsOutputBufferDispatcher dispatcher,
        RecordingTiming timing,
        IPlaybackTimedSchedulerDelay? delay = null) =>
        new(source, dispatcher, timing, delay ?? new RecordingDelay(),
            7, 11, static () => true);

    private static RecordingTiming Timing(params PlaybackAvTimingDecision[] decisions) =>
        new(decisions);

    private static PlaybackAvTimingDecision Decision(
        PlaybackAvTimingDecisionResult result,
        TimeSpan wait = default) => new(result, wait);

    private static RecordingReservation Item(PlaybackSchedulerOutputKind kind) =>
        new(kind == PlaybackSchedulerOutputKind.Video ? VideoFrame() : AudioFrame(), kind);

    private static PlaybackDecoderFfmpegManagedFrame VideoFrame() => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            1, 1, 0, 0, 0,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 4, 1, 4, new byte[4])]);

    private static PlaybackDecoderFfmpegManagedFrame AudioFrame() => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            0, 0, 48_000, 2, 2,
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.Zero),
            PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20))),
        [new(0, 8, 1, 8, new byte[8])]);

    private sealed class RecordingSource(params RecordingReservation[] reservations) :
        IPlaybackWindowsOutputSchedulerSource
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public List<PlaybackSchedulerOutputKind> Preferences { get; } = [];
        public ValueTask<PlaybackWindowsOutputSchedulerTake> ReserveAsync(
            PlaybackSchedulerOutputKind preferredKind, CancellationToken cancellationToken)
        {
            Calls++;
            Preferences.Add(preferredKind);
            var reservation = reservations.FirstOrDefault(value => !value.Consumed &&
                value.Kind == preferredKind) ?? reservations.FirstOrDefault(value => !value.Consumed);
            return ValueTask.FromResult(reservation is null
                ? new PlaybackWindowsOutputSchedulerTake(PlaybackWindowsOutputSchedulerTakeResult.Empty, null)
                : new PlaybackWindowsOutputSchedulerTake(
                    PlaybackWindowsOutputSchedulerTakeResult.Reserved, reservation));
        }
    }

    private sealed class RecordingReservation(
        PlaybackDecoderFfmpegManagedFrame frame,
        PlaybackSchedulerOutputKind kind) : IPlaybackWindowsOutputSchedulerReservation
    {
        public PlaybackSchedulerOutputKind Kind { get; } = kind;
        public PlaybackDecoderFfmpegManagedFrame Frame { get; } = frame;
        public long Sequence => 1;
        public PlaybackDecodedFrameDiscontinuityKind Discontinuity =>
            PlaybackDecodedFrameDiscontinuityKind.None;
        public bool Consumed { get; private set; }
        public bool Completed { get; private set; }
        public int CompleteCalls { get; private set; }
        public void Complete(bool consume)
        {
            CompleteCalls++;
            Completed = true;
            Consumed = consume;
        }
        public void Dispose() { }
    }

    private sealed class RecordingDispatcher : IPlaybackWindowsOutputBufferDispatcher
    {
        public PlaybackWindowsOutputBufferProcessingResult VideoResult { get; init; } =
            PlaybackWindowsOutputBufferProcessingResult.Processed;
        public int VideoCalls { get; private set; }
        public int AudioCalls { get; private set; }
        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
            PlaybackWindowsVideoFrameBuffer frame, CancellationToken cancellationToken)
        { VideoCalls++; return ValueTask.FromResult(VideoResult); }
        public ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
            PlaybackWindowsAudioSampleBuffer samples, CancellationToken cancellationToken)
        { AudioCalls++; return ValueTask.FromResult(PlaybackWindowsOutputBufferProcessingResult.Processed); }
        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoAsync(
            long generation, CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferResetResult.Reset);
        public ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioAsync(
            long generation, CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsOutputBufferResetResult.Reset);
    }

    private sealed class RecordingTiming(params PlaybackAvTimingDecision[] decisions) :
        IPlaybackTimedSchedulerTimingDecider
    {
        private readonly Queue<PlaybackAvTimingDecision> _decisions = new(decisions);
        public List<PlaybackAvTimingItem> Items { get; } = [];
        public Queue<PlaybackAvTimingResetResult> ResetResults { get; } = [];
        public bool Disposed { get; private set; }
        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            return ValueTask.FromResult(_decisions.Count == 0
                ? Decision(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromMilliseconds(20))
                : _decisions.Dequeue());
        }
        public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
            long videoGeneration, long audioGeneration, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ResetResults.TryDequeue(out var result)
                ? result : PlaybackAvTimingResetResult.Reset);
    }

    private sealed class RecordingDelay : IPlaybackTimedSchedulerDelay
    {
        public List<TimeSpan> Durations { get; } = [];
        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        { Durations.Add(duration); return ValueTask.CompletedTask; }
    }

    private sealed class BlockingDelay : IPlaybackTimedSchedulerDelay
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FixedClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }
}

namespace PeerCast.Nova.Playback.Tests;

public sealed class BoundedPlaybackSchedulerTests
{
    private static readonly string[] RequiredFrameSymbols =
        ["av_frame_alloc", "av_frame_free", "avcodec_receive_frame"];

    [Fact]
    public async Task RunOneAsync_PreservesFifoAcrossAudioAndVideoAndOwnsFrames()
    {
        using var owners = new TestOwners();
        var first = AudioFrame(0);
        var second = VideoFrame(20);
        using var scheduler = Scheduler([Ms(0), Ms(20)]);
        var sink = new RecordingSink();
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, scheduler.Enqueue(first, 1, None));
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, scheduler.Enqueue(second, 2, None));

        Assert.Equal(PlaybackSchedulerRunResult.Delivered, await Run(scheduler, owners, sink));
        Assert.Equal(PlaybackSchedulerRunResult.Delivered, await Run(scheduler, owners, sink));

        Assert.Equal(new long[] { 1, 2 }, sink.Sequences);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Equal(new PlaybackSchedulerSnapshot(0, 0, 2), scheduler.GetSnapshot());
    }

    [Fact]
    public void Enqueue_UsesOneCombinedBoundedCapacityForAudioAndVideo()
    {
        using var scheduler = Scheduler([TimeSpan.Zero], capacity: 2);
        var audio = AudioFrame(0);
        var video = VideoFrame(1);
        var extra = AudioFrame(2);

        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, scheduler.Enqueue(audio, 1, None));
        Assert.Equal(PlaybackSchedulerEnqueueResult.Enqueued, scheduler.Enqueue(video, 2, None));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.DroppedIncoming,
            scheduler.Enqueue(extra, 3, None));

        Assert.Equal(2, scheduler.GetSnapshot().QueueLength);
        Assert.True(extra.IsDisposed);
    }

    [Fact]
    public void Enqueue_DropOldestDisposesOldestAndAcceptsIncoming()
    {
        using var scheduler = Scheduler(
            [TimeSpan.Zero],
            capacity: 1,
            fullPolicy: PlaybackSchedulerFullPolicy.DropOldest);
        var oldest = AudioFrame(0);
        var incoming = AudioFrame(1);
        scheduler.Enqueue(oldest, 1, None);

        var result = scheduler.Enqueue(incoming, 2, None);

        Assert.Equal(PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest, result);
        Assert.True(oldest.IsDisposed);
        Assert.False(incoming.IsDisposed);
        Assert.Equal(new PlaybackSchedulerSnapshot(1, 1, 0), scheduler.GetSnapshot());
    }

    [Fact]
    public void Enqueue_DropIncomingDisposesIncomingAndCountsDrop()
    {
        using var scheduler = Scheduler([TimeSpan.Zero], capacity: 1);
        var queued = AudioFrame(0);
        var incoming = AudioFrame(1);
        scheduler.Enqueue(queued, 1, None);

        var result = scheduler.Enqueue(incoming, 2, None);

        Assert.Equal(PlaybackSchedulerEnqueueResult.DroppedIncoming, result);
        Assert.True(incoming.IsDisposed);
        Assert.Equal(new PlaybackSchedulerSnapshot(1, 1, 0), scheduler.GetSnapshot());
    }

    [Fact]
    public void Enqueue_RejectDisposesIncomingWithoutChangingQueue()
    {
        using var scheduler = Scheduler(
            [TimeSpan.Zero],
            capacity: 1,
            fullPolicy: PlaybackSchedulerFullPolicy.Reject);
        var queued = AudioFrame(0);
        var incoming = AudioFrame(1);
        scheduler.Enqueue(queued, 1, None);

        var result = scheduler.Enqueue(incoming, 2, None);

        Assert.Equal(PlaybackSchedulerEnqueueResult.Rejected, result);
        Assert.True(incoming.IsDisposed);
        Assert.Equal(new PlaybackSchedulerSnapshot(1, 0, 0), scheduler.GetSnapshot());
    }

    [Fact]
    public async Task RunOneAsync_AwaitsFinitePolicyWaitBeforeDelivery()
    {
        using var owners = new TestOwners();
        var delay = new RecordingDelay();
        using var scheduler = Scheduler([Ms(0), Ms(900)], delay: delay);
        scheduler.Enqueue(AudioFrame(0), 1, None);
        scheduler.Enqueue(AudioFrame(1_000), 2, None);
        await Run(scheduler, owners, new RecordingSink());

        var result = await Run(scheduler, owners, new RecordingSink());

        Assert.Equal(PlaybackSchedulerRunResult.Delivered, result);
        Assert.Equal(Ms(100), delay.LastDelay);
    }

    [Fact]
    public async Task RunOneAsync_ProcessesDropResetAndRejectPolicyResults()
    {
        using var owners = new TestOwners();
        using var dropScheduler = Scheduler([Ms(0), Ms(1_501)]);
        dropScheduler.Enqueue(AudioFrame(0), 1, None);
        dropScheduler.Enqueue(AudioFrame(1_000), 2, None);
        await Run(dropScheduler, owners, new RecordingSink());
        Assert.Equal(
            PlaybackSchedulerRunResult.DroppedByPolicy,
            await Run(dropScheduler, owners, new RecordingSink()));

        using var resetScheduler = Scheduler([Ms(0)]);
        resetScheduler.Enqueue(AudioFrame(0), 0, DecoderReset);
        Assert.Equal(
            PlaybackSchedulerRunResult.ClockReset,
            await Run(resetScheduler, owners, new RecordingSink()));

        using var rejectScheduler = Scheduler([Ms(0), Ms(1)]);
        rejectScheduler.Enqueue(AudioFrame(100), 1, None);
        rejectScheduler.Enqueue(AudioFrame(99), 2, None);
        await Run(rejectScheduler, owners, new RecordingSink());
        Assert.Equal(
            PlaybackSchedulerRunResult.PolicyRejected,
            await Run(rejectScheduler, owners, new RecordingSink()));
    }

    [Fact]
    public async Task RunOneAsync_CancellationDuringWaitIsFixedAndDisposesFrame()
    {
        using var owners = new TestOwners();
        var delay = new BlockingDelay();
        using var scheduler = Scheduler([Ms(0), Ms(0)], delay: delay);
        scheduler.Enqueue(AudioFrame(0), 1, None);
        await Run(scheduler, owners, new RecordingSink());
        var waitingFrame = AudioFrame(1_000);
        scheduler.Enqueue(waitingFrame, 2, None);
        using var cancellation = new CancellationTokenSource();

        var run = Run(scheduler, owners, new RecordingSink(), cancellation.Token).AsTask();
        await delay.Entered;
        cancellation.Cancel();

        Assert.Equal(PlaybackSchedulerRunResult.WaitCancelled, await run);
        Assert.True(waitingFrame.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task RunOneAsync_SinkFailureIsFixedAndPreservesContext()
    {
        using var owners = new TestOwners();
        var frame = AudioFrame(0);
        using var scheduler = Scheduler([Ms(0)]);
        scheduler.Enqueue(frame, 1, None);

        var result = await Run(scheduler, owners, new ThrowingSink());

        Assert.Equal(PlaybackSchedulerRunResult.SinkFailed, result);
        Assert.True(frame.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task Dispose_DuringWaitCancelsRunAndDisposesQueuedOwnership()
    {
        using var owners = new TestOwners();
        var delay = new BlockingDelay();
        using var scheduler = Scheduler([Ms(0), Ms(0)], delay: delay);
        scheduler.Enqueue(AudioFrame(0), 1, None);
        await Run(scheduler, owners, new RecordingSink());
        var active = AudioFrame(1_000);
        var queued = AudioFrame(2_000);
        scheduler.Enqueue(active, 2, None);
        scheduler.Enqueue(queued, 3, None);
        var run = Run(scheduler, owners, new RecordingSink()).AsTask();
        await delay.Entered;

        scheduler.Dispose();

        Assert.Equal(PlaybackSchedulerRunResult.Disposed, await run);
        Assert.True(active.IsDisposed);
        Assert.True(queued.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task Dispose_AfterwardCallsReturnFixedResultsAndDisposeIncoming()
    {
        using var owners = new TestOwners();
        using var scheduler = Scheduler([TimeSpan.Zero]);
        scheduler.Dispose();
        var incoming = AudioFrame(0);

        var enqueue = scheduler.Enqueue(incoming, 1, None);
        var run = await Run(scheduler, owners, new RecordingSink());

        Assert.Equal(PlaybackSchedulerEnqueueResult.Disposed, enqueue);
        Assert.Equal(PlaybackSchedulerRunResult.Disposed, run);
        Assert.True(incoming.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task RunOneAsync_AllowsOnlyOneSinkCallbackAndPreservesOrder()
    {
        using var owners = new TestOwners();
        using var scheduler = Scheduler([Ms(0), Ms(1)]);
        scheduler.Enqueue(AudioFrame(0), 1, None);
        scheduler.Enqueue(VideoFrame(1), 2, None);
        var sink = new BlockingSink();

        var first = Run(scheduler, owners, sink).AsTask();
        await sink.FirstEntered;
        var second = Run(scheduler, owners, sink).AsTask();
        Assert.False(second.IsCompleted);
        sink.ReleaseFirst();

        Assert.Equal(PlaybackSchedulerRunResult.Delivered, await first);
        Assert.Equal(PlaybackSchedulerRunResult.Delivered, await second);
        Assert.Equal(new long[] { 1, 2 }, sink.Sequences);
        Assert.Equal(1, sink.MaximumConcurrency);
    }

    [Fact]
    public async Task RunOneAsync_UnexpectedClockFailureFailsClosed()
    {
        using var owners = new TestOwners();
        var policy = new PlaybackPolicyClockBoundary(
            new ThrowingClock(),
            new PlaybackPolicyOptions());
        using var scheduler = new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions(),
            policy);
        scheduler.Enqueue(AudioFrame(0), 1, None);

        var result = await Run(scheduler, owners, new RecordingSink());

        Assert.Equal(PlaybackSchedulerRunResult.IntegrationFailed, result);
        Assert.True(owners.Context.IsDisposed);
    }

    [Fact]
    public void Options_EnforceCapacityHardLimit()
    {
        var policy = new PlaybackPolicyClockBoundary(
            new FakeClock(TimeSpan.Zero),
            new PlaybackPolicyOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions { Capacity = 0 },
            policy));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedPlaybackScheduler(
            new PlaybackSchedulerOptions
            {
                Capacity = PlaybackSchedulerOptions.MaximumCapacity + 1,
            },
            policy));
    }

    private static readonly PlaybackDecodedFrameDiscontinuityKind None =
        PlaybackDecodedFrameDiscontinuityKind.None;
    private static readonly PlaybackDecodedFrameDiscontinuityKind DecoderReset =
        PlaybackDecodedFrameDiscontinuityKind.DecoderReset;

    private static BoundedPlaybackScheduler Scheduler(
        TimeSpan[] arrivals,
        int capacity = PlaybackSchedulerOptions.DefaultCapacity,
        PlaybackSchedulerFullPolicy fullPolicy = PlaybackSchedulerFullPolicy.DropIncoming,
        IPlaybackSchedulerDelay? delay = null) => new(
            new PlaybackSchedulerOptions { Capacity = capacity, FullPolicy = fullPolicy },
            new PlaybackPolicyClockBoundary(
                new FakeClock(arrivals),
                new PlaybackPolicyOptions()),
            new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary(),
            delay ?? new RecordingDelay());

    private static ValueTask<PlaybackSchedulerRunResult> Run(
        BoundedPlaybackScheduler scheduler,
        TestOwners owners,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken = default) =>
        scheduler.RunOneAsync(owners.Context, sink, cancellationToken);

    private static PlaybackDecoderFfmpegManagedFrame AudioFrame(int timestampMs) => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            0,
            0,
            48_000,
            2,
            2,
            PlaybackDecoderFfmpegFrameTime.Known(Ms(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(Ms(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(Ms(20))),
        [new(0, 8, 1, 8, new byte[8])]);

    private static PlaybackDecoderFfmpegManagedFrame VideoFrame(int timestampMs) => new(
        new PlaybackDecoderFfmpegFrameMetadata(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            1,
            1,
            0,
            0,
            0,
            PlaybackDecoderFfmpegFrameTime.Known(Ms(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(Ms(timestampMs)),
            PlaybackDecoderFfmpegFrameTime.Known(Ms(20))),
        [new(0, 4, 1, 4, new byte[4])]);

    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    private sealed class FakeClock(params TimeSpan[] timestamps) : IPlaybackMonotonicClock
    {
        private int _index;
        public TimeSpan GetTimestamp() => timestamps[Math.Min(_index++, timestamps.Length - 1)];
    }

    private sealed class ThrowingClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => throw new InvalidOperationException();
    }

    private sealed class RecordingDelay : IPlaybackSchedulerDelay
    {
        public TimeSpan LastDelay { get; private set; }
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            LastDelay = delay;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IPlaybackSchedulerDelay
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class RecordingSink : IPlaybackDecodedFrameSink
    {
        public List<long> Sequences { get; } = [];
        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            Sequences.Add(frame.Sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            Sequences.Add(frame.Sequence);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IPlaybackDecodedFrameSink
    {
        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class BlockingSink : IPlaybackDecodedFrameSink
    {
        private readonly TaskCompletionSource _firstEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrency;
        public Task FirstEntered => _firstEntered.Task;
        public List<long> Sequences { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public async ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) =>
            await Record(frame.Sequence, cancellationToken);
        public async ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) =>
            await Record(frame.Sequence, cancellationToken);
        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        private async ValueTask Record(long sequence, CancellationToken cancellationToken)
        {
            var concurrency = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            Sequences.Add(sequence);
            try
            {
                if (sequence == 1)
                {
                    _firstEntered.TrySetResult();
                    await _releaseFirst.Task.WaitAsync(cancellationToken);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners()
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "runtime.bin");
            File.WriteAllBytes(path, [1]);
            _runtime = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = new(path, FileMode.Open, FileAccess.Read, FileShare.Read),
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = path,
                },
                []);
            var symbols = RequiredFrameSymbols
                .Select((name, index) => (name, pointer: (nint)(index + 1)))
                .ToDictionary(item => item.name, item => item.pointer, StringComparer.Ordinal);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)100],
                symbols);
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                new FakeContextNativeApi(),
                PlaybackDecoderFfmpegCodecId.H264,
                (nint)200,
                (nint)300);
            Context.MarkParametersTransferred();
            Context.MarkOpened();
        }

        public PlaybackDecoderFfmpegContextLease Context { get; }

        public void Dispose()
        {
            Context.Dispose();
            _runtime.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) => 0;
        public nint GetSymbol(nint module, string symbol) => 0;
        public uint InvokeVersion(nint symbol) => 0;
        public void Free(nint module) { }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) => 0;
        public nint AllocateContext(nint function, nint decoder) => 0;
        public void FreeContext(nint function, ref nint context) => context = 0;
    }
}

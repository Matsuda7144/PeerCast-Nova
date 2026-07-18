namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackAvTimingDecisionBoundaryTests
{
    private static readonly PlaybackAvTimingTolerances Tolerances = new()
    {
        VideoEarlyTolerance = TimeSpan.FromMilliseconds(10),
        VideoLateTolerance = TimeSpan.FromMilliseconds(40),
        AudioEarlyTolerance = TimeSpan.FromMilliseconds(5),
        AudioLateTolerance = TimeSpan.FromMilliseconds(20),
        MaximumWait = TimeSpan.FromMilliseconds(100),
    };

    [Theory]
    [InlineData(PlaybackPolicyMediaKind.Video)]
    [InlineData(PlaybackPolicyMediaKind.Audio)]
    public async Task DecideAsync_OnTime_ReturnsProcessNow(PlaybackPolicyMediaKind kind)
    {
        using var boundary = Create(Position(TimeSpan.FromMilliseconds(100)));
        var result = await boundary.DecideAsync(Item(kind, TimeSpan.FromMilliseconds(100)));
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, result.Result);
    }

    [Fact]
    public async Task DecideAsync_FutureWithinMaximum_ReturnsExactWait()
    {
        using var boundary = Create(Position(TimeSpan.FromMilliseconds(100)));
        var result = await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
            TimeSpan.FromMilliseconds(150)));
        Assert.Equal(PlaybackAvTimingDecisionResult.Wait, result.Result);
        Assert.Equal(TimeSpan.FromMilliseconds(50), result.WaitDuration);
    }

    [Theory]
    [InlineData(PlaybackPolicyMediaKind.Video, PlaybackAvTimingDecisionResult.VideoLate)]
    [InlineData(PlaybackPolicyMediaKind.Audio, PlaybackAvTimingDecisionResult.AudioLate)]
    public async Task DecideAsync_Late_ReturnsMediaSpecificResult(
        PlaybackPolicyMediaKind kind, PlaybackAvTimingDecisionResult expected)
    {
        using var boundary = Create(Position(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(expected, (await boundary.DecideAsync(
            Item(kind, TimeSpan.FromMilliseconds(100)))).Result);
    }

    [Fact]
    public async Task DecideAsync_ToleranceBoundaries_AreProcessNow()
    {
        var source = new QueuePositionSource(
            Position(TimeSpan.FromMilliseconds(100)), Position(TimeSpan.FromMilliseconds(100)));
        using var boundary = Create(source);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.FromMilliseconds(110)))).Result);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.FromMilliseconds(60)))).Result);
    }

    [Fact]
    public async Task DecideAsync_MaximumWaitExceeded_IsFixedResult()
    {
        using var boundary = Create(Position(TimeSpan.Zero));
        Assert.Equal(PlaybackAvTimingDecisionResult.WaitLimitExceeded,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.FromMilliseconds(101)))).Result);
    }

    [Fact]
    public async Task DecideAsync_ClockNotStarted_IsFixedResult()
    {
        using var boundary = Create(new PlaybackAvTimingPositionRead(
            PlaybackAvTimingPositionResult.NotStarted, default));
        Assert.Equal(PlaybackAvTimingDecisionResult.ClockNotStarted,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.Zero))).Result);
    }

    [Fact]
    public async Task DecideAsync_ClockRegression_Faults()
    {
        using var boundary = Create(new QueuePositionSource(
            Position(TimeSpan.FromSeconds(2)), Position(TimeSpan.FromSeconds(1))));
        await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video, TimeSpan.FromSeconds(2)));
        Assert.Equal(PlaybackAvTimingDecisionResult.Failure,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.FromSeconds(2)))).Result);
        Assert.Equal(PlaybackAvTimingDecisionState.Faulted, boundary.Snapshot.State);
    }

    [Fact]
    public async Task DecideAsync_ClockException_Faults()
    {
        using var boundary = Create(new ThrowingPositionSource());
        Assert.Equal(PlaybackAvTimingDecisionResult.Failure,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video, TimeSpan.Zero))).Result);
    }

    [Fact]
    public async Task DecideAsync_TimestampSubtractionOverflow_Faults()
    {
        using var boundary = Create(Position(TimeSpan.MinValue));
        Assert.Equal(PlaybackAvTimingDecisionResult.Failure,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.MaxValue))).Result);
        Assert.Equal(PlaybackAvTimingDecisionState.Faulted, boundary.Snapshot.State);
    }

    [Fact]
    public async Task DecideAsync_InvalidInputFailsClosedBeforeClockAccess()
    {
        var source = new CountingPositionSource(Position(TimeSpan.Zero));
        using var boundary = Create(source);
        var unknownKind = Item((PlaybackPolicyMediaKind)99, TimeSpan.Zero);
        Assert.Equal(PlaybackAvTimingDecisionResult.InvalidTimestamp,
            (await boundary.DecideAsync(unknownKind)).Result);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task DecideAsync_UnknownDiscontinuityFailsClosed()
    {
        using var boundary = Create(Position(TimeSpan.Zero));
        var item = Item(PlaybackPolicyMediaKind.Video, TimeSpan.Zero) with
        { Discontinuity = (PlaybackDecodedFrameDiscontinuityKind)99 };
        Assert.Equal(PlaybackAvTimingDecisionResult.InvalidTimestamp,
            (await boundary.DecideAsync(item)).Result);
    }

    [Fact]
    public async Task DecideAsync_DiscontinuityLatchesResetRequiredWithoutReadingClock()
    {
        var source = new CountingPositionSource(Position(TimeSpan.Zero));
        using var boundary = Create(source);
        var item = Item(PlaybackPolicyMediaKind.Video, TimeSpan.Zero) with
        { Discontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset };
        Assert.Equal(PlaybackAvTimingDecisionResult.DiscontinuityResetRequired,
            (await boundary.DecideAsync(item)).Result);
        Assert.Equal(PlaybackAvTimingDecisionResult.DiscontinuityResetRequired,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Audio, TimeSpan.Zero))).Result);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task DecideAsync_GenerationMismatch_DoesNotReadClock()
    {
        var source = new CountingPositionSource(Position(TimeSpan.Zero));
        using var boundary = Create(source);
        Assert.Equal(PlaybackAvTimingDecisionResult.GenerationMismatch,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.Zero) with
            {
                Generation = 2,
            })).Result);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task ResetAsync_SucceedsTransactionallyAndRestoresReady()
    {
        using var boundary = Create(Position(TimeSpan.Zero));
        var discontinuity = Item(PlaybackPolicyMediaKind.Video, TimeSpan.Zero) with
        { Discontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset };
        await boundary.DecideAsync(discontinuity);
        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(2, 3));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(2, 3,
            PlaybackAvTimingDecisionState.Ready), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_VideoFailureRollsBackAudioAndPreservesSnapshot()
    {
        var resetter = new ScriptedResetter();
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Failure);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        using var boundary = Create(Position(TimeSpan.Zero), resetter);
        Assert.Equal(PlaybackAvTimingResetResult.Failure, await boundary.ResetAsync(2, 3));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(1, 1,
            PlaybackAvTimingDecisionState.Ready), boundary.Snapshot);
        Assert.Collection(resetter.Calls,
            call => Assert.Equal("audio:3", call),
            call => Assert.Equal("video:2", call),
            call => Assert.Equal("audio:1", call));
    }

    [Fact]
    public async Task ResetAsync_RollbackFailure_ExposesPartialResetFault()
    {
        var resetter = new ScriptedResetter();
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Failure);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Failure);
        using var boundary = Create(Position(TimeSpan.Zero), resetter);
        Assert.Equal(PlaybackAvTimingResetResult.PartialResetFault,
            await boundary.ResetAsync(2, 3));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(1, 3,
            PlaybackAvTimingDecisionState.Faulted), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_AfterPartialFault_CanRecover()
    {
        var resetter = new ScriptedResetter();
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Failure);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Failure);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        using var boundary = Create(Position(TimeSpan.Zero), resetter);
        await boundary.ResetAsync(2, 3);
        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(4, 5));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(4, 5,
            PlaybackAvTimingDecisionState.Ready), boundary.Snapshot);
    }

    [Fact]
    public async Task DecideAsync_CancelledBeforeAdmission_DoesNotReadClock()
    {
        var source = new CountingPositionSource(Position(TimeSpan.Zero));
        using var boundary = Create(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(PlaybackAvTimingDecisionResult.Cancelled,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.Zero), cancellation.Token)).Result);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task Dispose_WaitsForInFlightDecisionAndDoesNotDisposeLowerSource()
    {
        var source = new BlockingPositionSource();
        var boundary = Create(source);
        var deciding = boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
            TimeSpan.Zero)).AsTask();
        await source.Entered.Task;
        var disposing = Task.Run(boundary.Dispose);
        Assert.False(disposing.IsCompleted);
        source.Release.SetResult();
        await Task.WhenAll(deciding, disposing);
        boundary.Dispose();
        Assert.False(source.Disposed);
        Assert.Equal(PlaybackAvTimingDecisionState.Disposed, boundary.Snapshot.State);
    }

    [Fact]
    public void Constructor_InvalidTolerance_IsRejected()
    {
        var invalid = Tolerances with
        {
            MaximumWait = TimeSpan.Zero,
            VideoEarlyTolerance = TimeSpan.FromMilliseconds(1),
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackAvTimingDecisionBoundary(
            new FakeClock(), invalid, 1, 1));
    }

    [Fact]
    public async Task ProductionClockSeam_UsesConstructionIndependentPlaybackOrigin()
    {
        var clock = new SequenceClock(TimeSpan.FromHours(5),
            TimeSpan.FromHours(5) + TimeSpan.FromMilliseconds(50));
        using var boundary = new PlaybackAvTimingDecisionBoundary(clock, Tolerances, 1, 1);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.Zero))).Result);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Audio,
                TimeSpan.FromMilliseconds(50)))).Result);
    }

    [Fact]
    public async Task ProductionClockSeam_ExplicitResetRebasesNextReadToZero()
    {
        var clock = new SequenceClock(TimeSpan.FromHours(5),
            TimeSpan.FromHours(5) + TimeSpan.FromMilliseconds(50),
            TimeSpan.FromHours(5) + TimeSpan.FromSeconds(10));
        using var boundary = new PlaybackAvTimingDecisionBoundary(clock, Tolerances, 1, 1);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.Zero))).Result);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Audio,
                TimeSpan.FromMilliseconds(50)))).Result);

        var discontinuity = Item(PlaybackPolicyMediaKind.Video, TimeSpan.Zero) with
        {
            Discontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
        };
        Assert.Equal(PlaybackAvTimingDecisionResult.DiscontinuityResetRequired,
            (await boundary.DecideAsync(discontinuity)).Result);
        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(2, 2));
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
                TimeSpan.Zero) with
            {
                Generation = 2,
            })).Result);
        Assert.Equal(0, clock.Remaining);
    }

    [Theory]
    [InlineData((int)PlaybackAvTimingPositionResetResult.Failure)]
    [InlineData((int)PlaybackAvTimingPositionResetResult.Cancelled)]
    [InlineData((int)PlaybackAvTimingPositionResetResult.Disposed)]
    [InlineData(99)]
    public async Task ResetAsync_PositionFailureRollsBackGenerationsAndFaults(
        int positionResult)
    {
        var source = new ScriptedPositionSource(Position(TimeSpan.Zero));
        source.ResetResults.Enqueue((PlaybackAvTimingPositionResetResult)positionResult);
        var resetter = new ScriptedResetter();
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        using var boundary = Create(source, resetter);

        Assert.Equal(PlaybackAvTimingResetResult.PartialResetFault,
            await boundary.ResetAsync(2, 3));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(1, 1,
            PlaybackAvTimingDecisionState.Faulted), boundary.Snapshot);
        Assert.Collection(resetter.Calls,
            call => Assert.Equal("audio:3", call),
            call => Assert.Equal("video:2", call),
            call => Assert.Equal("video:1", call),
            call => Assert.Equal("audio:1", call));
    }

    [Fact]
    public async Task ResetAsync_RepeatedSuccessRebasesEachTime()
    {
        var source = new ScriptedPositionSource(Position(TimeSpan.Zero));
        source.ResetResults.Enqueue(PlaybackAvTimingPositionResetResult.Reset);
        source.ResetResults.Enqueue(PlaybackAvTimingPositionResetResult.Reset);
        using var boundary = Create(source);

        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(2, 3));
        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(4, 5));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(4, 5,
            PlaybackAvTimingDecisionState.Ready), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_AfterPositionFault_CanRecoverAndRebase()
    {
        var source = new ScriptedPositionSource(Position(TimeSpan.Zero));
        source.ResetResults.Enqueue(PlaybackAvTimingPositionResetResult.Failure);
        source.ResetResults.Enqueue(PlaybackAvTimingPositionResetResult.Reset);
        var resetter = new ScriptedResetter();
        for (var index = 0; index < 6; index++)
            resetter.Results.Enqueue(PlaybackAvTimingGenerationResetResult.Reset);
        using var boundary = Create(source, resetter);
        await boundary.ResetAsync(2, 3);

        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(4, 5));
        Assert.Equal(new PlaybackAvTimingDecisionSnapshot(4, 5,
            PlaybackAvTimingDecisionState.Ready), boundary.Snapshot);
    }

    [Fact]
    public async Task ResetAsync_BlocksDecideAndDisposeWithoutTakingSourceOwnership()
    {
        var source = new BlockingPositionResetSource();
        var boundary = Create(source);
        var reset = boundary.ResetAsync(2, 2).AsTask();
        await source.ResetEntered.Task;
        var decide = boundary.DecideAsync(Item(PlaybackPolicyMediaKind.Video,
            TimeSpan.Zero)).AsTask();
        var dispose = Task.Run(boundary.Dispose);
        Assert.False(decide.IsCompleted);
        Assert.False(dispose.IsCompleted);

        source.ResetRelease.SetResult();
        await Task.WhenAll(reset, decide, dispose);
        boundary.Dispose();
        Assert.Equal(0, source.ReadCount);
        Assert.False(source.Disposed);
        Assert.Equal(PlaybackAvTimingDecisionState.Disposed, boundary.Snapshot.State);
    }

    private static PlaybackAvTimingDecisionBoundary Create(
        PlaybackAvTimingPositionRead read,
        IPlaybackAvTimingGenerationResetter? resetter = null) =>
        Create(new QueuePositionSource(read), resetter);

    private static PlaybackAvTimingDecisionBoundary Create(
        IPlaybackAvTimingPositionSource source,
        IPlaybackAvTimingGenerationResetter? resetter = null) =>
        new(source, resetter ?? new InMemoryPlaybackAvTimingGenerationResetter(),
            Tolerances, 1, 1);

    private static PlaybackAvTimingItem Item(
        PlaybackPolicyMediaKind kind, TimeSpan timestamp) =>
        new(kind, PlaybackPolicyTime.Known(timestamp),
            PlaybackPolicyTime.Known(TimeSpan.FromMilliseconds(20)), 1,
            PlaybackDecodedFrameDiscontinuityKind.None, 1);

    private static PlaybackAvTimingPositionRead Position(TimeSpan value) =>
        new(PlaybackAvTimingPositionResult.Started, value);

    private sealed class QueuePositionSource(params PlaybackAvTimingPositionRead[] reads) :
        IPlaybackAvTimingPositionSource
    {
        private readonly Queue<PlaybackAvTimingPositionRead> _reads = new(reads);
        public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_reads.Dequeue());
        public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingPositionResetResult.Reset);
    }

    private sealed class CountingPositionSource(PlaybackAvTimingPositionRead read) :
        IPlaybackAvTimingPositionSource
    {
        public int CallCount { get; private set; }
        public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken)
        { CallCount++; return ValueTask.FromResult(read); }
        public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingPositionResetResult.Reset);
    }

    private sealed class ThrowingPositionSource : IPlaybackAvTimingPositionSource
    {
        public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
        public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingPositionResetResult.Reset);
    }

    private sealed class BlockingPositionSource : IPlaybackAvTimingPositionSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public async ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken)
        { Entered.SetResult(); await Release.Task; return Position(TimeSpan.Zero); }
        public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackAvTimingPositionResetResult.Reset);
    }

    private sealed class ScriptedResetter : IPlaybackAvTimingGenerationResetter
    {
        public Queue<PlaybackAvTimingGenerationResetResult> Results { get; } = new();
        public List<string> Calls { get; } = [];
        public ValueTask<PlaybackAvTimingGenerationResetResult> ResetAudioAsync(
            long generation, CancellationToken cancellationToken)
        { Calls.Add($"audio:{generation}"); return ValueTask.FromResult(Results.Dequeue()); }
        public ValueTask<PlaybackAvTimingGenerationResetResult> ResetVideoAsync(
            long generation, CancellationToken cancellationToken)
        { Calls.Add($"video:{generation}"); return ValueTask.FromResult(Results.Dequeue()); }
    }

    private sealed class FakeClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }

    private sealed class SequenceClock(params TimeSpan[] timestamps) : IPlaybackMonotonicClock
    {
        private readonly Queue<TimeSpan> _timestamps = new(timestamps);
        public int Remaining => _timestamps.Count;
        public TimeSpan GetTimestamp() => _timestamps.Dequeue();
    }

    private sealed class ScriptedPositionSource(params PlaybackAvTimingPositionRead[] reads) :
        IPlaybackAvTimingPositionSource
    {
        private readonly Queue<PlaybackAvTimingPositionRead> _reads = new(reads);
        public Queue<PlaybackAvTimingPositionResetResult> ResetResults { get; } = new();
        public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_reads.Dequeue());
        public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(ResetResults.Dequeue());
    }

    private sealed class BlockingPositionResetSource : IPlaybackAvTimingPositionSource
    {
        public TaskCompletionSource ResetEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResetRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }
        public bool Disposed { get; private set; }
        public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return ValueTask.FromResult(Position(TimeSpan.Zero));
        }
        public async ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken)
        {
            ResetEntered.SetResult();
            await ResetRelease.Task;
            return PlaybackAvTimingPositionResetResult.Reset;
        }
    }
}

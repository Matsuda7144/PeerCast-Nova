namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackAvTimingPresentationOriginTests
{
    private static readonly PlaybackAvTimingTolerances Tolerances = new()
    {
        VideoEarlyTolerance = TimeSpan.FromMilliseconds(10),
        VideoLateTolerance = TimeSpan.FromMilliseconds(40),
        AudioEarlyTolerance = TimeSpan.FromMilliseconds(5),
        AudioLateTolerance = TimeSpan.FromMilliseconds(20),
        MaximumWait = TimeSpan.FromMilliseconds(250),
    };

    [Fact]
    public async Task ProductionClock_FirstNonZeroTimestampAnchorsToPlaybackStart()
    {
        var clock = new SequenceClock(
            TimeSpan.FromHours(5),
            TimeSpan.FromHours(5) + TimeSpan.FromMilliseconds(100));
        using var boundary = new PlaybackAvTimingDecisionBoundary(clock, Tolerances, 0, 0);

        var first = await boundary.DecideAsync(Item(
            PlaybackPolicyMediaKind.Video,
            TimeSpan.FromMilliseconds(621),
            sequence: 44,
            generation: 0));
        var second = await boundary.DecideAsync(Item(
            PlaybackPolicyMediaKind.Video,
            TimeSpan.FromMilliseconds(721),
            sequence: 45,
            generation: 0));

        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, first.Result);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, second.Result);
        Assert.Equal(0, clock.Remaining);
    }

    [Fact]
    public async Task ProductionClock_SharedAnchorPreservesCrossMediaOffset()
    {
        var clock = new SequenceClock(
            TimeSpan.FromHours(5),
            TimeSpan.FromHours(5) + TimeSpan.FromMilliseconds(20));
        using var boundary = new PlaybackAvTimingDecisionBoundary(clock, Tolerances, 0, 0);

        var video = await boundary.DecideAsync(Item(
            PlaybackPolicyMediaKind.Video,
            TimeSpan.FromMilliseconds(621),
            sequence: 44,
            generation: 0));
        var audio = await boundary.DecideAsync(Item(
            PlaybackPolicyMediaKind.Audio,
            TimeSpan.FromMilliseconds(641),
            sequence: 45,
            generation: 0));

        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, video.Result);
        Assert.Equal(PlaybackAvTimingDecisionResult.ProcessNow, audio.Result);
        Assert.Equal(0, clock.Remaining);
    }

    [Fact]
    public async Task ProductionClock_ResetClearsPresentationAnchor()
    {
        var clock = new SequenceClock(TimeSpan.FromHours(5), TimeSpan.FromHours(6));
        using var boundary = new PlaybackAvTimingDecisionBoundary(clock, Tolerances, 0, 0);

        Assert.Equal(
            PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(
                PlaybackPolicyMediaKind.Video,
                TimeSpan.FromMilliseconds(621),
                sequence: 44,
                generation: 0))).Result);
        Assert.Equal(
            PlaybackAvTimingDecisionResult.DiscontinuityResetRequired,
            (await boundary.DecideAsync(Item(
                PlaybackPolicyMediaKind.Video,
                TimeSpan.FromSeconds(10),
                sequence: 45,
                generation: 0) with
            {
                Discontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            })).Result);
        Assert.Equal(PlaybackAvTimingResetResult.Reset, await boundary.ResetAsync(1, 1));

        Assert.Equal(
            PlaybackAvTimingDecisionResult.ProcessNow,
            (await boundary.DecideAsync(Item(
                PlaybackPolicyMediaKind.Audio,
                TimeSpan.FromSeconds(10),
                sequence: 0,
                generation: 1))).Result);
        Assert.Equal(0, clock.Remaining);
    }

    [Fact]
    public async Task InjectedAbsolutePositionModeRetainsWaitLimitContract()
    {
        using var boundary = new PlaybackAvTimingDecisionBoundary(
            new FixedPositionSource(TimeSpan.Zero),
            new InMemoryPlaybackAvTimingGenerationResetter(),
            Tolerances,
            0,
            0);

        var decision = await boundary.DecideAsync(Item(
            PlaybackPolicyMediaKind.Video,
            TimeSpan.FromMilliseconds(621),
            sequence: 44,
            generation: 0));

        Assert.Equal(PlaybackAvTimingDecisionResult.WaitLimitExceeded, decision.Result);
    }

    private static PlaybackAvTimingItem Item(
        PlaybackPolicyMediaKind kind,
        TimeSpan timestamp,
        long sequence,
        long generation) => new(
            kind,
            PlaybackPolicyTime.Known(timestamp),
            PlaybackPolicyTime.Unknown,
            sequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            generation);

    private sealed class SequenceClock : IPlaybackMonotonicClock
    {
        private readonly Queue<TimeSpan> _values;

        public SequenceClock(params TimeSpan[] values)
        {
            _values = new(values);
        }

        public int Remaining => _values.Count;

        public TimeSpan GetTimestamp() => _values.Dequeue();
    }

    private sealed class FixedPositionSource : IPlaybackAvTimingPositionSource
    {
        private readonly TimeSpan _position;

        public FixedPositionSource(TimeSpan position)
        {
            _position = position;
        }

        public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PlaybackAvTimingPositionRead(
                PlaybackAvTimingPositionResult.Started,
                _position));
        }

        public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PlaybackAvTimingPositionResetResult.Reset);
        }
    }
}

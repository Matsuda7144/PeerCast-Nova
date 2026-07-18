namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPolicyClockBoundaryTests
{
    [Fact]
    public void StopwatchClock_IsMonotonic()
    {
        var clock = new StopwatchPlaybackMonotonicClock();

        var first = clock.GetTimestamp();
        var second = clock.GetTimestamp();

        Assert.True(first >= TimeSpan.Zero);
        Assert.True(second >= first);
    }

    [Fact]
    public void Evaluate_OnTimeFrameDeliversNow()
    {
        var policy = Policy();
        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, policy.Evaluate(Frame(1, 0), Ms(0)).Kind);

        var decision = policy.Evaluate(Frame(2, 100), Ms(100));

        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, decision.Kind);
        Assert.Equal(TimeSpan.Zero, decision.WaitDuration);
    }

    [Fact]
    public void Evaluate_EarlyFrameReturnsFiniteBoundedWait()
    {
        var policy = Policy();
        policy.Evaluate(Frame(1, 0), Ms(0));

        var shortWait = policy.Evaluate(Frame(2, 1_000), Ms(900));
        var boundedWait = policy.Evaluate(Frame(3, 2_000), Ms(1_000));

        Assert.Equal(PlaybackPolicyDecisionKind.Wait, shortWait.Kind);
        Assert.Equal(Ms(100), shortWait.WaitDuration);
        Assert.Equal(PlaybackPolicyDecisionKind.Wait, boundedWait.Kind);
        Assert.Equal(Ms(250), boundedWait.WaitDuration);
    }

    [Fact]
    public void Evaluate_LateFrameBeyondLimitDrops()
    {
        var policy = Policy();
        policy.Evaluate(Frame(1, 0), Ms(0));

        var decision = policy.Evaluate(Frame(2, 1_000), Ms(1_501));

        Assert.Equal(PlaybackPolicyDecisionKind.Drop, decision.Kind);
        Assert.Equal(PlaybackPolicyDecisionReason.Late, decision.Reason);
    }

    [Fact]
    public void Evaluate_LatenessWithinLimitDeliversNow()
    {
        var policy = Policy();
        policy.Evaluate(Frame(1, 0), Ms(0));

        var decision = policy.Evaluate(Frame(2, 1_000), Ms(1_500));

        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, decision.Kind);
    }

    [Fact]
    public void Evaluate_UnknownTimestampAndDurationAreDeterministic()
    {
        var policy = Policy();
        policy.Evaluate(Frame(1, 0), Ms(0));
        var frame = new PlaybackPolicyFrameInput(
            PlaybackPolicyMediaKind.Video,
            PlaybackPolicyTime.Unknown,
            PlaybackPolicyTime.Unknown,
            2,
            PlaybackDecodedFrameDiscontinuityKind.None);

        var decision = policy.Evaluate(frame, Ms(10));

        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, decision.Kind);
        Assert.Equal(PlaybackPolicyDecisionReason.UnknownTiming, decision.Reason);
    }

    [Fact]
    public void Evaluate_TimestampRegressionRejects()
    {
        var policy = Policy();
        policy.Evaluate(Frame(1, 100), Ms(0));

        var decision = policy.Evaluate(Frame(2, 99), Ms(1));

        Assert.Equal(PlaybackPolicyDecisionKind.Reject, decision.Kind);
        Assert.Equal(PlaybackPolicyDecisionReason.TimestampRegression, decision.Reason);
    }

    [Fact]
    public void Evaluate_SequenceRegressionRejects()
    {
        var policy = Policy();
        policy.Evaluate(Frame(5, 0), Ms(0));

        var decision = policy.Evaluate(Frame(4, 1), Ms(1));

        Assert.Equal(PlaybackPolicyDecisionKind.Reject, decision.Kind);
        Assert.Equal(PlaybackPolicyDecisionReason.SequenceRegression, decision.Reason);
    }

    [Fact]
    public void Evaluate_DecoderResetReanchorsBeforeRegressionChecks()
    {
        var policy = Policy();
        policy.Evaluate(Frame(10, 10_000), Ms(10_000));

        var reset = policy.Evaluate(
            Frame(0, 0, PlaybackDecodedFrameDiscontinuityKind.DecoderReset),
            Ms(11_000));
        var after = policy.Evaluate(Frame(1, 100), Ms(11_100));

        Assert.Equal(PlaybackPolicyDecisionKind.ResetClock, reset.Kind);
        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, after.Kind);
    }

    [Fact]
    public void Evaluate_AudioAndVideoMasterSettingsProduceConfiguredBehavior()
    {
        var audioMaster = Policy(PlaybackPolicyClockMaster.Audio);
        var videoMaster = Policy(PlaybackPolicyClockMaster.Video);
        var audio = Frame(1, 0, mediaKind: PlaybackPolicyMediaKind.Audio);
        var video = Frame(2, 1_000, mediaKind: PlaybackPolicyMediaKind.Video);

        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, audioMaster.Evaluate(audio, Ms(0)).Kind);
        Assert.Equal(PlaybackPolicyDecisionKind.Wait, audioMaster.Evaluate(video, Ms(0)).Kind);
        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, videoMaster.Evaluate(audio, Ms(0)).Kind);
        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, videoMaster.Evaluate(video, Ms(0)).Kind);
    }

    [Fact]
    public void Evaluate_ClockRegressionRejects()
    {
        var policy = Policy();
        policy.Evaluate(Frame(1, 0), Ms(10));

        var decision = policy.Evaluate(Frame(2, 1), Ms(9));

        Assert.Equal(PlaybackPolicyDecisionKind.Reject, decision.Kind);
        Assert.Equal(PlaybackPolicyDecisionReason.ClockRegression, decision.Reason);
    }

    [Fact]
    public void Evaluate_OverflowAndExcessiveLeadFailClosed()
    {
        var overflowPolicy = Policy();
        overflowPolicy.Evaluate(Frame(1, 0), TimeSpan.MaxValue);
        var overflow = overflowPolicy.Evaluate(
            FrameTicks(2, 1),
            TimeSpan.MaxValue);

        var leadPolicy = Policy();
        leadPolicy.Evaluate(Frame(1, 0), TimeSpan.Zero);
        var excessiveLead = leadPolicy.Evaluate(Frame(2, 2_001), TimeSpan.Zero);

        Assert.Equal(PlaybackPolicyDecisionReason.Overflow, overflow.Reason);
        Assert.Equal(PlaybackPolicyDecisionKind.Reject, excessiveLead.Kind);
    }

    [Fact]
    public void Evaluate_InvalidDurationRejects()
    {
        var policy = Policy();
        var frame = new PlaybackPolicyFrameInput(
            PlaybackPolicyMediaKind.Audio,
            PlaybackPolicyTime.Known(TimeSpan.Zero),
            PlaybackPolicyTime.Known(TimeSpan.Zero),
            1,
            PlaybackDecodedFrameDiscontinuityKind.None);

        var decision = policy.Evaluate(frame, TimeSpan.Zero);

        Assert.Equal(PlaybackPolicyDecisionKind.Reject, decision.Kind);
        Assert.Equal(PlaybackPolicyDecisionReason.InvalidInput, decision.Reason);
    }

    [Fact]
    public void Options_RejectValuesAboveFixedLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackPolicyClockBoundary(
            new FakeClock(TimeSpan.Zero),
            new PlaybackPolicyOptions
            {
                MaximumWait = PlaybackPolicyOptions.MaximumWaitLimit + TimeSpan.FromTicks(1),
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackPolicyClockBoundary(
            new FakeClock(TimeSpan.Zero),
            new PlaybackPolicyOptions
            {
                MaximumLateness =
                    PlaybackPolicyOptions.MaximumLatenessLimit + TimeSpan.FromTicks(1),
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackPolicyClockBoundary(
            new FakeClock(TimeSpan.Zero),
            new PlaybackPolicyOptions
            {
                MaximumLead = PlaybackPolicyOptions.MaximumLeadLimit + TimeSpan.FromTicks(1),
            }));
    }

    [Fact]
    public void Evaluate_EquivalentPoliciesProduceSameDecisions()
    {
        var first = Policy();
        var second = Policy();
        var inputs = new[]
        {
            (Frame(1, 0), Ms(0)),
            (Frame(2, 1_000), Ms(900)),
            (Frame(3, 2_000), Ms(2_600)),
        };

        var firstResults = inputs.Select(input => first.Evaluate(input.Item1, input.Item2)).ToArray();
        var secondResults = inputs.Select(input => second.Evaluate(input.Item1, input.Item2)).ToArray();

        Assert.Equal(firstResults, secondResults);
    }

    [Fact]
    public void Evaluate_UsesInjectedMonotonicClockArrival()
    {
        var clock = new FakeClock(Ms(0), Ms(100));
        var policy = new PlaybackPolicyClockBoundary(clock, new PlaybackPolicyOptions());

        var first = policy.Evaluate(Frame(1, 0));
        var second = policy.Evaluate(Frame(2, 100));

        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, first.Kind);
        Assert.Equal(PlaybackPolicyDecisionKind.DeliverNow, second.Kind);
    }

    private static PlaybackPolicyClockBoundary Policy(
        PlaybackPolicyClockMaster master = PlaybackPolicyClockMaster.Audio) => new(
            new FakeClock(TimeSpan.Zero),
            new PlaybackPolicyOptions { ClockMaster = master });

    private static PlaybackPolicyFrameInput Frame(
        long sequence,
        int timestampMilliseconds,
        PlaybackDecodedFrameDiscontinuityKind discontinuity =
            PlaybackDecodedFrameDiscontinuityKind.None,
        PlaybackPolicyMediaKind mediaKind = PlaybackPolicyMediaKind.Audio) => new(
            mediaKind,
            PlaybackPolicyTime.Known(Ms(timestampMilliseconds)),
            PlaybackPolicyTime.Known(Ms(20)),
            sequence,
            discontinuity);

    private static PlaybackPolicyFrameInput FrameTicks(long sequence, long timestampTicks) => new(
        PlaybackPolicyMediaKind.Audio,
        PlaybackPolicyTime.Known(TimeSpan.FromTicks(timestampTicks)),
        PlaybackPolicyTime.Known(Ms(20)),
        sequence,
        PlaybackDecodedFrameDiscontinuityKind.None);

    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    private sealed class FakeClock(params TimeSpan[] timestamps) : IPlaybackMonotonicClock
    {
        private int _index;

        public TimeSpan GetTimestamp() => timestamps[Math.Min(_index++, timestamps.Length - 1)];
    }
}

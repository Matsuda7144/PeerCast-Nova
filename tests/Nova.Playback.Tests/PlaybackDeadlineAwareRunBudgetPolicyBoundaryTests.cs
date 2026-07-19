namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDeadlineAwareRunBudgetPolicyBoundaryTests
{
    [Fact]
    public void Decide_BudgetsDurationAndPumpCountFromRemainingDeadline()
    {
        var decision = PlaybackDeadlineAwareRunBudgetPolicyBoundary.Decide(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),
            ValidOptions(
                maximumRunDuration: TimeSpan.FromSeconds(10),
                maximumPumpCount: 100,
                minimumPumpDuration: TimeSpan.FromMilliseconds(100),
                safetyMargin: TimeSpan.FromSeconds(1)));

        Assert.Equal(PlaybackDeadlineAwareRunBudgetResult.Budgeted, decision.Result);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.RemainingDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), decision.BudgetedRunDuration);
        Assert.Equal(40, decision.BudgetedPumpCount);
        Assert.NotNull(decision.PumpOptions);
        Assert.Equal(TimeSpan.FromSeconds(4), decision.PumpOptions.MaximumRunDuration);
        Assert.Equal(40, decision.PumpOptions.MaximumPumpCount);
    }

    [Fact]
    public void Decide_PreservesStricterRequestedLimits()
    {
        var options = ValidOptions(
            maximumRunDuration: TimeSpan.FromSeconds(2),
            maximumPumpCount: 3,
            minimumPumpDuration: TimeSpan.FromMilliseconds(100),
            safetyMargin: TimeSpan.FromMilliseconds(500));

        var decision = PlaybackDeadlineAwareRunBudgetPolicyBoundary.Decide(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(20),
            options);

        Assert.Equal(PlaybackDeadlineAwareRunBudgetResult.Budgeted, decision.Result);
        Assert.Equal(TimeSpan.FromSeconds(2), decision.BudgetedRunDuration);
        Assert.Equal(3, decision.BudgetedPumpCount);
        Assert.Same(options.PumpOptions.DispatchOptions, decision.PumpOptions!.DispatchOptions);
    }

    [Fact]
    public void Decide_DeadlineAtSafetyMarginReturnsDeadlineReached()
    {
        var decision = PlaybackDeadlineAwareRunBudgetPolicyBoundary.Decide(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            ValidOptions(safetyMargin: TimeSpan.FromSeconds(1)));

        Assert.Equal(PlaybackDeadlineAwareRunBudgetResult.DeadlineReached, decision.Result);
        Assert.Equal(TimeSpan.FromSeconds(1), decision.RemainingDuration);
        Assert.Null(decision.PumpOptions);
    }

    [Fact]
    public void Decide_UsableDurationBelowOnePumpReturnsDeadlineReached()
    {
        var decision = PlaybackDeadlineAwareRunBudgetPolicyBoundary.Decide(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(150),
            ValidOptions(
                minimumPumpDuration: TimeSpan.FromMilliseconds(100),
                safetyMargin: TimeSpan.FromMilliseconds(75)));

        Assert.Equal(PlaybackDeadlineAwareRunBudgetResult.DeadlineReached, decision.Result);
        Assert.Equal(TimeSpan.FromMilliseconds(150), decision.RemainingDuration);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 1)]
    public void Decide_InvalidClockInputsFailClosed(int nowSeconds, int deadlineSeconds)
    {
        var decision = PlaybackDeadlineAwareRunBudgetPolicyBoundary.Decide(
            TimeSpan.FromSeconds(nowSeconds),
            TimeSpan.FromSeconds(deadlineSeconds),
            ValidOptions());

        Assert.Equal(PlaybackDeadlineAwareRunBudgetResult.InvalidConfiguration, decision.Result);
        Assert.Null(decision.PumpOptions);
    }

    [Fact]
    public void Decide_InvalidNestedPumpOptionsFailClosed()
    {
        var decision = PlaybackDeadlineAwareRunBudgetPolicyBoundary.Decide(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            new PlaybackDeadlineAwareRunBudgetOptions
            {
                MinimumPumpDuration = TimeSpan.FromMilliseconds(1),
                PumpOptions = new PlaybackLateAwareContinuousPumpOptions(),
            });

        Assert.Equal(PlaybackDeadlineAwareRunBudgetResult.InvalidConfiguration, decision.Result);
    }

    private static PlaybackDeadlineAwareRunBudgetOptions ValidOptions(
        TimeSpan? maximumRunDuration = null,
        int maximumPumpCount = 10,
        TimeSpan? minimumPumpDuration = null,
        TimeSpan? safetyMargin = null) => new()
        {
            SafetyMargin = safetyMargin ?? TimeSpan.Zero,
            MinimumPumpDuration = minimumPumpDuration ?? TimeSpan.FromMilliseconds(10),
            PumpOptions = new PlaybackLateAwareContinuousPumpOptions
            {
                MaximumPumpCount = maximumPumpCount,
                MaximumRunDuration = maximumRunDuration ?? TimeSpan.FromSeconds(1),
                MaximumConsecutiveIdleCount = 2,
                IdleDelay = TimeSpan.FromMilliseconds(1),
                DispatchOptions = new PlaybackTimedSchedulerDispatchOptions(),
            },
        };
}

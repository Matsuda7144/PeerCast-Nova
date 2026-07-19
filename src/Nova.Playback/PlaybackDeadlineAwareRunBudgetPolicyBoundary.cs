namespace PeerCast.Nova.Playback;

public enum PlaybackDeadlineAwareRunBudgetResult
{
    Budgeted,
    DeadlineReached,
    InvalidConfiguration,
    Failure,
}

public sealed record PlaybackDeadlineAwareRunBudgetOptions
{
    public TimeSpan SafetyMargin { get; init; }
    public TimeSpan MinimumPumpDuration { get; init; }
    public PlaybackLateAwareContinuousPumpOptions PumpOptions { get; init; } = new();
}

public readonly record struct PlaybackDeadlineAwareRunBudgetDecision(
    PlaybackDeadlineAwareRunBudgetResult Result,
    TimeSpan RemainingDuration,
    TimeSpan BudgetedRunDuration,
    int BudgetedPumpCount,
    PlaybackLateAwareContinuousPumpOptions? PumpOptions);

public static class PlaybackDeadlineAwareRunBudgetPolicyBoundary
{
    public static PlaybackDeadlineAwareRunBudgetDecision Decide(
        TimeSpan now,
        TimeSpan deadline,
        PlaybackDeadlineAwareRunBudgetOptions? options)
    {
        if (!Validate(now, deadline, options))
            return Invalid();

        try
        {
            var remainingTicks = checked(deadline.Ticks - now.Ticks);
            var remaining = TimeSpan.FromTicks(remainingTicks);
            if (remaining <= options!.SafetyMargin)
                return DeadlineReached(remaining);

            var usableTicks = checked(remainingTicks - options.SafetyMargin.Ticks);
            var requestedTicks = options.PumpOptions.MaximumRunDuration.Ticks;
            var budgetedTicks = Math.Min(usableTicks, requestedTicks);
            if (budgetedTicks < options.MinimumPumpDuration.Ticks)
                return DeadlineReached(remaining);

            var pumpCountByDuration = budgetedTicks / options.MinimumPumpDuration.Ticks;
            if (pumpCountByDuration < 1)
                return DeadlineReached(remaining);

            var budgetedPumpCount = Math.Min(
                options.PumpOptions.MaximumPumpCount,
                pumpCountByDuration > int.MaxValue ? int.MaxValue : (int)pumpCountByDuration);
            if (budgetedPumpCount < 1)
                return DeadlineReached(remaining);

            var budgetedDuration = TimeSpan.FromTicks(budgetedTicks);
            var pumpOptions = new PlaybackLateAwareContinuousPumpOptions
            {
                MaximumPumpCount = budgetedPumpCount,
                MaximumRunDuration = budgetedDuration,
                MaximumConsecutiveIdleCount = options.PumpOptions.MaximumConsecutiveIdleCount,
                IdleDelay = options.PumpOptions.IdleDelay,
                DispatchOptions = options.PumpOptions.DispatchOptions,
            };

            return new(
                PlaybackDeadlineAwareRunBudgetResult.Budgeted,
                remaining,
                budgetedDuration,
                budgetedPumpCount,
                pumpOptions);
        }
        catch
        {
            return new(
                PlaybackDeadlineAwareRunBudgetResult.Failure,
                TimeSpan.Zero,
                TimeSpan.Zero,
                0,
                null);
        }
    }

    private static bool Validate(
        TimeSpan now,
        TimeSpan deadline,
        PlaybackDeadlineAwareRunBudgetOptions? options) =>
        now >= TimeSpan.Zero && deadline >= now && options is not null &&
        options.SafetyMargin >= TimeSpan.Zero &&
        options.MinimumPumpDuration > TimeSpan.Zero &&
        ValidatePumpOptions(options.PumpOptions);

    private static bool ValidatePumpOptions(PlaybackLateAwareContinuousPumpOptions? options) =>
        options is not null &&
        options.MaximumPumpCount is >= 1 and <=
            PlaybackLateAwareContinuousPumpOptions.MaximumAllowedPumpCount &&
        options.MaximumConsecutiveIdleCount is >= 1 and <=
            PlaybackLateAwareContinuousPumpOptions.MaximumAllowedConsecutiveIdleCount &&
        options.MaximumRunDuration > TimeSpan.Zero &&
        options.MaximumRunDuration <= PlaybackLateAwareContinuousPumpOptions.MaximumAllowedRunDuration &&
        options.IdleDelay >= PlaybackLateAwareContinuousPumpOptions.MinimumIdleDelay &&
        options.IdleDelay <= PlaybackLateAwareContinuousPumpOptions.MaximumIdleDelay &&
        options.DispatchOptions is not null;

    private static PlaybackDeadlineAwareRunBudgetDecision Invalid() => new(
        PlaybackDeadlineAwareRunBudgetResult.InvalidConfiguration,
        TimeSpan.Zero,
        TimeSpan.Zero,
        0,
        null);

    private static PlaybackDeadlineAwareRunBudgetDecision DeadlineReached(TimeSpan remaining) => new(
        PlaybackDeadlineAwareRunBudgetResult.DeadlineReached,
        remaining,
        TimeSpan.Zero,
        0,
        null);
}

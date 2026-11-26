namespace PeerCast.Nova.App;

/// <summary>
/// Compact GUI-facing projection of channel-catalog refresh diagnostics. The
/// caller supplies the current time so presentation remains deterministic in
/// tests and does not own a timer.
/// </summary>
internal static class GenesisChannelCatalogRefreshStatusPresentation
{
    public static string Format(
        GenesisChannelCatalogRefreshDiagnostics diagnostics,
        bool isRefreshing,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (isRefreshing)
        {
            return diagnostics.LastSuccessfulAt is { } lastSuccessfulAt
                ? $"YP: updating · last updated {FormatAge(lastSuccessfulAt, now)}"
                : "YP: updating";
        }

        return diagnostics.LastResult switch
        {
            GenesisChannelCatalogRefreshResult.Completed
                when diagnostics.LastSuccessfulAt is { } lastSuccessfulAt =>
                $"YP: updated {FormatAge(lastSuccessfulAt, now)}",
            GenesisChannelCatalogRefreshResult.Faulted =>
                FormatNonSuccessfulResult(
                    "YP: refresh failed",
                    diagnostics.LastAttemptedAt,
                    diagnostics.LastSuccessfulAt,
                    now),
            GenesisChannelCatalogRefreshResult.Cancelled =>
                FormatNonSuccessfulResult(
                    "YP: refresh cancelled",
                    diagnostics.LastAttemptedAt,
                    diagnostics.LastSuccessfulAt,
                    now),
            _ => "YP: not refreshed",
        };
    }

    internal static string FormatAge(
        DateTimeOffset observedAt,
        DateTimeOffset now)
    {
        var age = now - observedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return $"{(long)age.TotalSeconds}s ago";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(long)age.TotalMinutes}m ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(long)age.TotalHours}h ago";
        }

        return $"{(long)age.TotalDays}d ago";
    }

    private static string FormatNonSuccessfulResult(
        string status,
        DateTimeOffset? lastAttemptedAt,
        DateTimeOffset? lastSuccessfulAt,
        DateTimeOffset now)
    {
        var attemptedStatus = lastAttemptedAt is { } attemptedAt
            ? $"{status} {FormatAge(attemptedAt, now)}"
            : status;
        return lastSuccessfulAt is { } successfulAt
            ? $"{attemptedStatus} · last updated {FormatAge(successfulAt, now)}"
            : attemptedStatus;
    }
}

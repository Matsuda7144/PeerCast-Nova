namespace PeerCast.Nova.Network.Pcp;

public enum PcpSmartRelayMode { Limited, Persistent }
public enum PcpSmartRelayState { Disabled, Discovering, Connecting, Negotiating, Receiving, Degraded, Recovering, Backoff, Stopping, Stopped, Failed }

/// <summary>Opt-in recovery policy. Null on session options preserves legacy finite reconnects.</summary>
public sealed record PcpSmartRelayOptions
{
    public PcpSmartRelayMode Mode { get; init; } = PcpSmartRelayMode.Limited;
    public TimeSpan RecoveryLimit { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    // Only explicitly profiled continuous media should use these deadlines.
    // Null keeps sparse RAW streams on their existing read-idle policy.
    public TimeSpan? MediaStallTimeout { get; init; }
    public TimeSpan MediaDegradedAfter { get; init; } = TimeSpan.FromSeconds(10);
    internal Func<double> JitterSample { get; init; } = Random.Shared.NextDouble;

    internal void Validate()
    {
        if (!Enum.IsDefined(Mode) || RecoveryLimit <= TimeSpan.Zero || CleanupTimeout <= TimeSpan.Zero ||
            CleanupTimeout > TimeSpan.FromMinutes(1) || MediaDegradedAfter <= TimeSpan.Zero ||
            MediaStallTimeout is { } stall && stall <= MediaDegradedAfter)
            throw new ArgumentOutOfRangeException(nameof(PcpSmartRelayOptions));
        ArgumentNullException.ThrowIfNull(TimeProvider);
        ArgumentNullException.ThrowIfNull(JitterSample);
    }

    internal static TimeSpan Backoff(bool lowFrequency, int step, double sample)
    {
        var seconds = lowFrequency ? step switch { 0 => 60, 1 => 120, _ => 300 } :
            step >= 5 ? 30 : 1 << Math.Max(0, step);
        return TimeSpan.FromSeconds(seconds * (0.8 + 0.4 * Math.Clamp(sample, 0, 1)));
    }
}

/// <summary>Content/endpoint-free observation; counters saturate and retain no attempt list.</summary>
public sealed record PcpSmartRelaySnapshot(PcpSmartRelayState State = PcpSmartRelayState.Disabled,
    int AttemptCount = 0, int WaveAttemptCount = 0, int ConsecutiveFailures = 0,
    bool LowFrequency = false, TimeSpan? RetryDelay = null, long? ResumePosition = null,
    long MediaGeneration = 0, long Discontinuities = 0);

internal static class PcpSmartRelayAttemptLimit
{
    // Shared across channel owners. Waiting owns no transport or PipeReader.
    internal static readonly SemaphoreSlim Slots = new(4, 4);
}

namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Immutable terminal fault summary safe for default UI and diagnostics. It
/// deliberately stores no free-form text, identifier, endpoint, metadata,
/// payload, position, protocol code, or exception.
/// </summary>
public sealed record ChannelFaultSnapshot
{
    public ChannelFaultSnapshot(
        ChannelFaultCategory category,
        ChannelFaultStage stage,
        int attemptCount,
        int reconnectCount)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "The channel fault category is not defined.");
        }

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "The channel fault stage is not defined.");
        }

        if (attemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptCount),
                attemptCount,
                "A terminal fault must follow at least one attempt.");
        }

        if (reconnectCount < 0 || reconnectCount >= attemptCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectCount),
                reconnectCount,
                "Reconnect count must be non-negative and lower than attempt count.");
        }

        Category = category;
        Stage = stage;
        AttemptCount = attemptCount;
        ReconnectCount = reconnectCount;
    }

    public ChannelFaultCategory Category { get; }

    public ChannelFaultStage Stage { get; }

    public int AttemptCount { get; }

    public int ReconnectCount { get; }
}

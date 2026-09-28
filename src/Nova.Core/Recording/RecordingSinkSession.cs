using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Recorder-owned session identity retained by orchestration code.
/// </summary>
public sealed record RecordingSinkSession
{
    public RecordingSinkSession(
        RecordingSessionId sessionId,
        ChannelId channelId,
        Guid startedByRuleId)
        : this(
            sessionId,
            channelId,
            RecordingSessionOrigin.AutomaticRule,
            startedByRuleId)
    {
    }

    private RecordingSinkSession(
        RecordingSessionId sessionId,
        ChannelId channelId,
        RecordingSessionOrigin origin,
        Guid? startedByRuleId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(channelId);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (origin == RecordingSessionOrigin.AutomaticRule &&
            (startedByRuleId is null || startedByRuleId == Guid.Empty))
        {
            throw new ArgumentException(
                "A starting recording rule identifier cannot be empty.",
                nameof(startedByRuleId));
        }

        if (origin == RecordingSessionOrigin.Manual &&
            startedByRuleId is not null)
        {
            throw new ArgumentException(
                "A manual recording session cannot identify a starting rule.",
                nameof(startedByRuleId));
        }

        SessionId = sessionId;
        ChannelId = channelId;
        Origin = origin;
        AutomaticRuleId = startedByRuleId;
    }

    public static RecordingSinkSession Manual(
        RecordingSessionId sessionId,
        ChannelId channelId)
    {
        return new RecordingSinkSession(
            sessionId,
            channelId,
            RecordingSessionOrigin.Manual,
            startedByRuleId: null);
    }

    public RecordingSessionId SessionId { get; }

    public ChannelId ChannelId { get; }

    public RecordingSessionOrigin Origin { get; }

    public Guid? AutomaticRuleId { get; }

    /// <summary>
    /// Compatibility view for rule-started sessions. Empty means manual.
    /// </summary>
    public Guid StartedByRuleId => AutomaticRuleId ?? Guid.Empty;
}

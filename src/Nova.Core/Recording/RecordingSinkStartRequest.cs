namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Immutable request to begin one concrete recording session.
/// </summary>
public sealed record RecordingSinkStartRequest
{
    public RecordingSinkStartRequest(
        RecordingCandidate candidate,
        Guid selectedRuleId)
        : this(
            candidate,
            RecordingSessionOrigin.AutomaticRule,
            selectedRuleId)
    {
    }

    private RecordingSinkStartRequest(
        RecordingCandidate candidate,
        RecordingSessionOrigin origin,
        Guid? selectedRuleId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (origin == RecordingSessionOrigin.AutomaticRule &&
            (selectedRuleId is null || selectedRuleId == Guid.Empty))
        {
            throw new ArgumentException(
                "A selected recording rule identifier cannot be empty.",
                nameof(selectedRuleId));
        }

        if (origin == RecordingSessionOrigin.Manual &&
            selectedRuleId is not null)
        {
            throw new ArgumentException(
                "A manual recording request cannot select a rule.",
                nameof(selectedRuleId));
        }

        Candidate = candidate;
        Origin = origin;
        RuleId = selectedRuleId;
    }

    public static RecordingSinkStartRequest Manual(
        RecordingCandidate candidate)
    {
        return new RecordingSinkStartRequest(
            candidate,
            RecordingSessionOrigin.Manual,
            selectedRuleId: null);
    }

    public RecordingCandidate Candidate { get; }

    public RecordingSessionOrigin Origin { get; }

    /// <summary>
    /// Rule identifier for an automatic request, or null for manual capture.
    /// </summary>
    public Guid? RuleId { get; }

    /// <summary>
    /// Compatibility view for callers that only issue automatic requests.
    /// Empty means the request is manual; use <see cref="RuleId"/> when the
    /// origin can vary.
    /// </summary>
    public Guid SelectedRuleId => RuleId ?? Guid.Empty;
}

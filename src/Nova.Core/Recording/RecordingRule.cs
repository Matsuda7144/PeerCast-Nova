namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// One ordered, independently enabled recording decision rule.
/// </summary>
public sealed record RecordingRule
{
    public RecordingRule(
        Guid id,
        string name,
        RecordingRuleAction action,
        RecordingRuleCriteria criteria,
        int priority = 0,
        bool isEnabled = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "A recording rule identifier cannot be empty.",
                nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(criteria);

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        Id = id;
        Name = name.Trim();
        Action = action;
        Criteria = criteria;
        Priority = priority;
        IsEnabled = isEnabled;
    }

    public Guid Id { get; }

    public string Name { get; }

    public RecordingRuleAction Action { get; }

    public RecordingRuleCriteria Criteria { get; }

    public int Priority { get; }

    public bool IsEnabled { get; }
}

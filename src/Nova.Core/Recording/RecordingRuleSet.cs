using System.Collections.ObjectModel;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Ordered, validated recording rule collection.
/// </summary>
public sealed class RecordingRuleSet
{
    public const int MaximumRuleCount = 1024;

    private readonly ReadOnlyCollection<RecordingRule> rules;

    public RecordingRuleSet(IEnumerable<RecordingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var collected = new List<RecordingRule>();
        var identifiers = new HashSet<Guid>();

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (collected.Count >= MaximumRuleCount)
            {
                throw new ArgumentException(
                    $"A recording rule set cannot contain more than {MaximumRuleCount} rules.",
                    nameof(rules));
            }

            if (!identifiers.Add(rule.Id))
            {
                throw new ArgumentException(
                    "Recording rule identifiers must be unique.",
                    nameof(rules));
            }

            collected.Add(rule);
        }

        this.rules = Array.AsReadOnly(collected.ToArray());
    }

    public IReadOnlyList<RecordingRule> Rules => rules;

    public RecordingRuleSet Add(RecordingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var updated = rules.ToList();
        updated.Add(rule);
        return new RecordingRuleSet(updated);
    }

    public RecordingRuleSet Replace(RecordingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var index = FindIndex(rule.Id);

        if (index < 0)
        {
            throw new KeyNotFoundException(
                "The recording rule to replace does not exist.");
        }

        if (rules[index] == rule)
        {
            return this;
        }

        var updated = rules.ToArray();
        updated[index] = rule;
        return new RecordingRuleSet(updated);
    }

    public RecordingRuleSet Remove(Guid ruleId)
    {
        ValidateRuleId(ruleId);

        var index = FindIndex(ruleId);

        if (index < 0)
        {
            return this;
        }

        var updated = rules.ToList();
        updated.RemoveAt(index);
        return new RecordingRuleSet(updated);
    }

    public RecordingRuleSet Move(Guid ruleId, int targetIndex)
    {
        ValidateRuleId(ruleId);

        if (targetIndex < 0 || targetIndex >= rules.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetIndex));
        }

        var sourceIndex = FindIndex(ruleId);

        if (sourceIndex < 0)
        {
            throw new KeyNotFoundException(
                "The recording rule to move does not exist.");
        }

        if (sourceIndex == targetIndex)
        {
            return this;
        }

        var updated = rules.ToList();
        var rule = updated[sourceIndex];
        updated.RemoveAt(sourceIndex);
        updated.Insert(targetIndex, rule);
        return new RecordingRuleSet(updated);
    }

    public RecordingRuleDecision Evaluate(RecordingCandidate candidate)
    {
        return RecordingRuleEngine.Evaluate(candidate, rules);
    }

    public RecordingRuleEvaluationReport EvaluateDetailed(
        RecordingCandidate candidate)
    {
        return RecordingRuleEngine.EvaluateDetailed(candidate, rules);
    }

    private int FindIndex(Guid ruleId)
    {
        for (var index = 0; index < rules.Count; index++)
        {
            if (rules[index].Id == ruleId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void ValidateRuleId(Guid ruleId)
    {
        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException(
                "A recording rule identifier cannot be empty.",
                nameof(ruleId));
        }
    }
}

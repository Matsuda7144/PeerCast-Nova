using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingRuleSetEditingTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public void Add_AppendsRuleWithoutMutatingOriginal()
    {
        var first = CreateRule("First");
        var second = CreateRule("Second");
        var original = new RecordingRuleSet([first]);

        var updated = original.Add(second);

        Assert.Single(original.Rules);
        Assert.Equal(2, updated.Rules.Count);
        Assert.Same(first, updated.Rules[0]);
        Assert.Same(second, updated.Rules[1]);
    }

    [Fact]
    public void Add_DuplicateIdentifier_IsRejected()
    {
        var first = CreateRule("First");
        var duplicate = new RecordingRule(
            first.Id,
            "Duplicate",
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll);

        var original = new RecordingRuleSet([first]);

        Assert.Throws<ArgumentException>(
            () => original.Add(duplicate));
    }

    [Fact]
    public void Replace_PreservesPositionAndOriginal()
    {
        var first = CreateRule("First");
        var second = CreateRule("Second");
        var replacement = new RecordingRule(
            first.Id,
            "Replacement",
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll,
            priority: 200,
            isEnabled: false);

        var original = new RecordingRuleSet([first, second]);

        var updated = original.Replace(replacement);

        Assert.Same(first, original.Rules[0]);
        Assert.Same(replacement, updated.Rules[0]);
        Assert.Same(second, updated.Rules[1]);
    }

    [Fact]
    public void Replace_MissingIdentifier_IsRejected()
    {
        var original = new RecordingRuleSet(
            [CreateRule("First")]);

        Assert.Throws<KeyNotFoundException>(
            () => original.Replace(CreateRule("Missing")));
    }

    [Fact]
    public void Remove_IsIdempotentAndDoesNotMutateOriginal()
    {
        var first = CreateRule("First");
        var second = CreateRule("Second");
        var original = new RecordingRuleSet([first, second]);

        var updated = original.Remove(first.Id);
        var unchanged = updated.Remove(first.Id);

        Assert.Equal(2, original.Rules.Count);
        Assert.Single(updated.Rules);
        Assert.Same(second, updated.Rules[0]);
        Assert.Same(updated, unchanged);
    }

    [Fact]
    public void Move_ChangesTieBreakOrderWithoutMutatingOriginal()
    {
        var record = CreateRule(
            "Record",
            RecordingRuleAction.Record,
            priority: 100);

        var ignore = CreateRule(
            "Ignore",
            RecordingRuleAction.Ignore,
            priority: 100);

        var original = new RecordingRuleSet([record, ignore]);
        var moved = original.Move(ignore.Id, 0);

        var candidate = new RecordingCandidate(
            TestChannelId,
            ChannelMetadataSnapshot.Empty);

        Assert.Equal(
            RecordingRuleDecisionKind.Record,
            original.Evaluate(candidate).Kind);
        Assert.Equal(
            RecordingRuleDecisionKind.Ignore,
            moved.Evaluate(candidate).Kind);
        Assert.Same(record, original.Rules[0]);
        Assert.Same(ignore, moved.Rules[0]);
    }

    [Fact]
    public void Move_InvalidTargetOrMissingRule_IsRejected()
    {
        var first = CreateRule("First");
        var original = new RecordingRuleSet([first]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => original.Move(first.Id, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => original.Move(first.Id, 1));
        Assert.Throws<KeyNotFoundException>(
            () => original.Move(Guid.NewGuid(), 0));
        Assert.Throws<ArgumentException>(
            () => original.Move(Guid.Empty, 0));
    }

    private static RecordingRule CreateRule(
        string name,
        RecordingRuleAction action = RecordingRuleAction.Record,
        int priority = 0)
    {
        return new RecordingRule(
            Guid.NewGuid(),
            name,
            action,
            RecordingRuleCriteria.MatchAll,
            priority);
    }
}

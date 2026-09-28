using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingRulePersistenceTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public void RuleSet_DuplicateIdentifiers_AreRejected()
    {
        var id = Guid.NewGuid();

        var first = CreateRule(
            id,
            "First",
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll);

        var second = CreateRule(
            id,
            "Second",
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll);

        Assert.Throws<ArgumentException>(
            () => new RecordingRuleSet([first, second]));
    }

    [Fact]
    public void RuleSet_TooManyRules_AreRejected()
    {
        var rules = Enumerable.Range(
                0,
                RecordingRuleSet.MaximumRuleCount + 1)
            .Select(index => CreateRule(
                Guid.NewGuid(),
                $"Rule {index}",
                RecordingRuleAction.Record,
                RecordingRuleCriteria.MatchAll));

        Assert.Throws<ArgumentException>(
            () => new RecordingRuleSet(rules));
    }

    [Fact]
    public void JsonCodec_RoundTrip_PreservesOrderAndEveryCriterion()
    {
        var first = CreateRule(
            Guid.ParseExact(
                "11111111111111111111111111111111",
                "N"),
            "Primary",
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                channelId: TestChannelId,
                name: new RecordingTextPattern(
                    "ゾエア",
                    RecordingTextMatchMode.Contains),
                genre: new RecordingTextPattern(
                    "^ゲーム$",
                    RecordingTextMatchMode.Regex),
                description: new RecordingTextPattern(
                    "説明",
                    RecordingTextMatchMode.Exact),
                contentType: new RecordingTextPattern(
                    "video",
                    RecordingTextMatchMode.Contains),
                streamType: new RecordingTextPattern(
                    "FLV",
                    RecordingTextMatchMode.Exact),
                comment: new RecordingTextPattern(
                    "test",
                    RecordingTextMatchMode.Contains),
                minimumBitrateKbps: 1000,
                maximumBitrateKbps: 5000),
            priority: 200,
            isEnabled: true);

        var second = CreateRule(
            Guid.ParseExact(
                "22222222222222222222222222222222",
                "N"),
            "Disabled exclusion",
            RecordingRuleAction.Ignore,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(
                    "exclude",
                    RecordingTextMatchMode.Exact)),
            priority: -10,
            isEnabled: false);

        var original = new RecordingRuleSet([first, second]);

        var json = RecordingRuleJsonCodec.Serialize(original);
        var restored = RecordingRuleJsonCodec.Deserialize(json);

        Assert.Equal(2, restored.Rules.Count);

        var restoredFirst = restored.Rules[0];

        Assert.Equal(first.Id, restoredFirst.Id);
        Assert.Equal(first.Name, restoredFirst.Name);
        Assert.Equal(first.Action, restoredFirst.Action);
        Assert.Equal(first.Priority, restoredFirst.Priority);
        Assert.Equal(first.IsEnabled, restoredFirst.IsEnabled);
        Assert.Equal(
            TestChannelId,
            restoredFirst.Criteria.ChannelId);
        AssertPattern(
            restoredFirst.Criteria.Name,
            "ゾエア",
            RecordingTextMatchMode.Contains);
        AssertPattern(
            restoredFirst.Criteria.Genre,
            "^ゲーム$",
            RecordingTextMatchMode.Regex);
        AssertPattern(
            restoredFirst.Criteria.Description,
            "説明",
            RecordingTextMatchMode.Exact);
        AssertPattern(
            restoredFirst.Criteria.ContentType,
            "video",
            RecordingTextMatchMode.Contains);
        AssertPattern(
            restoredFirst.Criteria.StreamType,
            "FLV",
            RecordingTextMatchMode.Exact);
        AssertPattern(
            restoredFirst.Criteria.Comment,
            "test",
            RecordingTextMatchMode.Contains);
        Assert.Equal(
            1000,
            restoredFirst.Criteria.MinimumBitrateKbps);
        Assert.Equal(
            5000,
            restoredFirst.Criteria.MaximumBitrateKbps);

        var restoredSecond = restored.Rules[1];

        Assert.Equal(second.Id, restoredSecond.Id);
        Assert.False(restoredSecond.IsEnabled);
        Assert.Equal(
            RecordingRuleAction.Ignore,
            restoredSecond.Action);
        Assert.Equal(-10, restoredSecond.Priority);
    }

    [Fact]
    public void JsonCodec_UsesExplicitVersionAndStableEnumTokens()
    {
        var ruleSet = new RecordingRuleSet(
            [
                CreateRule(
                    Guid.ParseExact(
                        "11111111111111111111111111111111",
                        "N"),
                    "Example",
                    RecordingRuleAction.Ignore,
                    new RecordingRuleCriteria(
                        name: new RecordingTextPattern(
                            "Nova",
                            RecordingTextMatchMode.Regex))),
            ]);

        var json = RecordingRuleJsonCodec.Serialize(ruleSet);

        Assert.Contains(
            "\"version\": 1",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"action\": \"ignore\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mode\": \"regex\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCodec_MatchAllIsExplicitAndRoundTrips()
    {
        var rule = CreateRule(
            Guid.ParseExact(
                "11111111111111111111111111111111",
                "N"),
            "Everything",
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll);

        var json = RecordingRuleJsonCodec.Serialize(
            new RecordingRuleSet([rule]));
        var restored = RecordingRuleJsonCodec.Deserialize(json);

        Assert.Contains(
            "\"matchAll\": true",
            json,
            StringComparison.Ordinal);
        Assert.True(restored.Rules[0].Criteria.IsMatchAll);
    }

    [Fact]
    public void JsonCodec_LegacyEmptyCriteriaPreservesSchemaV1MatchAllSemantics()
    {
        const string Json =
            """
            {
              "version": 1,
              "rules": [
                {
                  "id": "11111111111111111111111111111111",
                  "name": "Everything",
                  "action": "record",
                  "priority": 0,
                  "enabled": true,
                  "criteria": {}
                }
              ]
            }
            """;

        var restored = RecordingRuleJsonCodec.Deserialize(Json);

        Assert.True(restored.Rules[0].Criteria.IsMatchAll);
    }

    [Fact]
    public void JsonCodec_UnsupportedVersion_IsRejected()
    {
        const string Json =
            """
            {
              "version": 2,
              "rules": []
            }
            """;

        Assert.Throws<NotSupportedException>(
            () => RecordingRuleJsonCodec.Deserialize(Json));
    }

    [Fact]
    public void JsonCodec_UnknownField_IsRejected()
    {
        const string Json =
            """
            {
              "version": 1,
              "rules": [],
              "futureField": true
            }
            """;

        Assert.Throws<FormatException>(
            () => RecordingRuleJsonCodec.Deserialize(Json));
    }

    [Fact]
    public void JsonCodec_UnknownAction_IsRejected()
    {
        const string Json =
            """
            {
              "version": 1,
              "rules": [
                {
                  "id": "11111111111111111111111111111111",
                  "name": "Example",
                  "action": "future",
                  "priority": 0,
                  "enabled": true,
                  "criteria": {}
                }
              ]
            }
            """;

        Assert.Throws<FormatException>(
            () => RecordingRuleJsonCodec.Deserialize(Json));
    }

    [Fact]
    public void JsonCodec_InvalidRegex_IsRejected()
    {
        const string Json =
            """
            {
              "version": 1,
              "rules": [
                {
                  "id": "11111111111111111111111111111111",
                  "name": "Example",
                  "action": "record",
                  "priority": 0,
                  "enabled": true,
                  "criteria": {
                    "name": {
                      "value": "[",
                      "mode": "regex"
                    }
                  }
                }
              ]
            }
            """;

        Assert.Throws<FormatException>(
            () => RecordingRuleJsonCodec.Deserialize(Json));
    }

    [Fact]
    public void JsonCodec_RestoredRuleSet_EvaluatesLikeOriginal()
    {
        var rule = CreateRule(
            Guid.NewGuid(),
            "Games",
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                genre: new RecordingTextPattern("ゲーム")),
            priority: 50);

        var original = new RecordingRuleSet([rule]);
        var restored = RecordingRuleJsonCodec.Deserialize(
            RecordingRuleJsonCodec.Serialize(original));

        var candidate = new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(
                name: "Channel",
                genre: "ゲーム配信",
                bitrateKbps: 2000));

        var originalDecision = original.Evaluate(candidate);
        var restoredDecision = restored.Evaluate(candidate);

        Assert.Equal(
            originalDecision.Kind,
            restoredDecision.Kind);
        Assert.Equal(
            originalDecision.MatchedRule?.Id,
            restoredDecision.MatchedRule?.Id);
    }

    private static RecordingRule CreateRule(
        Guid id,
        string name,
        RecordingRuleAction action,
        RecordingRuleCriteria criteria,
        int priority = 0,
        bool isEnabled = true)
    {
        return new RecordingRule(
            id,
            name,
            action,
            criteria,
            priority,
            isEnabled);
    }

    private static void AssertPattern(
        RecordingTextPattern? pattern,
        string value,
        RecordingTextMatchMode mode)
    {
        Assert.NotNull(pattern);
        Assert.Equal(value, pattern.Value);
        Assert.Equal(mode, pattern.Mode);
    }
}

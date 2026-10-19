using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    [Fact]
    public async Task StopSuppressionAndAllowDoNotWriteIsolatedRuleFileOrChangeRepositoryRevision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Nova-suppression-rule-immutability", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "recording-rules.json");
        var rule = RecordRule();
        try
        {
            await RecordingRuleFileStore.SaveAsync(path, new([rule]));
            await using var repository = new GenesisRecordingRuleRepository(path);
            var before = await repository.GetSnapshotAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            var sink = new UserStopSink();
            await using var f = new Fixture(new([rule]), new GenesisRecordingRuleFileProvider(path), sink: sink);
            await f.Controller.ObserveAsync([Channel()]);
            var session = f.Runtime.GetState(ChannelId()).Session!;
            var committed = WaitForCommitted(f.Controller);
            await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
            sink.Finish(RecordingSinkStopResultKind.Stopped);
            await committed;
            Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
            await f.Controller.AllowAutomaticAsync(ChannelId(), rule.Id);
            Assert.Equal(1, sink.Starts);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            var after = await repository.GetSnapshotAsync();
            Assert.Equal(before.Revision, after.Revision);
            Assert.Equal(rule, Assert.Single(after.Rules!.Rules));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task RegexTimeoutRetainsSuppressionAndEvaluatesOnceWithoutFallback()
    {
        var r = RecordRule(); var count = 0; RecordingRuleDecision? last = null;
        var sink = new UserStopSink();
        await using var f = new Fixture(new([r]), evaluate: (rules, candidate) => { count++; return last = rules.Evaluate(candidate); }, sink: sink);
        await f.Controller.ObserveAsync([Channel()]);
        var s = f.Runtime.GetState(ChannelId()).Session!; var committed = WaitForCommitted(f.Controller);
        await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId); sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        var pattern = new RecordingTextPattern("^(a+)+$", RecordingTextMatchMode.Regex);
        var input = new string('a', 200) + "!";
        // Prove this fixture exercises timeout rather than an ordinary non-match.
        var regex = (System.Text.RegularExpressions.Regex)typeof(RecordingTextPattern)
            .GetField("regex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pattern)!;
        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() => regex.IsMatch(input));
        f.RuleSetProvider.RuleSet = new([new RecordingRule(r.Id, r.Name, r.Action, new(name: pattern))]);
        await f.Controller.ObserveAsync([Channel() with { Name = input }]);
        Assert.Equal(2, count); Assert.Equal(RecordingRuleDecisionKind.NoMatch, last!.Kind);
        Assert.Single(f.Controller.SuppressionRows); Assert.Equal(1, sink.Starts);
        f.RuleSetProvider.RuleSet = new([r]);
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Single(f.Controller.SuppressionRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyObservedDisableClearsBeforeReenable(bool observed)
    {
        var r = RecordRule(); var sink = new UserStopSink(); await using var f = new Fixture(new([r]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var s = f.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(f.Controller); await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId);
        sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        f.RuleSetProvider.RuleSet = new([new RecordingRule(r.Id, r.Name, r.Action, r.Criteria, isEnabled: false)]);
        if (observed) { await f.Controller.ObserveAsync([]); Assert.Empty(f.Controller.SuppressionRows); }
        f.RuleSetProvider.RuleSet = new([r]);
        Assert.Equal(observed ? 1 : 0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Equal(observed ? 0 : 1, f.Controller.SuppressionRows.Count);
    }

    [Fact]
    public async Task RuleReadFailureRetainsSuppressionAndNewControllerHasNoPersistedIntent()
    {
        var r = RecordRule(); var provider = new GatedRuleProvider(new([r]), new(), new()); var sink = new UserStopSink();
        await using var f = new Fixture(new([r]), provider, sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var s = f.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(f.Controller); await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId);
        sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        provider.Fail = true;
        Assert.False((await f.Controller.ObserveAsync([Channel()])).RuleSetAvailable);
        Assert.Single(f.Controller.SuppressionRows); Assert.Equal(1, sink.Starts);
        provider.Fail = false;
        await f.Controller.DisposeAsync(); Assert.Empty(f.Controller.SuppressionRows);
        await using var fresh = new GenesisAutomaticRecordingEvaluationController(f.Runtime, f.OwnerProvider, provider);
        Assert.Empty(fresh.SuppressionRows);
        Assert.Equal(1, (await fresh.ObserveAsync([Channel()])).StartedCount);
        Assert.Equal(2, sink.Starts);
    }
}

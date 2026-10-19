using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    [Fact]
    public async Task ManualControllerCleanupCannotEraseCapturedHistory()
    {
        var history = new GenesisRecordingHistoryReadModel();
        await using var fixture = new Fixture(new([]), terminalObserver: history);
        await using var manual = new GenesisManualRecordingSessionController(fixture.OwnerProvider, fixture.Runtime);
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SourceProvider.StreamFactory = () => new DirectoryTestStream(read);
        Assert.Equal(GenesisManualRecordingCommandKind.Started, (await manual.StartAsync(Channel())).Kind);
        var session = fixture.Runtime.GetState(ChannelId()).Session!;
        await read.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _ = await manual.StopAsync(ChannelId(), session.SessionId);
        Assert.Empty((await manual.GetActiveRecordingSessionsAsync()).Sessions);
        var fact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(session, fact.Session);
        Assert.Equal(RecordingSessionOrigin.Manual, fact.Session.Origin);
        Assert.Null(fact.Session.AutomaticRuleId);
        Assert.Equal(Channel().Name, fact.ChannelName);
        Assert.Equal("FLV", fact.StreamType);
        Assert.Equal("flv", fact.RequestedFileExtension);
        // This existing non-AVC fixture follows the recorder's fallback path.
        // Preserve its actual writer result rather than inventing no-start.
        Assert.Equal(RecordingSinkSessionLifecycleKind.Completed, fact.Kind);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, fact.WriteResult!.CompletionKind);
        Assert.True(File.Exists(fact.WriteResult.Path));
    }

    [Fact]
    public async Task AutomaticHistoryPreservesWinnerAcrossStopSuppressionAllowAndSecondSession()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var rule = RecordRule();
        await using var fixture = new Fixture(new([rule]), terminalObserver: history);
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        var first = fixture.Runtime.GetState(ChannelId()).Session!;
        await StopAndCommit(fixture);
        var firstFact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(first, firstFact.Session);
        Assert.Equal(rule.Id, firstFact.Session.AutomaticRuleId);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, firstFact.Session.Origin);
        Assert.Equal(0, (await fixture.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Single(fixture.Controller.SuppressionRows);
        _ = await fixture.Controller.AllowAutomaticAsync(ChannelId(), rule.Id);
        Assert.Empty(fixture.Controller.SuppressionRows);
        Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
        Assert.Equal(1, (await fixture.Controller.ObserveAsync([Channel()])).StartedCount);
        var second = fixture.Runtime.GetState(ChannelId()).Session!;
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(rule.Id, second.AutomaticRuleId);
        Assert.Single(await fixture.Runtime.GetActiveRecordingSessionsAsync());
        await StopAndCommit(fixture);
        Assert.Equal(2, history.GetSnapshot().Entries.Count);
        Assert.Contains(history.GetSnapshot().Entries, e => e.Session.SessionId == first.SessionId);
        Assert.Contains(history.GetSnapshot().Entries, e => e.Session.SessionId == second.SessionId);
    }

    [Fact]
    public async Task AutomaticRuleReplacementDoesNotRewriteRecordedWinningRuleId()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var original = RecordRule();
        await using var fixture = new Fixture(new([original]), terminalObserver: history);
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        var session = fixture.Runtime.GetState(ChannelId()).Session!;
        fixture.RuleSetProvider.RuleSet = new([new RecordingRule(Guid.NewGuid(), "renamed current definition",
            RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll)]);
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        _ = await fixture.Runtime.StopAsync(ChannelId());
        var fact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(session, fact.Session);
        Assert.Equal(original.Id, fact.Session.AutomaticRuleId);
        Assert.Equal(Channel().Name, fact.ChannelName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnadmittedControllerFailureCannotCreateCompletedHistory(bool automatic)
    {
        var history = new GenesisRecordingHistoryReadModel();
        await using var fixture = new Fixture(new([RecordRule()]), terminalObserver: history);
        fixture.SourceProvider.ThrowOnOpen = true;
        if (automatic)
            Assert.Equal(1, (await fixture.Controller.ObserveAsync([Channel()])).FailedCount);
        else
        {
            await using var manual = new GenesisManualRecordingSessionController(fixture.OwnerProvider, fixture.Runtime);
            Assert.Equal(GenesisManualRecordingCommandKind.Failed, (await manual.StartAsync(Channel())).Kind);
        }
        Assert.Empty(history.GetSnapshot().Entries);
        Assert.False(history.GetSnapshot().HasGap);
        Assert.Empty(await fixture.Runtime.GetActiveRecordingSessionsAsync());
    }
}

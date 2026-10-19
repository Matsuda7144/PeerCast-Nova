using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    private static Task WaitForCommitted(GenesisAutomaticRecordingEvaluationController controller, int expectedCount = 1)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? changed = null;
        changed = (_, _) =>
        {
            if (controller.SuppressionRows.Count(e => e.State == GenesisAutomaticRecordingSuppressionState.Committed) < expectedCount) return;
            controller.SuppressionChanged -= changed;
            completed.TrySetResult();
        };
        controller.SuppressionChanged += changed;
        changed(null, EventArgs.Empty);
        return completed.Task;
    }

    private static async Task StopAndCommit(Fixture f)
    {
        var session = f.Runtime.GetState(ChannelId()).Session!;
        var completion = WaitForCommitted(f.Controller);
        Assert.True(f.Controller.CanStopAutomatic(ChannelId(), session.SessionId));
        Assert.Equal("停止処理中です。", await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId));
        await completion;
        Assert.Null(f.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(GenesisAutomaticRecordingSuppressionState.Committed, Assert.Single(f.Controller.SuppressionRows).State);
    }

    [Fact]
    public async Task AcceptedStopSuppressesSameWinnerAcrossPassesAndAllowHasNoImmediateEffects()
    {
        var rule = RecordRule();
        await using var f = new Fixture(new([rule]));
        await f.Controller.ObserveAsync([Channel()]);
        await StopAndCommit(f);
        var row = Assert.Single(f.Controller.SuppressionRows);
        Assert.Equal(rule.Id, row.RuleId);
        Assert.Equal(ChannelId(), row.ChannelId);
        for (var i = 0; i < 3; i++) Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Equal(1, f.SourceProvider.OpenCount);
        Assert.Equal(1, f.OwnerProvider.AttachCount);
        Assert.True(f.Controller.CanAllowAutomatic(ChannelId(), rule.Id));
        Assert.Contains("次回", await f.Controller.AllowAutomaticAsync(ChannelId(), rule.Id));
        Assert.Empty(f.Controller.SuppressionRows);
        Assert.Contains("変更はありません", await f.Controller.AllowAutomaticAsync(ChannelId(), rule.Id));
        Assert.Equal(1, f.SourceProvider.OpenCount);
        Assert.Equal(1, f.OwnerProvider.AttachCount);
        Assert.Equal(1, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
    }

    [Theory]
    [InlineData("same", true, 0)]
    [InlineData("edit", true, 0)]
    [InlineData("unrelated-edit", true, 0)]
    [InlineData("missing", true, 0)]
    [InlineData("invalid-source", true, 0)]
    [InlineData("no-match", true, 0)]
    [InlineData("different", false, 1)]
    [InlineData("ignore", false, 0)]
    [InlineData("disable", false, 0)]
    [InlineData("delete", false, 0)]
    public async Task ObservedPolicyClearsOnlyProvenWinnerOrRuleChanges(string scenario, bool retains, int starts)
    {
        var r = RecordRule();
        await using var f = new Fixture(new([r]));
        await f.Controller.ObserveAsync([Channel()]); await StopAndCommit(f);
        IReadOnlyList<GenesisChannelItem> channels = [Channel()];
        f.RuleSetProvider.RuleSet = scenario switch
        {
            "edit" => new([new RecordingRule(r.Id, "edited", RecordingRuleAction.Record, r.Criteria, 120)]),
            "unrelated-edit" => new([r, new RecordingRule(Guid.NewGuid(), "unrelated edited", RecordingRuleAction.Ignore, new(channelId: new ChannelId(Guid.NewGuid())), 999)]),
            "no-match" => new([new RecordingRule(r.Id, "temporary mismatch", RecordingRuleAction.Record, new(channelId: new ChannelId(Guid.NewGuid())))]),
            "different" => new([r, new RecordingRule(Guid.NewGuid(), "higher winner", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll, 999)]),
            "ignore" => new([r, new RecordingRule(Guid.NewGuid(), "higher ignore", RecordingRuleAction.Ignore, RecordingRuleCriteria.MatchAll, 999)]),
            "disable" => new([new RecordingRule(r.Id, r.Name, r.Action, r.Criteria, r.Priority, false)]),
            "delete" => new([]),
            _ => new([r]),
        };
        if (scenario is "missing" or "disable" or "delete") channels = [];
        if (scenario == "invalid-source") channels = [Channel() with { Source = "invalid" }];
        Assert.Equal(starts, (await f.Controller.ObserveAsync(channels)).StartedCount);
        Assert.Equal(retains ? 1 : 0, f.Controller.SuppressionRows.Count);
        if (retains)
        {
            f.RuleSetProvider.RuleSet = new([r]);
            Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
            Assert.Equal(1, f.SourceProvider.OpenCount);
        }
    }

    [Fact]
    public async Task ReorderClearsOnlyChannelWhoseWinnerChangedAndRetainsOtherPair()
    {
        var r = RecordRule();
        var specific = new RecordingRule(Guid.NewGuid(), "specific", RecordingRuleAction.Record, new(channelId: ChannelId()));
        var otherId = new ChannelId(Guid.NewGuid());
        var other = Channel() with { ChannelId = otherId.Value.ToString("N"), Source = $"pcp://tracker.example:7144/channel/{otherId.Value:N}" };
        var sink = new UserStopSink();
        await using var f = new Fixture(new([r, specific]), sink: sink);
        await f.Controller.ObserveAsync([Channel(), other]);
        var committed = WaitForCommitted(f.Controller, 2);
        await f.Controller.StopAutomaticAsync(ChannelId(), f.Runtime.GetState(ChannelId()).Session!.SessionId);
        await f.Controller.StopAutomaticAsync(otherId, f.Runtime.GetState(otherId).Session!.SessionId);
        sink.Finish(RecordingSinkStopResultKind.Stopped);
        await committed;
        f.RuleSetProvider.RuleSet = new([specific, r]);
        Assert.Equal(1, (await f.Controller.ObserveAsync([Channel(), other])).StartedCount);
        var retained = Assert.Single(f.Controller.SuppressionRows);
        Assert.Equal(otherId, retained.ChannelId);
        Assert.Equal(r.Id, retained.RuleId);
        Assert.Equal(specific.Id, f.Runtime.GetState(ChannelId()).AutomationState.ActiveRuleId);
        Assert.Null(f.Runtime.GetState(otherId).Session);
    }

    [Fact]
    public async Task StartedByACurrentWinnerBStopSuppressesBAndQueuedSaveIsNotAuthority()
    {
        var a = RecordRule(); var b = new RecordingRule(Guid.NewGuid(), "b", a.Action, a.Criteria, 100);
        await using var f = new Fixture(new([a]));
        await f.Controller.ObserveAsync([Channel()]);
        var session = f.Runtime.GetState(ChannelId()).Session!;
        f.RuleSetProvider.RuleSet = new([a, b]);
        await f.Controller.ObserveAsync([Channel()]);
        Assert.Same(session, f.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(a.Id, session.AutomaticRuleId);
        Assert.Equal(b.Id, f.Runtime.GetState(ChannelId()).AutomationState.ActiveRuleId);
        f.RuleSetProvider.RuleSet = new([a]); // saved, not yet evaluated
        await StopAndCommit(f);
        Assert.Equal(b.Id, Assert.Single(f.Controller.SuppressionRows).RuleId);
        Assert.True(f.Runtime.TryGetAutomaticStopAdmission(ChannelId(), session.SessionId, out var receipt));
        Assert.Equal(b.Id, receipt!.ActiveRuleId);
        Assert.Equal(1, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Empty(f.Controller.SuppressionRows);
    }

    [Fact]
    public async Task ManualStartStopAndAllowPreserveManualContractAndSuppression()
    {
        var r = RecordRule();
        await using var f = new Fixture(new([r]));
        await f.Controller.ObserveAsync([Channel()]); await StopAndCommit(f);
        await using var manual = new GenesisManualRecordingSessionController(f.OwnerProvider, f.Runtime);
        Assert.Equal(GenesisManualRecordingCommandKind.Started, (await manual.StartAsync(Channel())).Kind);
        var s = f.Runtime.GetState(ChannelId()).Session!;
        Assert.False(f.Controller.CanStopAutomatic(ChannelId(), s.SessionId));
        Assert.Contains("更新", await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId));
        Assert.Single(f.Controller.SuppressionRows);
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StoppedCount);
        Assert.Same(s, f.Runtime.GetState(ChannelId()).Session);
        await manual.StopAsync(ChannelId(), s.SessionId);
        Assert.Single(f.Controller.SuppressionRows);
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        await manual.StartAsync(Channel());
        s = f.Runtime.GetState(ChannelId()).Session!;
        await f.Controller.AllowAutomaticAsync(ChannelId(), r.Id);
        Assert.Same(s, f.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StoppedCount);
    }

    [Fact]
    public async Task PendingStopRejectsDuplicatesAndAllowWhileCancellationCannotDropFence()
    {
        var r = RecordRule(); var stream = new GatedStopStream();
        await using var f = new Fixture(new([r])); f.SourceProvider.StreamFactory = () => stream;
        try
        {
            await f.Controller.ObserveAsync([Channel()]);
            var s = f.Runtime.GetState(ChannelId()).Session!;
            using var caller = new CancellationTokenSource();
            var committed = WaitForCommitted(f.Controller);
            Assert.Equal("停止処理中です。", await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId, caller.Token));
            await stream.DisposeEntered.Task;
            caller.Cancel();
            var pending = Assert.Single(f.Controller.SuppressionRows);
            Assert.Equal(GenesisAutomaticRecordingSuppressionState.AdmittedPending, pending.State);
            Assert.False(f.Controller.CanStopAutomatic(ChannelId(), s.SessionId));
            Assert.False(f.Controller.CanAllowAutomatic(ChannelId(), r.Id));
            Assert.Contains("更新", await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId));
            Assert.Contains("変更はありません", await f.Controller.AllowAutomaticAsync(ChannelId(), r.Id));
            Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
            Assert.Single(await f.Runtime.GetActiveRecordingSessionsAsync());
            stream.ReleaseDispose.SetResult(); await committed;
            Assert.Equal(1, stream.DisposeCount);
            Assert.Equal(1, f.SourceProvider.OpenCount);
        }
        finally { stream.ReleaseDispose.TrySetResult(); }
    }

    [Fact]
    public async Task ShutdownDrainsPendingObserverClosesCommandsAndPreventsLatePublication()
    {
        var r = RecordRule(); var stream = new GatedStopStream();
        await using var f = new Fixture(new([r])); f.SourceProvider.StreamFactory = () => stream;
        try
        {
            await f.Controller.ObserveAsync([Channel()]); var s = f.Runtime.GetState(ChannelId()).Session!;
            await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId); await stream.DisposeEntered.Task;
            var shutdown = f.Controller.DisposeAsync().AsTask();
            var events = 0; f.Controller.SuppressionChanged += (_, _) => events++;
            Assert.False(shutdown.IsCompleted);
            Assert.False(f.Controller.CanStopAutomatic(ChannelId(), s.SessionId));
            Assert.False(f.Controller.CanAllowAutomatic(ChannelId(), r.Id));
            Assert.Contains("停止できません", await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId));
            Assert.Contains("解除できません", await f.Controller.AllowAutomaticAsync(ChannelId(), r.Id));
            stream.ReleaseDispose.SetResult(); await shutdown;
            Assert.Equal(0, events); Assert.Empty(f.Controller.SuppressionRows);
            Assert.Null(f.Runtime.GetState(ChannelId()).Session);
        }
        finally { stream.ReleaseDispose.TrySetResult(); }
    }

    [Fact]
    public async Task RuleDecisionIsEvaluatedOncePerCandidateIncludingSuppressedPass()
    {
        var count = 0; var r = RecordRule();
        await using var f = new Fixture(new([r]), evaluate: (rules, candidate) => { count++; return rules.Evaluate(candidate); });
        await f.Controller.ObserveAsync([Channel(), Channel()]); Assert.Equal(1, count);
        await StopAndCommit(f);
        await f.Controller.ObserveAsync([Channel()]); Assert.Equal(2, count);
        Assert.Equal(1, f.SourceProvider.OpenCount);
    }

    [Fact]
    public async Task PreCancelledAndStaleStopDoNotCreateSuppressionOrStopReplacement()
    {
        await using var f = new Fixture(new([RecordRule()]));
        await f.Controller.ObserveAsync([Channel()]); var s = f.Runtime.GetState(ChannelId()).Session!;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId, cancellation.Token);
        await f.Controller.StopAutomaticAsync(ChannelId(), new RecordingSessionId(Guid.NewGuid()));
        Assert.Empty(f.Controller.SuppressionRows); Assert.Same(s, f.Runtime.GetState(ChannelId()).Session);
    }

    private sealed class GatedStopStream : BlockingFlvStream
    {
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult(); await ReleaseDispose.Task; DisposeCount++;
            await base.DisposeAsync();
        }
    }
}

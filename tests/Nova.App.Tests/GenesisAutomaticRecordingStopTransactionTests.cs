using System.Reflection;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    [Theory]
    [InlineData(RecordingSinkStopResultKind.Stopped, "停止")]
    [InlineData(RecordingSinkStopResultKind.NoDecodableVideoStart, "作成されません")]
    [InlineData(RecordingSinkStopResultKind.Faulted, "保存に失敗")]
    public async Task AutomaticTransactionCommitsCanonicalTypedTerminalAfterCallerCancellation(RecordingSinkStopResultKind kind, string message)
    {
        var sink = new UserStopSink(); var rule = RecordRule();
        await using var f = new Fixture(new([rule]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        using var cancellation = new CancellationTokenSource(); var committed = WaitForCommitted(f.Controller);
        await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId, cancellation.Token);
        cancellation.Cancel(); Assert.Single(f.Controller.SuppressionRows);
        Assert.False(f.Controller.CanStopAutomatic(ChannelId(), session.SessionId));
        await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
        Assert.Equal(1, sink.AdmissionCalls);
        Assert.True(f.Runtime.TryGetAutomaticStopAdmission(ChannelId(), session.SessionId, out var receipt));
        Assert.Equal(rule.Id, receipt!.ActiveRuleId);
        sink.Finish(kind); await committed;
        Assert.Contains(message, Assert.Single(f.Controller.SuppressionRows).StatusText);
        Assert.Null(f.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Equal(1, sink.Starts);
        Assert.Equal(0, sink.LegacyStops);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("AlreadyTerminal")]
    [InlineData("Unavailable")]
    [InlineData("Failure")]
    public async Task NotAdmittedRollsBackReservationWithoutLegacyFallbackOrFalseSuppression(string reason)
    {
        var sink = new UserStopSink { Reject = reason }; var rule = RecordRule();
        await using var f = new Fixture(new([rule]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        Assert.Contains("停止できません", await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId));
        Assert.Empty(f.Controller.SuppressionRows); Assert.Same(session, f.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(0, sink.LegacyStops); Assert.Equal(1, sink.AdmissionCalls);
        sink.Reject = null; var committed = WaitForCommitted(f.Controller);
        await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId); sink.Finish(RecordingSinkStopResultKind.Stopped);
        await committed; Assert.Single(f.Controller.SuppressionRows); Assert.Equal(2, sink.AdmissionCalls);
    }

    [Fact]
    public async Task FullSuppressionCapacityRejectsAutomaticAdmissionBeforeSinkEffect()
    {
        var sink = new UserStopSink(); var rule = RecordRule();
        await using var f = new Fixture(new([rule]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        var registry = (GenesisAutomaticRecordingSuppression)typeof(GenesisAutomaticRecordingEvaluationController)
            .GetField("_suppression", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Controller)!;
        for (var i = 0; i < 4096; i++) Assert.NotNull(registry.Reserve(new ChannelId(Guid.NewGuid()), new RecordingSessionId(Guid.NewGuid())));
        Assert.Contains("準備できない", await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId));
        Assert.Equal(0, sink.AdmissionCalls); Assert.Equal(0, sink.LegacyStops);
        Assert.Same(session, f.Runtime.GetState(ChannelId()).Session);
    }

    [Fact]
    public async Task PendingChannelDoesNotFenceOtherChannelAndSameRuleOtherChannelStarts()
    {
        var sink = new UserStopSink(); var rule = RecordRule();
        await using var f = new Fixture(new([rule]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(f.Controller); await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
        var id = Guid.NewGuid().ToString("N"); var other = Channel() with { ChannelId = id, Source = $"pcp://tracker.example:7144/channel/{id}" };
        var pass = await f.Controller.ObserveAsync([Channel(), other]);
        Assert.Equal(1, pass.StartedCount); Assert.Equal(2, sink.Starts);
        Assert.Single(f.Controller.SuppressionRows);
        sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        await f.Controller.AllowAutomaticAsync(ChannelId(), rule.Id);
        Assert.Equal(2, sink.Starts); Assert.NotNull(f.Runtime.GetState(new ChannelId(Guid.ParseExact(id, "N"))).Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightPassCompletesOldSnapshotThenStopCapturesWinnerBeforeNextPass(bool changedWinner)
    {
        var a = RecordRule(); var b = new RecordingRule(Guid.NewGuid(), "next", a.Action, a.Criteria, 999);
        var snapshotTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new GatedRuleProvider(new([a]), snapshotTaken, release);
        var sink = new UserStopSink();
        await using var f = new Fixture(new([a]), provider, sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        provider.Pause = true;
        var pass = f.Controller.ObserveAsync([Channel()]).AsTask(); await snapshotTaken.Task;
        provider.Rules = changedWinner ? new([b]) : new([a]);
        var stop = f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId).AsTask(); Assert.False(stop.IsCompleted);
        release.SetResult(); await pass; await stop;
        Assert.Equal(a.Id, Assert.Single(f.Controller.SuppressionRows).RuleId);
        var committed = WaitForCommitted(f.Controller); sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        Assert.Equal(changedWinner ? 1 : 0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Equal(changedWinner ? 0 : 1, f.Controller.SuppressionRows.Count);
        Assert.Equal(changedWinner ? b.Id : (Guid?)null, f.Runtime.GetState(ChannelId()).AutomationState.ActiveRuleId);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("delete")]
    [InlineData("reorder")]
    [InlineData("no-match")]
    public async Task RuleMutationDuringPendingStopWaitsForNextObservedSnapshot(string mutation)
    {
        var a = RecordRule(); var b = new RecordingRule(Guid.NewGuid(), "other", a.Action, a.Criteria);
        var sink = new UserStopSink(); await using var f = new Fixture(new([a, b]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(f.Controller); await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
        f.RuleSetProvider.RuleSet = mutation switch
        {
            "delete" => new([b]),
            "disable" => new([new RecordingRule(a.Id, a.Name, a.Action, a.Criteria, isEnabled: false), b]),
            "reorder" => new([b, a]),
            _ => new([new RecordingRule(a.Id, a.Name, a.Action, new(channelId: new ChannelId(Guid.NewGuid())))])
        };
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StartedCount);
        Assert.Equal(GenesisAutomaticRecordingSuppressionState.AdmittedPending, Assert.Single(f.Controller.SuppressionRows).State);
        sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        var next = await f.Controller.ObserveAsync([Channel()]);
        Assert.Equal(mutation == "no-match" ? 0 : 1, next.StartedCount);
        Assert.Equal(mutation == "no-match" ? 1 : 0, f.Controller.SuppressionRows.Count);
    }

    [Fact]
    public async Task ConcurrentAutomaticStopAndManualStartUseCoreGenerationSerialization()
    {
        var sink = new UserStopSink(); await using var f = new Fixture(new([RecordRule()]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var session = f.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(f.Controller); await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
        var manual = f.Runtime.StartManualAsync(GenesisRecordingCandidateAdapter.TryCreate(Channel(), out var candidate) ? candidate!.Candidate : null!).AsTask();
        Assert.False(manual.IsCompleted); sink.Finish(RecordingSinkStopResultKind.Stopped); await committed; await manual;
        Assert.Equal(RecordingSessionOrigin.Manual, f.Runtime.GetState(ChannelId()).Session!.Origin);
        Assert.Single(f.Controller.SuppressionRows); Assert.Equal(1, sink.AdmissionCalls);
        Assert.Equal(0, (await f.Controller.ObserveAsync([Channel()])).StoppedCount);
    }

    private sealed class GatedRuleProvider(RecordingRuleSet rules, TaskCompletionSource taken, TaskCompletionSource release) : IGenesisRecordingRuleSetProvider
    {
        public RecordingRuleSet Rules { get; set; } = rules;
        public bool Pause { get; set; }
        public bool Fail { get; set; }
        public async ValueTask<RecordingRuleSet> GetSnapshotAsync(CancellationToken token = default)
        {
            if (Fail) throw new IOException("private rule path");
            var snapshot = Rules;
            if (Pause) { Pause = false; taken.SetResult(); await release.Task.WaitAsync(token); }
            return snapshot;
        }
    }

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.Completed)]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted)]
    public async Task NaturalTerminalWithoutUserAdmissionCreatesNoSuppressionAndStaleStopCannotAffectReplacement(RecordingSinkSessionLifecycleKind terminal)
    {
        var sink = new UserStopSink();
        await using var f = new Fixture(new([RecordRule()]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]);
        var old = f.Runtime.GetState(ChannelId()).Session!;
        sink.Lifecycle = terminal;
        await f.Runtime.GetActiveRecordingSessionsAsync();
        Assert.Null(f.Runtime.GetState(ChannelId()).Session);
        Assert.Empty(f.Controller.SuppressionRows);
        await f.Controller.StopAutomaticAsync(ChannelId(), old.SessionId);
        Assert.Equal(0, sink.AdmissionCalls);
        Assert.Empty(f.Controller.SuppressionRows);
        await f.Controller.ObserveAsync([Channel()]);
        var current = f.Runtime.GetState(ChannelId()).Session!;
        Assert.NotEqual(old.SessionId, current.SessionId);
        await f.Controller.StopAutomaticAsync(ChannelId(), old.SessionId);
        Assert.Equal(current.SessionId, f.Runtime.GetState(ChannelId()).Session!.SessionId);
        Assert.Equal(0, sink.AdmissionCalls);
        Assert.Empty(f.Controller.SuppressionRows);
    }

    [Fact]
    public async Task UnknownCurrentWinnerRejectsAutomaticStopWithoutSinkEffect()
    {
        var sink = new UserStopSink();
        await using var f = new Fixture(new([RecordRule()]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]);
        var state = f.Runtime.GetState(ChannelId());
        var session = state.Session!;
        var field = typeof(RecordingAutomationExecutionState).GetField("<AutomationState>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var original = state.AutomationState;
        // Defensive inconsistent-state fixture; normal constructors forbid an unknown winner.
        field.SetValue(state, RecordingAutomationState.Manual);
        try
        {
            Assert.False(f.Controller.CanStopAutomatic(ChannelId(), session.SessionId));
            await f.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
            Assert.Equal(RecordingAutomaticStopNotAdmittedReason.ProvenanceUnavailable,
                (await f.Runtime.RequestAutomaticStopAsync(ChannelId(), session.SessionId)).Reason);
            Assert.Equal(0, sink.AdmissionCalls);
            Assert.Empty(f.Controller.SuppressionRows);
            Assert.Same(session, f.Runtime.GetState(ChannelId()).Session);
        }
        finally { field.SetValue(state, original); }
    }

    private sealed class UserStopSink : IRecordingStopAdmissionSink, IRecordingSinkSessionMonitor
    {
        private readonly Dictionary<RecordingSessionId, (RecordingSinkStopAdmission Receipt, TaskCompletionSource<RecordingSinkStopResult> Terminal)> _receipts = [];
        public int Starts { get; private set; }
        public int AdmissionCalls { get; private set; }
        public int LegacyStops { get; private set; }
        public string? Reject { get; set; }
        public bool BlockStart { get; set; }
        public RecordingSinkSessionLifecycleKind Lifecycle { get; set; } = RecordingSinkSessionLifecycleKind.Active;
        public bool TryGetSessionStatus(RecordingSinkSession session, out RecordingSinkSessionLifecycleStatus? status)
        {
            status = new(session, Lifecycle);
            return true;
        }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<RecordingSinkStartResult> StartAsync(RecordingSinkStartRequest request, CancellationToken token)
        {
            Starts++;
            Lifecycle = RecordingSinkSessionLifecycleKind.Active;
            if (BlockStart) { StartEntered.TrySetResult(); await ReleaseStart.Task.WaitAsync(token); }
            var session = request.Origin == RecordingSessionOrigin.Manual
                ? RecordingSinkSession.Manual(new(Guid.NewGuid()), request.Candidate.ChannelId)
                : new RecordingSinkSession(new(Guid.NewGuid()), request.Candidate.ChannelId, request.RuleId!.Value);
            return RecordingSinkStartResult.Started(session);
        }
        public ValueTask<RecordingSinkStopResult> StopAsync(RecordingSinkStopRequest request, CancellationToken token)
        { LegacyStops++; return ValueTask.FromResult(RecordingSinkStopResult.Stopped); }
        public RecordingSinkStopAdmissionResult RequestStopAdmission(RecordingSinkStopRequest request, CancellationToken token = default)
        {
            AdmissionCalls++;
            if (Reject == "Failure") throw new IOException("private source C:\\private pcp://private");
            if (Reject is not null) return RecordingSinkStopAdmissionResult.NotAdmitted(Enum.Parse<RecordingSinkStopNotAdmittedReason>(Reject));
            var terminal = new TaskCompletionSource<RecordingSinkStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receipt = new RecordingSinkStopAdmission(request.Session, terminal.Task);
            _receipts.Add(request.Session.SessionId, (receipt, terminal));
            return RecordingSinkStopAdmissionResult.Admitted(receipt);
        }
        public bool TryGetStopAdmission(RecordingSinkSession session, out RecordingSinkStopAdmission? admission)
        { admission = _receipts.GetValueOrDefault(session.SessionId).Receipt; return admission is not null; }
        public void Finish(RecordingSinkStopResultKind kind)
        {
            var result = kind switch
            {
                RecordingSinkStopResultKind.Faulted => RecordingSinkStopResult.Faulted,
                RecordingSinkStopResultKind.NoDecodableVideoStart => RecordingSinkStopResult.NoDecodableVideoStart,
                _ => RecordingSinkStopResult.Stopped
            };
            foreach (var owned in _receipts.Values) owned.Terminal.TrySetResult(result);
        }
    }
}

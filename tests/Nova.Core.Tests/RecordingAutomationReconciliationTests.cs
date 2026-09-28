using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingAutomationReconciliationTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.Completed)]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted)]
    public async Task Evaluate_TerminalSessionStillMatchingRecord_Restarts(
        RecordingSinkSessionLifecycleKind terminalKind)
    {
        var rule = CreateRecordRule();
        var sink = new MonitorSink(terminalKind);
        using var coordinator = new RecordingAutomationChannelCoordinator(
            TestChannelId,
            sink);

        var first = await coordinator.EvaluateAsync(
            CreateCandidate(),
            new RecordingRuleSet([rule]),
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            first.Kind);

        sink.MarkCurrentSessionTerminal(
            terminalKind);

        var second = await coordinator.EvaluateAsync(
            CreateCandidate(),
            new RecordingRuleSet([rule]),
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.Started,
            second.Kind);
        Assert.Equal(2, sink.StartCallCount);
        Assert.Equal(1, sink.StopCallCount);
        Assert.True(second.State.AutomationState.IsRecording);
        Assert.NotNull(second.State.Session);
        Assert.NotEqual(
            first.State.Session!.SessionId,
            second.State.Session!.SessionId);
    }

    [Fact]
    public async Task Evaluate_CompletedSessionNowIgnored_CleansUpWithoutRestart()
    {
        var recordRule = CreateRecordRule();
        var ignoreRule = new RecordingRule(
            Guid.NewGuid(),
            "Ignore",
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll);
        var sink = new MonitorSink(
            RecordingSinkSessionLifecycleKind.Completed);
        using var coordinator = new RecordingAutomationChannelCoordinator(
            TestChannelId,
            sink);

        _ = await coordinator.EvaluateAsync(
            CreateCandidate(),
            new RecordingRuleSet([recordRule]),
            CancellationToken.None);

        sink.MarkCurrentSessionTerminal(
            RecordingSinkSessionLifecycleKind.Completed);

        var result = await coordinator.EvaluateAsync(
            CreateCandidate(),
            new RecordingRuleSet([ignoreRule]),
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.NoChange,
            result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
        Assert.Equal(1, sink.StartCallCount);
        Assert.Equal(1, sink.StopCallCount);
    }

    [Fact]
    public async Task Evaluate_TerminalCleanupRejected_PreservesSessionAndDoesNotRestart()
    {
        var rule = CreateRecordRule();
        var sink = new MonitorSink(
            RecordingSinkSessionLifecycleKind.Completed)
        {
            RejectStop = true,
        };
        using var coordinator = new RecordingAutomationChannelCoordinator(
            TestChannelId,
            sink);

        var first = await coordinator.EvaluateAsync(
            CreateCandidate(),
            new RecordingRuleSet([rule]),
            CancellationToken.None);

        sink.MarkCurrentSessionTerminal(
            RecordingSinkSessionLifecycleKind.Completed);

        var second = await coordinator.EvaluateAsync(
            CreateCandidate(),
            new RecordingRuleSet([rule]),
            CancellationToken.None);

        Assert.Equal(
            RecordingAutomationExecutionResultKind.NoChange,
            second.Kind);
        Assert.Same(
            first.State.Session,
            second.State.Session);
        Assert.Equal(1, sink.StartCallCount);
        Assert.Equal(1, sink.StopCallCount);
    }

    private static RecordingRule CreateRecordRule()
    {
        return new RecordingRule(
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"),
            "Record",
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll);
    }

    private static RecordingCandidate CreateCandidate()
    {
        return new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(
                name: "Channel",
                genre: "Games",
                bitrateKbps: 2000));
    }

    private sealed class MonitorSink :
        IRecordingSink,
        IRecordingSinkSessionMonitor
    {
        private readonly Dictionary<
            RecordingSessionId,
            RecordingSinkSessionLifecycleKind> statuses = [];
        private RecordingSinkSessionLifecycleKind nextTerminalKind;
        private RecordingSinkSession? currentSession;

        public MonitorSink(
            RecordingSinkSessionLifecycleKind nextTerminalKind)
        {
            this.nextTerminalKind = nextTerminalKind;
        }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public bool RejectStop { get; init; }

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCallCount++;

            currentSession = new RecordingSinkSession(
                new RecordingSessionId(Guid.NewGuid()),
                request.Candidate.ChannelId,
                request.SelectedRuleId);

            statuses[currentSession.SessionId] =
                RecordingSinkSessionLifecycleKind.Active;

            return ValueTask.FromResult(
                RecordingSinkStartResult.Started(
                    currentSession));
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCallCount++;

            if (RejectStop)
            {
                return ValueTask.FromResult(
                    RecordingSinkStopResult.Rejected);
            }

            _ = statuses.Remove(
                request.Session.SessionId);

            return ValueTask.FromResult(
                nextTerminalKind ==
                    RecordingSinkSessionLifecycleKind.Faulted
                    ? RecordingSinkStopResult.Faulted
                    : RecordingSinkStopResult.Stopped);
        }

        public bool TryGetSessionStatus(
            RecordingSinkSession session,
            out RecordingSinkSessionLifecycleStatus? status)
        {
            if (!statuses.TryGetValue(
                    session.SessionId,
                    out var kind))
            {
                status = null;
                return false;
            }

            status = new RecordingSinkSessionLifecycleStatus(
                session,
                kind);
            return true;
        }

        public void MarkCurrentSessionTerminal(
            RecordingSinkSessionLifecycleKind kind)
        {
            if (currentSession is null)
            {
                throw new InvalidOperationException(
                    "No recording session has been started.");
            }

            nextTerminalKind = kind;
            statuses[currentSession.SessionId] = kind;
        }
    }
}

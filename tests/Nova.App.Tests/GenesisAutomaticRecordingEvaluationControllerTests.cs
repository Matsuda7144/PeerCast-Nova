using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    private const string ChannelIdText = "531DC8DFC7FB42928AC2C0A626517A87";
    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

    [Fact]
    public async Task EmptyRuleSetDoesNotAcquireRecordingSource()
    {
        await using var fixture = new Fixture(new RecordingRuleSet([]));

        var result = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.True(result.RuleSetAvailable);
        Assert.Equal(1, result.CandidateCount);
        Assert.Equal(1, result.EvaluatedCount);
        Assert.Equal(0, result.StartedCount);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);
        Assert.Equal(0, fixture.SourceProvider.OpenCount);
        Assert.False(fixture.Runtime.TryGetState(ChannelId(), out _));
    }

    [Fact]
    public async Task NonmatchingRuleDoesNotAcquireRecordingSource()
    {
        var nonmatchingRule = new RecordingRule(
            Guid.NewGuid(),
            "other channel",
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(channelId: new ChannelId(Guid.NewGuid())));
        await using var fixture = new Fixture(new([nonmatchingRule]));

        var result = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.Equal(1, result.EvaluatedCount);
        Assert.Equal(0, result.StartedCount);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);
        Assert.Equal(0, fixture.SourceProvider.OpenCount);
        Assert.False(fixture.Runtime.TryGetState(ChannelId(), out _));
    }

    [Fact]
    public async Task MatchingRuleStartsOnceAndPreservesChannelAndRuleIdentity()
    {
        var rule = RecordRule();
        await using var fixture = new Fixture(new([rule]));

        var started = await fixture.Controller.ObserveAsync([Channel()]);
        var state = fixture.Runtime.GetState(ChannelId());
        var session = Assert.IsType<RecordingSinkSession>(state.Session);

        Assert.Equal(1, started.StartedCount);
        Assert.Equal(0, started.FailedCount);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(ChannelId(), fixture.OwnerProvider.Sources[0].ChannelId);
        Assert.Equal(1, fixture.OwnerProvider.LastLease!.DisposeCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, session.Origin);
        Assert.Equal(ChannelId(), session.ChannelId);
        Assert.Equal(rule.Id, session.AutomaticRuleId);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule,
            fixture.SourceProvider.LastRequest!.Origin);
        Assert.Equal(rule.Id, fixture.SourceProvider.LastRequest.RuleId);

        var repeated = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.Equal(1, repeated.EvaluatedCount);
        Assert.Equal(0, repeated.StartedCount);
        Assert.Equal(0, repeated.FailedCount);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
        Assert.Equal(session.SessionId,
            fixture.Runtime.GetState(ChannelId()).Session!.SessionId);
    }

    [Fact]
    public async Task ConcurrentCatalogSnapshotsDoNotDuplicateStart()
    {
        await using var fixture = new Fixture(new([RecordRule()]));

        var results = await Task.WhenAll(
            fixture.Controller.ObserveAsync([Channel()]).AsTask(),
            fixture.Controller.ObserveAsync([Channel()]).AsTask());

        Assert.Equal(1, results.Sum(static result => result.StartedCount));
        Assert.Equal(2, results.Sum(static result => result.EvaluatedCount));
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
    }

    [Fact]
    public async Task ManualSessionIsNotReplacedOrStoppedByAutomaticEvaluation()
    {
        await using var fixture = new Fixture(new([RecordRule()]));
        await using var manual = new GenesisManualRecordingSessionController(
            fixture.OwnerProvider,
            fixture.Runtime);

        var manualStart = await manual.StartAsync(Channel());
        Assert.Equal(GenesisManualRecordingCommandKind.Started, manualStart.Kind);
        var manualSession = fixture.Runtime.GetState(ChannelId()).Session!;

        var result = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.Equal(1, result.EvaluatedCount);
        Assert.Equal(0, result.StartedCount);
        Assert.Equal(0, result.StoppedCount);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
        Assert.Equal(RecordingSessionOrigin.Manual,
            fixture.Runtime.GetState(ChannelId()).Session!.Origin);
        Assert.Equal(manualSession.SessionId,
            fixture.Runtime.GetState(ChannelId()).Session!.SessionId);
    }

    [Fact]
    public async Task IgnoreBeforeDecodableVideoCountsAsTerminalStopWithoutFailure()
    {
        await using var fixture = new Fixture(new([RecordRule()]));
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        fixture.RuleSetProvider.RuleSet = new([
            new RecordingRule(
                Guid.NewGuid(),
                "ignore selected station",
                RecordingRuleAction.Ignore,
                RecordingRuleCriteria.MatchAll),
        ]);

        var stopped = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.Equal(1, stopped.StoppedCount);
        Assert.Equal(0, stopped.StartedCount);
        Assert.Equal(0, stopped.FailedCount);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
        Assert.Null(fixture.Runtime.GetState(ChannelId()).Session);
    }

    [Fact]
    public async Task MissingChannelDoesNotSynthesizeAutomaticStop()
    {
        await using var fixture = new Fixture(new([RecordRule()]));
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        var sessionId = fixture.Runtime.GetState(ChannelId()).Session!.SessionId;

        var absent = await fixture.Controller.ObserveAsync([]);

        Assert.Equal(0, absent.CandidateCount);
        Assert.Equal(0, absent.EvaluatedCount);
        Assert.Equal(sessionId, fixture.Runtime.GetState(ChannelId()).Session!.SessionId);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
    }

    [Fact]
    public async Task InFlightEvaluationUsesOldRuleSnapshotAndNextPassLoadsSavedRules()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-recording-rule-snapshot-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var initial = await repository.GetSnapshotAsync();
        var originalRule = RecordRule();
        Assert.Equal(
            GenesisRecordingRuleRepositoryWriteStatus.Applied,
            (await repository.SaveAsync(initial, new([originalRule]))).Status);

        var snapshotProvider = new PausedFirstSnapshotProvider(repository);
        await using var fixture = new Fixture(
            new RecordingRuleSet([]),
            snapshotProvider);
        var inFlight = fixture.Controller.ObserveAsync([Channel()]).AsTask();
        await snapshotProvider.FirstSnapshotRead.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var savedSnapshot = await repository.GetSnapshotAsync();
        var ignoreRule = new RecordingRule(
            Guid.NewGuid(),
            "stop on next evaluation",
            RecordingRuleAction.Ignore,
            RecordingRuleCriteria.MatchAll);
        var saved = await repository.SaveAsync(savedSnapshot, new([ignoreRule]));
        snapshotProvider.ReleaseFirstSnapshot.TrySetResult();

        var first = await inFlight.WaitAsync(TimeSpan.FromSeconds(5));
        var next = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Applied, saved.Status);
        Assert.Equal(1, first.StartedCount);
        Assert.Equal(0, first.StoppedCount);
        Assert.Equal(1, next.StoppedCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
    }

    [Fact]
    public async Task InvalidCatalogSourceFailsClosedWithoutOwnerAcquisition()
    {
        await using var fixture = new Fixture(new([RecordRule()]));
        var invalid = Channel() with { Source = "https://tracker.invalid/channel" };

        var result = await fixture.Controller.ObserveAsync([invalid]);

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(0, result.EvaluatedCount);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);
        Assert.Equal(0, fixture.SourceProvider.OpenCount);
    }

    [Fact]
    public async Task RuleFileProviderReadsExistingCoreRuleFormat()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-recording-rule-provider-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "recording-rules.json");
        var rule = RecordRule();
        try
        {
            await RecordingRuleFileStore.SaveAsync(path, new([rule]));
            var provider = new GenesisRecordingRuleFileProvider(path);

            var loaded = await provider.GetSnapshotAsync();

            Assert.Equal(rule.Id, Assert.Single(loaded.Rules).Id);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MissingRuleFileUsesAnEmptyRuleSet()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-missing-recording-rules",
            Guid.NewGuid().ToString("N"),
            "recording-rules.json");
        var provider = new GenesisRecordingRuleFileProvider(missingPath);

        var loaded = await provider.GetSnapshotAsync();

        Assert.Empty(loaded.Rules);
    }

    [Fact]
    public async Task SinkStartFailureReleasesTemporaryOwnerLease()
    {
        await using var fixture = new Fixture(new([RecordRule()]));
        fixture.SourceProvider.ThrowOnOpen = true;

        var result = await fixture.Controller.ObserveAsync([Channel()]);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.StartedCount);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.OwnerProvider.LastLease!.DisposeCount);
        Assert.Same(RecordingAutomationExecutionState.Idle,
            fixture.Runtime.GetState(ChannelId()));
    }

    [Fact]
    public async Task ShutdownCancelsAndDrainsAnInFlightStartPreparation()
    {
        await using var fixture = new Fixture(new([RecordRule()]));
        fixture.OwnerProvider.BlockUntilCanceled = true;
        var evaluating = fixture.Controller.ObserveAsync([Channel()]).AsTask();
        await fixture.OwnerProvider.AttachEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await fixture.Controller.DisposeAsync();
        var result = await evaluating.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Cancelled);
        Assert.Equal(0, fixture.SourceProvider.OpenCount);
        Assert.Same(RecordingAutomationExecutionState.Idle,
            fixture.Runtime.GetState(ChannelId()));
        Assert.Equal(0, fixture.OwnerProvider.LastLease?.DisposeCount ?? 0);
    }

    private static RecordingRule RecordRule() => new(
        Guid.NewGuid(),
        "selected station",
        RecordingRuleAction.Record,
        RecordingRuleCriteria.MatchAll);

    private static GenesisChannelItem Channel() => new(
        "selected station",
        Source,
        Description: "managed fixture channel",
        BitrateKbps: 1_280,
        Type: "FLV",
        ChannelId: ChannelIdText);

    private static ChannelId ChannelId() =>
        new(Guid.ParseExact(ChannelIdText, "N"));

    [Theory]
    [InlineData("enable-record")]
    [InlineData("enable-ignore")]
    [InlineData("disable-no-match")]
    [InlineData("disable-ignore")]
    [InlineData("disable-record")]
    [InlineData("delete-no-match")]
    [InlineData("delete-record")]
    [InlineData("delete-ignore")]
    public async Task QuickRuleMutationAffectsNextProductionEvaluationOnly(string scenario)
    {
        var enabling = scenario.StartsWith("enable-", StringComparison.Ordinal);
        var targetIgnore = scenario is "enable-ignore" or "delete-ignore";
        var action = enabling ? GenesisRecordingRuleQuickAction.Enable
            : scenario.StartsWith("disable-", StringComparison.Ordinal)
                ? GenesisRecordingRuleQuickAction.Disable : GenesisRecordingRuleQuickAction.Delete;
        var target = RuleMutationFixture.Rule("target", !enabling,
            new RecordingRuleCriteria(channelId: ChannelId()),
            targetIgnore ? RecordingRuleAction.Ignore : RecordingRuleAction.Record, 200);
        var fallbackRecord = scenario is "enable-ignore" or "disable-record" or "delete-record" or "delete-ignore";
        var fallbackIgnore = scenario == "disable-ignore";
        var fallback = RuleMutationFixture.Rule("fallback", true,
            new RecordingRuleCriteria(channelId: ChannelId()),
            fallbackIgnore ? RecordingRuleAction.Ignore : RecordingRuleAction.Record, 100);
        await using var h = await RuleMutationFixture.CreateAsync(
            fallbackRecord || fallbackIgnore ? [target, fallback] : [target]);
        await using var fixture = new Fixture(new([]), new GenesisRecordingRuleFileProvider(h.Path));
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        var before = fixture.Runtime.GetState(ChannelId());
        var session = before.Session;
        var starts = fixture.SourceProvider.OpenCount;
        var attachments = fixture.OwnerProvider.AttachCount;
        Assert.True((await h.Act(target.Id, action)).Applied);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(before.AutomationState.ActiveRuleId, fixture.Runtime.GetState(ChannelId()).AutomationState.ActiveRuleId);
        Assert.Equal(starts, fixture.SourceProvider.OpenCount);
        Assert.Equal(attachments, fixture.OwnerProvider.AttachCount);

        var evaluated = await fixture.Controller.ObserveAsync([Channel()]);
        var next = fixture.Runtime.GetState(ChannelId());
        if (scenario is "enable-record" or "delete-ignore")
        {
            Assert.Equal(1, evaluated.StartedCount);
            Assert.Equal(starts + 1, fixture.SourceProvider.OpenCount);
            Assert.Equal(scenario == "enable-record" ? target.Id : fallback.Id, next.AutomationState.ActiveRuleId);
        }
        else if (scenario is "disable-record" or "delete-record")
        {
            Assert.Equal(0, evaluated.StartedCount);
            Assert.Equal(0, evaluated.StoppedCount);
            Assert.Same(session, next.Session);
            Assert.Equal(fallback.Id, next.AutomationState.ActiveRuleId);
            Assert.Equal(target.Id, next.Session!.AutomaticRuleId);
            Assert.Equal(starts, fixture.SourceProvider.OpenCount);
            Assert.Equal(attachments, fixture.OwnerProvider.AttachCount);
        }
        else
        {
            Assert.Equal(1, evaluated.StoppedCount);
            Assert.Null(next.Session);
            Assert.Equal(starts, fixture.SourceProvider.OpenCount);
        }
        Assert.Equal(0, evaluated.FailedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ManualRecordingRemainsProtectedAfterQuickRuleMutation(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var target = RuleMutationFixture.Rule("target", action != GenesisRecordingRuleQuickAction.Enable,
            new RecordingRuleCriteria(channelId: ChannelId()), RecordingRuleAction.Ignore);
        await using var h = await RuleMutationFixture.CreateAsync([target]);
        await using var fixture = new Fixture(new([]), new GenesisRecordingRuleFileProvider(h.Path));
        await using var manual = new GenesisManualRecordingSessionController(fixture.OwnerProvider, fixture.Runtime);
        _ = await manual.StartAsync(Channel());
        var session = fixture.Runtime.GetState(ChannelId()).Session;
        Assert.True((await h.Act(target.Id, action)).Applied);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
        var next = await fixture.Controller.ObserveAsync([Channel()]);
        Assert.Equal(0, next.StartedCount);
        Assert.Equal(0, next.StoppedCount);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(1, fixture.SourceProvider.OpenCount);
    }

    [Theory]
    [InlineData("different-priority")]
    [InlineData("record-ignore")]
    [InlineData("ignore-record")]
    [InlineData("record-record")]
    public async Task ReorderChangesOnlyNextEvaluationTieBreakNotPriorityOrSaveSideEffects(string scenario)
    {
        var firstIgnore = scenario == "ignore-record";
        var secondIgnore = scenario is "different-priority" or "record-ignore";
        var a = RuleMutationFixture.Rule("a", true, new(channelId: ChannelId()),
            firstIgnore ? RecordingRuleAction.Ignore : RecordingRuleAction.Record, 200);
        var b = RuleMutationFixture.Rule("b", true, new(channelId: ChannelId()),
            secondIgnore ? RecordingRuleAction.Ignore : RecordingRuleAction.Record, scenario == "different-priority" ? 100 : 200);
        await using var h = await RuleMutationFixture.CreateAsync([a, b]);
        await using var fixture = new Fixture(new([]), new GenesisRecordingRuleFileProvider(h.Path));
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        var before = fixture.Runtime.GetState(ChannelId());
        var session = before.Session;
        var starts = fixture.SourceProvider.OpenCount; var attachments = fixture.OwnerProvider.AttachCount;
        Assert.True((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        Assert.Equal(scenario == "different-priority" ? 0 : 1, h.Confirmations);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(before.AutomationState.ActiveRuleId, fixture.Runtime.GetState(ChannelId()).AutomationState.ActiveRuleId);
        Assert.Equal(starts, fixture.SourceProvider.OpenCount); Assert.Equal(attachments, fixture.OwnerProvider.AttachCount);
        var next = await fixture.Controller.ObserveAsync([Channel()]);
        var state = fixture.Runtime.GetState(ChannelId());
        Assert.Equal(0, next.FailedCount);
        if (scenario == "record-ignore")
        {
            Assert.Equal(1, next.StoppedCount); Assert.Equal(0, next.StartedCount); Assert.Null(state.Session);
            Assert.Equal(starts, fixture.SourceProvider.OpenCount);
        }
        else if (scenario == "ignore-record")
        {
            Assert.Equal(1, next.StartedCount); Assert.Equal(0, next.StoppedCount);
            Assert.Equal(b.Id, state.AutomationState.ActiveRuleId); Assert.Equal(b.Id, state.Session!.AutomaticRuleId);
        }
        else
        {
            Assert.Equal(0, next.StartedCount); Assert.Equal(0, next.StoppedCount); Assert.Same(session, state.Session);
            Assert.Equal(scenario == "different-priority" ? a.Id : b.Id, state.AutomationState.ActiveRuleId);
            Assert.Equal(a.Id, state.Session!.AutomaticRuleId);
            Assert.Equal(starts, fixture.SourceProvider.OpenCount); Assert.Equal(attachments, fixture.OwnerProvider.AttachCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualSessionRemainsProtectedAfterReorder(bool firstIgnore)
    {
        var a = RuleMutationFixture.Rule("a", true, new(channelId: ChannelId()), firstIgnore ? RecordingRuleAction.Ignore : RecordingRuleAction.Record);
        var b = RuleMutationFixture.Rule("b", true, new(channelId: ChannelId()), firstIgnore ? RecordingRuleAction.Record : RecordingRuleAction.Ignore);
        await using var h = await RuleMutationFixture.CreateAsync([a, b]);
        await using var fixture = new Fixture(new([]), new GenesisRecordingRuleFileProvider(h.Path));
        await using var manual = new GenesisManualRecordingSessionController(fixture.OwnerProvider, fixture.Runtime);
        _ = await manual.StartAsync(Channel());
        var session = fixture.Runtime.GetState(ChannelId()).Session;
        Assert.True((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        var evaluated = await fixture.Controller.ObserveAsync([Channel()]);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
        Assert.Equal(0, evaluated.StartedCount); Assert.Equal(0, evaluated.StoppedCount);
        Assert.Equal(1, fixture.SourceProvider.OpenCount); Assert.Equal(1, fixture.OwnerProvider.AttachCount);
    }

    [Fact]
    public async Task InFlightEvaluationUsesOldOrderAndNextPassLoadsReorderedSnapshot()
    {
        var a = RuleMutationFixture.Rule("a", true, new(channelId: ChannelId()), RecordingRuleAction.Record);
        var b = RuleMutationFixture.Rule("b", true, new(channelId: ChannelId()), RecordingRuleAction.Ignore);
        await using var h = await RuleMutationFixture.CreateAsync([a, b]);
        var provider = new PausedFirstSnapshotProvider(h.Repository);
        await using var fixture = new Fixture(new([]), provider);
        var pending = fixture.Controller.ObserveAsync([Channel()]).AsTask(); await provider.FirstSnapshotRead.Task;
        Assert.True((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        Assert.Equal(0, fixture.SourceProvider.OpenCount); Assert.Equal(0, fixture.OwnerProvider.AttachCount);
        provider.ReleaseFirstSnapshot.SetResult();
        var first = await pending;
        Assert.Equal(1, first.StartedCount); Assert.Equal(0, first.StoppedCount);
        Assert.Equal(a.Id, fixture.Runtime.GetState(ChannelId()).Session!.AutomaticRuleId);
        var next = await fixture.Controller.ObserveAsync([Channel()]);
        Assert.Equal(1, next.StoppedCount); Assert.Equal(0, next.StartedCount); Assert.Equal(1, fixture.SourceProvider.OpenCount);
    }

    [Fact]
    public async Task MissingChannelAndRuleReadFailureAfterReorderDoNotStopRecording()
    {
        var a = RuleMutationFixture.Rule("a", true, new(channelId: ChannelId()));
        var b = RuleMutationFixture.Rule("b", true, new(channelId: ChannelId()));
        await using var h = await RuleMutationFixture.CreateAsync([a, b]);
        await using var fixture = new Fixture(new([]), new GenesisRecordingRuleFileProvider(h.Path));
        _ = await fixture.Controller.ObserveAsync([Channel()]);
        var session = fixture.Runtime.GetState(ChannelId()).Session;
        Assert.True((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        var absent = await fixture.Controller.ObserveAsync([]);
        Assert.Equal(0, absent.EvaluatedCount); Assert.Equal(0, absent.StoppedCount);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
        await File.WriteAllTextAsync(h.Path, "invalid");
        var unavailable = await fixture.Controller.ObserveAsync([Channel()]);
        Assert.False(unavailable.RuleSetAvailable); Assert.Equal(0, unavailable.EvaluatedCount); Assert.Equal(0, unavailable.StoppedCount);
        Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session); Assert.Equal(1, fixture.SourceProvider.OpenCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly IRecordingSink? _testSink;
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-automatic-recording-evaluation-tests",
            Guid.NewGuid().ToString("N"));

        public Fixture(
            RecordingRuleSet ruleSet,
            IGenesisRecordingRuleSetProvider? ruleSetProvider = null,
            Func<RecordingRuleSet, RecordingCandidate, RecordingRuleDecision>? evaluate = null,
            IRecordingSink? sink = null,
            string? outputDirectory = null,
            IRecordingSessionTerminalObserver? terminalObserver = null)
        {
            _directory = outputDirectory ?? _directory;
            _testSink = sink;
            SourceProvider = new BlockingFlvSourceProvider();
            Runtime = new RecordingRuntime(
                SourceProvider,
                new DefaultRecordingFilePathProvider(_directory),
                terminalObserver: terminalObserver);
            if (sink is not null)
            {
                // Replace only the isolated fixture's unused sink composition.
                // The real Manager/Coordinator admission foundation is exercised.
                var field = typeof(RecordingRuntime).GetField("sessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                ((RecordingAutomationManager)field.GetValue(Runtime)!).Dispose();
                field.SetValue(Runtime, new RecordingAutomationManager(sink));
            }
            OwnerProvider = new FakeOwnerProvider(Runtime);
            RuleSetProvider = new FixedRuleSetProvider(ruleSet);
            Controller = new GenesisAutomaticRecordingEvaluationController(
                Runtime,
                OwnerProvider,
                ruleSetProvider ?? RuleSetProvider,
                evaluate);
        }

        public RecordingRuntime Runtime { get; }

        public BlockingFlvSourceProvider SourceProvider { get; }

        public FakeOwnerProvider OwnerProvider { get; }

        public FixedRuleSetProvider RuleSetProvider { get; }

        public GenesisAutomaticRecordingEvaluationController Controller { get; }

        public async ValueTask DisposeAsync()
        {
            if (_testSink is UserStopSink controlled) controlled.Finish(RecordingSinkStopResultKind.Stopped);
            await Controller.DisposeAsync();
            await Runtime.DisposeAsync();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class FixedRuleSetProvider(RecordingRuleSet ruleSet) :
        IGenesisRecordingRuleSetProvider
    {
        public RecordingRuleSet RuleSet { get; set; } = ruleSet;

        public ValueTask<RecordingRuleSet> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(RuleSet);
        }
    }

    private sealed class PausedFirstSnapshotProvider(
        GenesisRecordingRuleRepository repository) : IGenesisRecordingRuleSetProvider
    {
        private int _readCount;

        public TaskCompletionSource FirstSnapshotRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstSnapshot { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RecordingRuleSet> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            var snapshot = await repository.GetSnapshotAsync(cancellationToken);
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                FirstSnapshotRead.TrySetResult();
                await ReleaseFirstSnapshot.Task.WaitAsync(cancellationToken);
            }

            return Assert.IsType<RecordingRuleSet>(snapshot.Rules);
        }
    }

    private sealed class FakeOwnerProvider(RecordingRuntime recording) :
        IGenesisSharedPcpRecordingOwnerProvider
    {
        private int _attachCount;

        public int AttachCount => Volatile.Read(ref _attachCount);

        public bool BlockUntilCanceled { get; set; }

        public List<DailyPcpPlaybackSource> Sources { get; } = [];

        public SpyLease? LastLease { get; private set; }

        public TaskCompletionSource AttachEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GenesisSharedPcpRecordingOwnerAttachment> AttachRecordingOwnerAsync(
            DailyPcpPlaybackSource source,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attachCount);
            Sources.Add(source);
            AttachEntered.TrySetResult();
            if (BlockUntilCanceled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            LastLease = new();
            return new(
                recording,
                Task.FromResult(PcpRelayCoordinatorReadinessOutcome.Ready),
                Task.FromResult<PcpRelayCoordinatorResult>(null!),
                LastLease);
        }
    }

    private sealed class SpyLease : IAsyncDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingFlvSourceProvider : IRecordingStreamSourceProvider
    {
        private int _openCount;

        public int OpenCount => Volatile.Read(ref _openCount);

        public RecordingSinkStartRequest? LastRequest { get; private set; }

        public bool ThrowOnOpen { get; set; }
        public Func<Stream>? StreamFactory { get; set; }

        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _openCount);
            LastRequest = request;
            if (ThrowOnOpen)
            {
                return ValueTask.FromException<Stream>(
                    new IOException("Synthetic recording source acquisition failure."));
            }

            return ValueTask.FromResult(StreamFactory?.Invoke() ?? new BlockingFlvStream());
        }
    }

    private class BlockingFlvStream : Stream
    {
        private static readonly byte[] Header =
        [
            (byte)'F',
            (byte)'L',
            (byte)'V',
            1,
            0x05,
            0,
            0,
            0,
            9,
            0,
            0,
            0,
            0,
        ];

        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = Math.Min(count, Header.Length - _offset);
            Header.AsSpan(_offset, copied).CopyTo(buffer.AsSpan(offset, copied));
            _offset += copied;
            return copied;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_offset < Header.Length)
            {
                var copied = Math.Min(buffer.Length, Header.Length - _offset);
                Header.AsMemory(_offset, copied).CopyTo(buffer);
                _offset += copied;
                return ValueTask.FromResult(copied);
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private static async ValueTask<int> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }
    }
}

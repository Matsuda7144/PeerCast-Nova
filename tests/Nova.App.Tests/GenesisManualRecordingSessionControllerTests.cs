using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisManualRecordingSessionControllerTests
{
    private const string ChannelIdText = "531DC8DFC7FB42928AC2C0A626517A87";
    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

    [Fact]
    public async Task StartUsesSharedOwnerAndReleasesTemporaryLeaseAfterCoreStart()
    {
        await using var fixture = new Fixture();

        var initial = fixture.Controller.GetSnapshot(Channel());
        Assert.True(initial.IsCandidateValid);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle, initial.State);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);

        var started = await fixture.Controller.StartAsync(Channel());

        Assert.Equal(GenesisManualRecordingCommandKind.Started, started.Kind);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.OwnerProvider.LastLease!.DisposeCount);
        Assert.Equal(GenesisRecordingCoreStateKind.Manual, started.Snapshot.State);
        var channelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        var session = fixture.Runtime.GetState(channelId).Session!;
        Assert.Equal(RecordingSessionOrigin.Manual, session.Origin);

        var active = Assert.Single(
            (await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);
        Assert.Equal(channelId, active.ChannelId);
        Assert.Equal(session.SessionId, active.SessionId);
        Assert.Equal("selected station", active.ChannelName);
    }

    [Theory]
    [InlineData("pcp://tracker.example:7144/channel/not-an-id", ChannelIdText)]
    [InlineData(Source, null)]
    [InlineData(Source, "11111111111141118111111111111111")]
    public async Task InvalidOrMismatchedSourceFailsClosed(
        string source,
        string? channelId)
    {
        await using var fixture = new Fixture();
        var channel = new GenesisChannelItem("station", source, ChannelId: channelId);

        var result = await fixture.Controller.StartAsync(channel);

        Assert.Equal(GenesisManualRecordingCommandKind.Rejected, result.Kind);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);
        Assert.Empty((await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);
    }

    [Fact]
    public async Task DuplicateStartDoesNotAcquireAnotherOwner()
    {
        await using var fixture = new Fixture();
        var channel = Channel();

        var first = await fixture.Controller.StartAsync(channel);
        var duplicate = await fixture.Controller.StartAsync(channel);

        Assert.Equal(GenesisManualRecordingCommandKind.Started, first.Kind);
        Assert.Equal(GenesisManualRecordingCommandKind.AlreadyRunning, duplicate.Kind);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.OwnerProvider.LastLease!.DisposeCount);
    }

    [Fact]
    public async Task StopUsesChannelIdOnlyAndDoesNotAcquireAnOwner()
    {
        await using var fixture = new Fixture();
        var channel = Channel();
        _ = await fixture.Controller.StartAsync(channel);
        var attachesBeforeStop = fixture.OwnerProvider.AttachCount;

        var result = await fixture.Controller.StopAsync(channel with { Source = "invalid" });

        Assert.Contains(
            result.Kind,
            new[]
            {
                GenesisManualRecordingCommandKind.Stopped,
                GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                GenesisManualRecordingCommandKind.AlreadyStopped,
            });
        Assert.Equal(attachesBeforeStop, fixture.OwnerProvider.AttachCount);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle, result.Snapshot.State);
    }

    [Fact]
    public async Task StopWithoutSessionIsNoChangeAndRepeatedStopIsSafe()
    {
        await using var fixture = new Fixture();

        var first = await fixture.Controller.StopAsync(Channel());
        var second = await fixture.Controller.StopAsync(Channel());

        Assert.Equal(GenesisManualRecordingCommandKind.NoActiveSession, first.Kind);
        Assert.Equal(GenesisManualRecordingCommandKind.NoActiveSession, second.Kind);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);
    }

    [Fact]
    public async Task StopByChannelAndExpectedSessionDoesNotAcquireAnotherOwner()
    {
        await using var fixture = new Fixture();
        var channelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        _ = await fixture.Controller.StartAsync(Channel());
        var sessionId = fixture.Runtime.GetState(channelId).Session!.SessionId;
        var attachesBeforeStop = fixture.OwnerProvider.AttachCount;

        var stopped = await fixture.Controller.StopAsync(channelId, sessionId);

        Assert.Contains(
            stopped.Kind,
            new[]
            {
                GenesisManualRecordingCommandKind.Stopped,
                GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                GenesisManualRecordingCommandKind.AlreadyStopped,
            });
        Assert.Equal(attachesBeforeStop, fixture.OwnerProvider.AttachCount);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle, stopped.Snapshot.State);
        var repeated = await fixture.Controller.StopAsync(channelId, sessionId);
        Assert.Equal(GenesisManualRecordingCommandKind.NoActiveSession, repeated.Kind);
        Assert.Equal(attachesBeforeStop, fixture.OwnerProvider.AttachCount);
        Assert.Empty((await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);
    }

    [Fact]
    public async Task StopBeforeDecodableVideoStartMapsToNonFaultManualOutcome()
    {
        var channelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        var sourceProvider = new GatedRecordingSourceProvider(
            channelId,
            AvcConfigurationOnlyFlv());
        await using var fixture = new Fixture(sourceProvider);
        var channel = Channel();
        var started = await fixture.Controller.StartAsync(channel);
        Assert.Equal(GenesisManualRecordingCommandKind.Started, started.Kind);
        var sessionId = fixture.Runtime.GetState(channelId).Session!.SessionId;
        await sourceProvider.BlockedReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var stopping = fixture.Controller.StopAsync(channelId, sessionId).AsTask();
            await sourceProvider.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sourceProvider.ReleaseBlockedRead.TrySetResult();
            var stopped = await stopping.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                stopped.Kind);
            Assert.Equal(GenesisRecordingCoreStateKind.Idle, stopped.Snapshot.State);
            Assert.Empty((await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);
        }
        finally
        {
            sourceProvider.ReleaseBlockedRead.TrySetResult();
        }
    }

    [Fact]
    public async Task SnapshotCleanupRemovesOnlyPreexistingStaleManualIdentity()
    {
        await using var fixture = new Fixture();
        var channelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        _ = await fixture.Controller.StartAsync(Channel());
        var sessionId = fixture.Runtime.GetState(channelId).Session!.SessionId;
        Assert.Single((await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);

        var coreStop = await fixture.Runtime.StopAsync(channelId);
        Assert.Contains(
            coreStop.Kind,
            new[]
            {
                RecordingAutomationStopResultKind.Stopped,
                RecordingAutomationStopResultKind.NoDecodableVideoStart,
                RecordingAutomationStopResultKind.AlreadyStopped,
            });

        var active = await fixture.Controller.GetActiveManualRecordingsAsync();

        Assert.True(active.IsAvailable);
        Assert.Empty(active.Sessions);
        Assert.False(fixture.Controller.IsKnownManualSession(channelId, sessionId));
    }

    [Fact]
    public async Task ManualStopDoesNotStopAutomaticRecording()
    {
        await using var fixture = new Fixture();
        var channel = Channel();
        Assert.True(GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted));
        var automaticRule = new RecordingRule(
            Guid.NewGuid(),
            "Automatic test rule",
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll);
        var started = await fixture.Runtime.EvaluateAutomaticAsync(
            adapted!.Candidate,
            new RecordingRuleSet([automaticRule]));

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, started.Kind);
        Assert.Equal(
            GenesisRecordingCoreStateKind.Automatic,
            fixture.Controller.GetSnapshot(channel).State);
        var stop = await fixture.Controller.StopAsync(channel);

        Assert.Equal(GenesisManualRecordingCommandKind.Rejected, stop.Kind);
        Assert.Equal(0, fixture.OwnerProvider.AttachCount);
        Assert.Empty((await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);
        Assert.Equal(
            RecordingSessionOrigin.AutomaticRule,
            fixture.Runtime.GetState(adapted.Candidate.ChannelId).Session!.Origin);
    }

    [Fact]
    public async Task ActiveRecordingSnapshotIncludesManualAndAutomaticCoreIdentities()
    {
        await using var fixture = new Fixture();
        var manual = await fixture.Controller.StartAsync(Channel());
        Assert.Equal(GenesisManualRecordingCommandKind.Started, manual.Kind);

        const string automaticChannelId = "102132435465768798A9BABBDCDDEF00";
        var automaticChannel = new GenesisChannelItem(
            "automatic station",
            $"pcp://tracker.example:7144/channel/{automaticChannelId}",
            ChannelId: automaticChannelId);
        Assert.True(GenesisRecordingCandidateAdapter.TryCreate(
            automaticChannel,
            out var adapted));
        var rule = new RecordingRule(
            Guid.NewGuid(),
            "automatic snapshot test",
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll);
        var automaticStart = await fixture.Runtime.EvaluateAutomaticAsync(
            adapted!.Candidate,
            new RecordingRuleSet([rule]));
        Assert.Equal(RecordingAutomationExecutionResultKind.Started, automaticStart.Kind);

        var snapshot = await fixture.Controller.GetActiveRecordingSessionsAsync();

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(2, snapshot.Sessions.Count);
        var manualSession = Assert.Single(
            snapshot.Sessions,
            static session => session.Origin == RecordingSessionOrigin.Manual);
        var automaticSession = Assert.Single(
            snapshot.Sessions,
            static session => session.Origin == RecordingSessionOrigin.AutomaticRule);
        Assert.Equal("selected station", manualSession.RetainedChannelName);
        Assert.Equal(rule.Id, automaticSession.AutomaticRuleId);
        Assert.Null(automaticSession.RetainedChannelName);
        Assert.Equal(adapted.Candidate.ChannelId, automaticSession.ChannelId);
    }

    [Fact]
    public void SnapshotCleanupDoesNotRemoveReplacementGeneration()
    {
        var channelId = new ChannelId(Guid.NewGuid());
        var capturedSessionId = new RecordingSessionId(Guid.NewGuid());
        var replacementSessionId = new RecordingSessionId(Guid.NewGuid());
        var captured = new GenesisManualRecordingSessionIdentity(
            channelId,
            capturedSessionId,
            "captured name");
        var replacement = new GenesisManualRecordingSessionIdentity(
            channelId,
            replacementSessionId,
            "replacement name");
        var registry = new Dictionary<ChannelId,
            GenesisManualRecordingSessionIdentity>
        {
            [channelId] = replacement,
        };

        GenesisManualRecordingSessionController.CleanupStaleManualSessionIdentities(
            registry,
            [captured],
            new HashSet<(ChannelId ChannelId, RecordingSessionId SessionId)>());

        Assert.Same(replacement, Assert.Single(registry).Value);
    }

    [Fact]
    public void CurrentRegistryProjectionRequiresExactSessionGeneration()
    {
        var channelId = new ChannelId(Guid.NewGuid());
        var oldSessionId = new RecordingSessionId(Guid.NewGuid());
        var currentSessionId = new RecordingSessionId(Guid.NewGuid());
        var capturedRegistry = new Dictionary<ChannelId,
            GenesisManualRecordingSessionIdentity>
        {
            [channelId] = new(channelId, oldSessionId, "captured name"),
        };
        var currentRegistry = new Dictionary<ChannelId,
            GenesisManualRecordingSessionIdentity>
        {
            [channelId] = new(channelId, currentSessionId, "current name"),
        };

        var projected = GenesisManualRecordingSessionController.ProjectActiveRecordingSession(
            RecordingSinkSession.Manual(currentSessionId, channelId),
            currentRegistry);

        Assert.NotSame(capturedRegistry[channelId], currentRegistry[channelId]);
        Assert.Equal(currentSessionId, projected.SessionId);
        Assert.Equal("current name", projected.RetainedChannelName);
    }

    [Fact]
    public async Task FailedCoreSnapshotDoesNotDeleteManualRegistry()
    {
        await using var fixture = new Fixture();
        var channel = Channel();
        var started = await fixture.Controller.StartAsync(channel);
        Assert.Equal(GenesisManualRecordingCommandKind.Started, started.Kind);
        var channelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        var sessionId = fixture.Runtime.GetState(channelId).Session!.SessionId;

        await fixture.Runtime.DisposeAsync();

        var snapshot = await fixture.Controller.GetActiveRecordingSessionsAsync();

        Assert.False(snapshot.IsAvailable);
        Assert.True(fixture.Controller.IsKnownManualSession(channelId, sessionId));
    }

    [Fact]
    public async Task ConcurrentManualStartSurvivesOlderGlobalSnapshotCleanupAndRemainsStoppable()
    {
        var firstChannelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        var sourceProvider = new GatedRecordingSourceProvider(firstChannelId);
        await using var fixture = new Fixture(sourceProvider);
        var race = await BeginBlockedSnapshotRaceAsync(
            fixture,
            sourceProvider,
            "102132435465768798A9BABBDCDDEF00");

        try
        {
            Assert.Equal(
                GenesisRecordingCoreStateKind.Manual,
                fixture.Controller.GetSnapshot(race.NewChannelId).State);
            Assert.True(fixture.Controller.IsKnownManualSession(
                race.NewChannelId,
                race.NewSessionId));

            sourceProvider.ReleaseBlockedRead.TrySetResult();
            var firstStop = await race.FirstStopTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(
                firstStop.Kind,
                new[]
                {
                    GenesisManualRecordingCommandKind.Stopped,
                    GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                    GenesisManualRecordingCommandKind.AlreadyStopped,
                });
            var olderSnapshot = await race.SnapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(olderSnapshot.IsAvailable);
            Assert.DoesNotContain(
                olderSnapshot.Sessions,
                session => session.ChannelId == race.NewChannelId);

            Assert.True(fixture.Controller.IsKnownManualSession(
                race.NewChannelId,
                race.NewSessionId));

            var bridge = new ManualRecordingPlaybackBridge(fixture.Controller);
            var viewModel = new GenesisMainViewModel(bridge);
            await viewModel.RefreshActiveRecordingsAsync();
            var row = Assert.Single(viewModel.ActiveRecordings);
            Assert.Equal(race.NewSessionId, row.SessionId);
            Assert.Equal(race.NewChannel.Name, row.ChannelName);
            Assert.True(viewModel.GetActiveRecordingPresentation(row).CanStop);

            await viewModel.StopManualRecordingAsync(
                race.NewChannelId,
                race.NewSessionId);

            Assert.Equal(1, bridge.ManualStopCount);
            Assert.Equal(race.NewChannelId, bridge.LastManualStopChannelId);
            Assert.Equal(race.NewSessionId, bridge.LastManualStopSessionId);
            Assert.Empty(viewModel.ActiveRecordings);
            Assert.False(fixture.Controller.IsKnownManualSession(
                race.NewChannelId,
                race.NewSessionId));
        }
        finally
        {
            sourceProvider.ReleaseBlockedRead.TrySetResult();
        }
    }

    [Fact]
    public async Task SnapshotCleanupUsesCurrentRegistryForPresentationMetadata()
    {
        const string blockingChannelIdText = "00000000000000000000000000000001";
        const string replacementChannelIdText = "00000000000000000000000000000002";
        var blockingChannelId = new ChannelId(Guid.ParseExact(blockingChannelIdText, "N"));
        var replacementChannelId = new ChannelId(
            Guid.ParseExact(replacementChannelIdText, "N"));
        var sourceProvider = new GatedRecordingSourceProvider(blockingChannelId);
        await using var fixture = new Fixture(sourceProvider);
        var blockingChannel = Channel("blocking station", blockingChannelIdText);
        var blockingStart = await fixture.Controller.StartAsync(blockingChannel);
        Assert.Equal(GenesisManualRecordingCommandKind.Started, blockingStart.Kind);
        var blockingSessionId = fixture.Runtime.GetState(blockingChannelId).Session!.SessionId;
        await sourceProvider.BlockedReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var firstName = Channel("old station name", replacementChannelIdText);
        var firstGeneration = await fixture.Controller.StartAsync(firstName);
        Assert.Equal(GenesisManualRecordingCommandKind.Started, firstGeneration.Kind);
        var firstSessionId = fixture.Runtime.GetState(replacementChannelId).Session!.SessionId;

        var blockingStop = fixture.Controller.StopAsync(
            blockingChannelId,
            blockingSessionId).AsTask();
        await sourceProvider.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var olderSnapshotTask = fixture.Controller.GetActiveRecordingSessionsAsync().AsTask();
        Assert.False(olderSnapshotTask.IsCompleted);

        try
        {
            var coreStop = await fixture.Runtime.StopAsync(replacementChannelId);
            Assert.Contains(
                coreStop.Kind,
                new[]
                {
                    RecordingAutomationStopResultKind.Stopped,
                    RecordingAutomationStopResultKind.NoDecodableVideoStart,
                    RecordingAutomationStopResultKind.AlreadyStopped,
                });

            var currentName = Channel("current station name", replacementChannelIdText);
            var replacement = await fixture.Controller.StartAsync(currentName);
            Assert.Equal(GenesisManualRecordingCommandKind.Started, replacement.Kind);
            var replacementSessionId = fixture.Runtime
                .GetState(replacementChannelId)
                .Session!
                .SessionId;
            Assert.NotEqual(firstSessionId, replacementSessionId);
            Assert.True(fixture.Controller.IsKnownManualSession(
                replacementChannelId,
                replacementSessionId));

            sourceProvider.ReleaseBlockedRead.TrySetResult();
            _ = await blockingStop.WaitAsync(TimeSpan.FromSeconds(5));
            var olderSnapshot = await olderSnapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(olderSnapshot.IsAvailable);
            var projected = Assert.Single(
                olderSnapshot.Sessions,
                session => session.ChannelId == replacementChannelId);
            Assert.Equal(replacementSessionId, projected.SessionId);
            Assert.Equal("current station name", projected.RetainedChannelName);
            Assert.True(fixture.Controller.IsKnownManualSession(
                replacementChannelId,
                replacementSessionId));

            var stopped = await fixture.Controller.StopAsync(
                replacementChannelId,
                replacementSessionId);
            Assert.Contains(
                stopped.Kind,
                new[]
                {
                    GenesisManualRecordingCommandKind.Stopped,
                    GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                    GenesisManualRecordingCommandKind.AlreadyStopped,
                });
        }
        finally
        {
            sourceProvider.ReleaseBlockedRead.TrySetResult();
        }
    }

    [Fact]
    public async Task CancellationDoesNotDeleteNewManualIdentity()
    {
        var firstChannelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
        var sourceProvider = new GatedRecordingSourceProvider(firstChannelId);
        await using var fixture = new Fixture(sourceProvider);
        using var cancellation = new CancellationTokenSource();
        var race = await BeginBlockedSnapshotRaceAsync(
            fixture,
            sourceProvider,
            "2031425364758697A8B9CADBECFD0E1F",
            cancellation.Token);

        try
        {
            cancellation.Cancel();
            var cancelledSnapshot = await race.SnapshotTask.WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.False(cancelledSnapshot.IsAvailable);
            Assert.True(fixture.Controller.IsKnownManualSession(
                race.NewChannelId,
                race.NewSessionId));

            sourceProvider.ReleaseBlockedRead.TrySetResult();
            _ = await race.FirstStopTask.WaitAsync(TimeSpan.FromSeconds(5));
            var currentSnapshot = await fixture.Controller.GetActiveRecordingSessionsAsync();

            Assert.True(currentSnapshot.IsAvailable);
            var active = Assert.Single(currentSnapshot.Sessions);
            Assert.Equal(race.NewChannelId, active.ChannelId);
            Assert.Equal(race.NewSessionId, active.SessionId);

            var stopped = await fixture.Controller.StopAsync(
                race.NewChannelId,
                race.NewSessionId);
            Assert.Contains(
                stopped.Kind,
                new[]
                {
                    GenesisManualRecordingCommandKind.Stopped,
                    GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                    GenesisManualRecordingCommandKind.AlreadyStopped,
                });
        }
        finally
        {
            sourceProvider.ReleaseBlockedRead.TrySetResult();
        }
    }

    [Fact]
    public async Task CanceledCoreStartReleasesTemporaryOwnerLease()
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.OwnerProvider.BeforeReturn = cancellation.Cancel;

        var result = await fixture.Controller.StartAsync(Channel(), cancellation.Token);

        Assert.Equal(GenesisManualRecordingCommandKind.Cancelled, result.Kind);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.OwnerProvider.LastLease!.DisposeCount);
        Assert.Empty((await fixture.Controller.GetActiveManualRecordingsAsync()).Sessions);
    }

    [Fact]
    public async Task DifferentRecordingRuntimeIsRejectedAndTemporaryLeaseReleased()
    {
        await using var fixture = new Fixture();
        await using var otherRuntime = CreateRuntime();
        fixture.OwnerProvider.ReturnedRuntime = otherRuntime;

        var result = await fixture.Controller.StartAsync(Channel());

        Assert.Equal(GenesisManualRecordingCommandKind.Failed, result.Kind);
        Assert.Equal(1, fixture.OwnerProvider.AttachCount);
        Assert.Equal(1, fixture.OwnerProvider.LastLease!.DisposeCount);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle,
            fixture.Controller.GetSnapshot(Channel()).State);
    }

    [Fact]
    public async Task ShutdownCancelsAndDrainsAnInFlightOwnerAttachment()
    {
        await using var fixture = new Fixture();
        fixture.OwnerProvider.BlockUntilCanceled = true;
        var starting = fixture.Controller.StartAsync(Channel()).AsTask();
        await fixture.OwnerProvider.AttachEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await fixture.Controller.DisposeAsync();
        var result = await starting.WaitAsync(TimeSpan.FromSeconds(5));
        var afterShutdown = await fixture.Controller.StartAsync(Channel());

        Assert.Equal(GenesisManualRecordingCommandKind.Cancelled, result.Kind);
        Assert.Equal(GenesisManualRecordingCommandKind.Cancelled, afterShutdown.Kind);
        Assert.Equal(0, fixture.OwnerProvider.LastLease?.DisposeCount ?? 0);
    }

    private static GenesisChannelItem Channel() => new(
        "selected station",
        Source,
        Description: "Games - details",
        BitrateKbps: 1280,
        Type: "FLV",
        ChannelId: ChannelIdText);

    private static byte[] AvcConfigurationOnlyFlv() =>
    [
        (byte)'F', (byte)'L', (byte)'V', 1, 0x05, 0, 0, 0, 9, 0, 0, 0, 0,
        9, 0, 0, 22, 0, 0, 0, 0, 0, 0, 0,
        0x17, 0, 0, 0, 0,
        1, 0x64, 0, 0x1F, 0xFF, 0xE1,
        0, 4, 0x67, 0x64, 0, 0x1F,
        1, 0, 2, 0x68, 0xEE,
        0, 0, 0, 33,
    ];

    private static GenesisChannelItem Channel(string name, string channelId) => new(
        name,
        $"pcp://tracker.example:7144/channel/{channelId}",
        Description: "Games - details",
        BitrateKbps: 1280,
        Type: "FLV",
        ChannelId: channelId);

    private static async Task<BlockedSnapshotRace> BeginBlockedSnapshotRaceAsync(
        Fixture fixture,
        GatedRecordingSourceProvider sourceProvider,
        string newChannelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var firstChannel = Channel("first station", ChannelIdText);
            var firstStart = await fixture.Controller.StartAsync(
                firstChannel,
                CancellationToken.None);
            Assert.Equal(GenesisManualRecordingCommandKind.Started, firstStart.Kind);
            var firstChannelId = new ChannelId(Guid.ParseExact(ChannelIdText, "N"));
            var firstSessionId = fixture.Runtime.GetState(firstChannelId).Session!.SessionId;
            await sourceProvider.BlockedReadEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            var firstStopTask = fixture.Controller.StopAsync(
                firstChannelId,
                firstSessionId,
                CancellationToken.None).AsTask();
            await sourceProvider.StopRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            var snapshotTask = fixture.Controller.GetActiveRecordingSessionsAsync(
                    cancellationToken)
                .AsTask();
            Assert.False(snapshotTask.IsCompleted);

            var newChannel = Channel("new station", newChannelId);
            var newStart = await fixture.Controller.StartAsync(
                newChannel,
                CancellationToken.None);
            Assert.Equal(GenesisManualRecordingCommandKind.Started, newStart.Kind);
            var newId = new ChannelId(Guid.ParseExact(newChannelId, "N"));
            var newSessionId = fixture.Runtime.GetState(newId).Session!.SessionId;

            return new BlockedSnapshotRace(
                firstChannelId,
                firstSessionId,
                firstStopTask,
                snapshotTask,
                newChannel,
                newId,
                newSessionId);
        }
        catch
        {
            sourceProvider.ReleaseBlockedRead.TrySetResult();
            throw;
        }
    }

    private sealed record BlockedSnapshotRace(
        ChannelId FirstChannelId,
        RecordingSessionId FirstSessionId,
        Task<GenesisManualRecordingCommandOutcome> FirstStopTask,
        Task<GenesisActiveRecordingSessionSnapshot> SnapshotTask,
        GenesisChannelItem NewChannel,
        ChannelId NewChannelId,
        RecordingSessionId NewSessionId);

    private static RecordingRuntime CreateRuntime() => new(
        new MemoryRecordingSourceProvider(),
        new DefaultRecordingFilePathProvider(Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-manual-recording-controller-tests",
            Guid.NewGuid().ToString("N"))));

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(IRecordingStreamSourceProvider? sourceProvider = null)
        {
            RecordingDirectory = Path.Combine(
                Path.GetTempPath(),
                "PeerCastNova-manual-recording-controller-tests",
                Guid.NewGuid().ToString("N"));
            Runtime = new RecordingRuntime(
                sourceProvider ?? new MemoryRecordingSourceProvider(),
                new DefaultRecordingFilePathProvider(RecordingDirectory));
            OwnerProvider = new(Runtime);
            Controller = new GenesisManualRecordingSessionController(
                OwnerProvider,
                Runtime);
        }

        public RecordingRuntime Runtime { get; }

        public string RecordingDirectory { get; }

        public FakeOwnerProvider OwnerProvider { get; }

        public GenesisManualRecordingSessionController Controller { get; }

        public async ValueTask DisposeAsync()
        {
            await Controller.DisposeAsync();
            await Runtime.DisposeAsync();
            if (Directory.Exists(RecordingDirectory))
            {
                Directory.Delete(RecordingDirectory, recursive: true);
            }
        }
    }

    private sealed class ManualRecordingPlaybackBridge(
        IGenesisManualRecordingSessionController recordingController) :
        IPlaybackSessionController,
        IGenesisManualRecordingSessionController
    {
        public PlaybackSessionSnapshot Snapshot => PlaybackSessionSnapshot.Stopped;

        public int ManualStopCount { get; private set; }

        public ChannelId? LastManualStopChannelId { get; private set; }

        public RecordingSessionId? LastManualStopSessionId { get; private set; }

        public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel) =>
            recordingController.GetSnapshot(channel);

        public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId) =>
            recordingController.GetSnapshot(channelId);

        public bool IsKnownManualSession(ChannelId channelId, RecordingSessionId sessionId) =>
            recordingController.IsKnownManualSession(channelId, sessionId);

        public ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(
            CancellationToken cancellationToken = default) =>
            recordingController.GetActiveRecordingSessionsAsync(cancellationToken);

        public ValueTask<GenesisManualRecordingSessionSnapshot> GetActiveManualRecordingsAsync(
            CancellationToken cancellationToken = default) =>
            recordingController.GetActiveManualRecordingsAsync(cancellationToken);

        public ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken = default) =>
            recordingController.StartAsync(channel, cancellationToken);

        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken = default) =>
            recordingController.StopAsync(channel, cancellationToken);

        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
            ChannelId channelId,
            RecordingSessionId expectedSessionId,
            CancellationToken cancellationToken = default)
        {
            ManualStopCount++;
            LastManualStopChannelId = channelId;
            LastManualStopSessionId = expectedSessionId;
            return recordingController.StopAsync(
                channelId,
                expectedSessionId,
                cancellationToken);
        }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));
    }

    private sealed class GatedRecordingSourceProvider(
        ChannelId blockedChannelId,
        byte[]? prefix = null) :
        IRecordingStreamSourceProvider
    {
        private static readonly byte[] FlvHeader =
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

        private readonly byte[] _prefix = prefix ?? FlvHeader;

        public TaskCompletionSource BlockedReadEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseBlockedRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Candidate.ChannelId == blockedChannelId)
            {
                return ValueTask.FromResult<Stream>(new GatedReadStream(this));
            }

            return new MemoryRecordingSourceProvider().OpenReadAsync(
                request,
                cancellationToken);
        }

        private sealed class GatedReadStream(GatedRecordingSourceProvider owner) : Stream
        {
            private int _offset;
            private bool _disposed;

            public override bool CanRead => !_disposed;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_offset < owner._prefix.Length)
                {
                    var count = Math.Min(buffer.Length, owner._prefix.Length - _offset);
                    owner._prefix.AsMemory(_offset, count).CopyTo(buffer);
                    _offset += count;
                    return count;
                }

                owner.BlockedReadEntered.TrySetResult();
                using var registration = cancellationToken.Register(
                    static state =>
                    {
                        ((GatedRecordingSourceProvider)state!).StopRequested.TrySetResult();
                    },
                    owner);
                await owner.ReleaseBlockedRead.Task.ConfigureAwait(false);
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                _disposed = true;
                base.Dispose(disposing);
            }
        }
    }

    private sealed class FakeOwnerProvider(RecordingRuntime runtime) :
        IGenesisSharedPcpRecordingOwnerProvider
    {
        public int AttachCount { get; private set; }

        public RecordingRuntime? ReturnedRuntime { get; set; }

        public Action? BeforeReturn { get; set; }

        public bool BlockUntilCanceled { get; set; }

        public SpyLease? LastLease { get; private set; }

        public TaskCompletionSource AttachEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GenesisSharedPcpRecordingOwnerAttachment> AttachRecordingOwnerAsync(
            DailyPcpPlaybackSource source,
            CancellationToken cancellationToken)
        {
            _ = source;
            AttachCount++;
            AttachEntered.TrySetResult();
            if (BlockUntilCanceled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            LastLease = new();
            var attachment = new GenesisSharedPcpRecordingOwnerAttachment(
                ReturnedRuntime ?? runtime,
                Task.FromResult(PcpRelayCoordinatorReadinessOutcome.Ready),
                Task.FromResult<PcpRelayCoordinatorResult>(null!),
                LastLease);
            BeforeReturn?.Invoke();
            return attachment;
        }
    }

    private sealed class SpyLease : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryRecordingSourceProvider : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult<Stream>(
                new BlockingAfterPrefixReadStream(
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
                ]));
        }
    }

    private sealed class BlockingAfterPrefixReadStream(byte[] prefix) : Stream
    {
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
            var copied = Math.Min(count, prefix.Length - _offset);
            prefix.AsSpan(_offset, copied).CopyTo(buffer.AsSpan(offset, copied));
            _offset += copied;
            return copied;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_offset < prefix.Length)
            {
                var copied = Math.Min(buffer.Length, prefix.Length - _offset);
                prefix.AsMemory(_offset, copied).CopyTo(buffer);
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
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}

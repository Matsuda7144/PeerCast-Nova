using System.Text;
using System.Text.Json.Nodes;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingHistoryDurableStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Nova-history-durable-tests", Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_root, "history.json");
    private static readonly ChannelId Channel = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    private static readonly Guid Rule = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Time = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
    private GenesisRecordingHistoryStore Store => new(StorePath);

    [Fact]
    public async Task MissingStoreIsNormalAndLoadShutdownCreatesNothing()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        Assert.Equal(GenesisRecordingHistoryLoadStatus.MissingFile, coordinator.GetSnapshot().LoadStatus);
        Assert.False(coordinator.GetSnapshot().IsDegraded);
        Assert.False(coordinator.GetSnapshot().Pending);
        Assert.Empty(history.GetSnapshot().Entries);
        await coordinator.ShutdownAsync();
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TerminalFactsRoundtripExactWithoutCurrentMediaOrRules(bool automatic, bool stopped)
    {
        var fact = Fact(automatic: automatic, stopped: stopped);
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        history.ObserveTerminal(fact);
        await coordinator.ShutdownAsync();
        Assert.True(File.Exists(StorePath));
        Assert.False(File.Exists(fact.WriteResult!.Path));
        var json = await File.ReadAllTextAsync(StorePath);
        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.DoesNotContain("duration", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pcp://", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ruleName", json, StringComparison.Ordinal);
        var next = new GenesisRecordingHistoryReadModel();
        var restart = new GenesisRecordingHistoryPersistenceCoordinator(next, Store);
        Assert.Equal(GenesisRecordingHistoryLoadStatus.Available, restart.GetSnapshot().LoadStatus);
        Assert.Equal(fact, Assert.Single(next.GetSnapshot().Entries));
        Assert.Equal(1, next.GetSnapshot().Revision);
        Assert.False(restart.GetSnapshot().Pending);
        next.ObserveTerminal(fact);
        Assert.Equal(1, next.GetSnapshot().Revision);
        await restart.ShutdownAsync();
        Assert.Equal(json, await File.ReadAllTextAsync(StorePath));
    }

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.NoDecodableVideoStart, false)]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted, false)]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted, true)]
    public async Task NonSuccessfulTerminalFactsRoundtripWithoutFabrication(RecordingSinkSessionLifecycleKind kind, bool writerKnown)
    {
        var baseFact = Fact();
        var fact = new RecordingSessionTerminalFact(baseFact.Session, null, null, null, Time, Time,
            kind, writerKnown ? baseFact.WriteResult : null,
            kind == RecordingSinkSessionLifecycleKind.Faulted ? RecordingFaultDescriptor.FromSafeDetails(RecordingFailureStage.SourceRead, "IOException") : null);
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        history.ObserveTerminal(fact);
        await coordinator.ShutdownAsync();
        Assert.Equal(fact, Assert.Single(Store.Load().Snapshot!.Entries));
        var json = await File.ReadAllTextAsync(StorePath);
        Assert.DoesNotContain("stackTrace", json, StringComparison.Ordinal);
        Assert.DoesNotContain("message", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoredPlusNewSameChannelRetainsIdsAndContradictionCreatesGap()
    {
        var first = Fact();
        var history = new GenesisRecordingHistoryReadModel();
        history.ObserveTerminal(first);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Saved, (await Store.SaveAsync(Metadata(history), null, default)).Status);
        var next = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(next, Store);
        next.ObserveTerminal(first);
        next.ObserveTerminal(new(first.Session, "contradiction", first.StreamType, first.RequestedFileExtension,
            Time, Time.AddSeconds(2), first.Kind, first.WriteResult));
        var second = Fact();
        next.ObserveTerminal(second);
        Assert.Equal(first, next.Find(first.Session.SessionId));
        Assert.Equal(2, next.GetSnapshot().Entries.Count);
        Assert.Equal(1, next.GetSnapshot().PublicationFailureCount);
        await coordinator.ShutdownAsync();
        var loaded = Store.Load().Snapshot!;
        Assert.Equal(2, loaded.Entries.Count);
        Assert.True(loaded.HasGap);
        Assert.Equal(1, loaded.PublicationFailureCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapacityTruncationAndGapSurviveRestartAndOldReplay(bool overflow)
    {
        var history = new GenesisRecordingHistoryReadModel();
        var oldest = Fact(sequence: 0);
        history.ObserveTerminal(oldest);
        for (var i = 1; i < 4096 + (overflow ? 1 : 0); i++) history.ObserveTerminal(Fact(sequence: i));
        history.RecordPublicationFailure();
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Saved, (await Store.SaveAsync(Metadata(history), null, default)).Status);
        var next = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(next, Store);
        Assert.Equal(4096, next.GetSnapshot().Entries.Count);
        Assert.Equal(overflow ? 1 : 0, next.GetSnapshot().EvictedEntryCount);
        Assert.Equal(1, next.GetSnapshot().PublicationFailureCount);
        Assert.Equal(overflow, next.GetSnapshot().IsTruncated);
        Assert.True(next.GetSnapshot().HasGap);
        var revision = next.GetSnapshot().Revision;
        next.ObserveTerminal(oldest);
        Assert.Equal(revision, next.GetSnapshot().Revision);
        Assert.Equal(4096, next.GetSnapshot().Entries.Count);
        await coordinator.ShutdownAsync();
    }

    [Theory]
    [InlineData("malformed", 1)]
    [InlineData("malformed", 2)]
    [InlineData("null", 1)]
    [InlineData("null", 2)]
    [InlineData("version", 1)]
    [InlineData("version", 2)]
    [InlineData("duplicateSession", 1)]
    [InlineData("duplicateSession", 2)]
    [InlineData("enum", 1)]
    [InlineData("enum", 2)]
    [InlineData("active", 1)]
    [InlineData("active", 2)]
    [InlineData("noWriter", 1)]
    [InlineData("noWriter", 2)]
    [InlineData("noStartWriter", 1)]
    [InlineData("noStartWriter", 2)]
    [InlineData("nonAbsolutePath", 1)]
    [InlineData("nonAbsolutePath", 2)]
    [InlineData("negativeBytes", 1)]
    [InlineData("negativeBytes", 2)]
    [InlineData("manualRule", 1)]
    [InlineData("manualRule", 2)]
    [InlineData("automaticNoRule", 1)]
    [InlineData("automaticNoRule", 2)]
    [InlineData("emptyId", 1)]
    [InlineData("emptyId", 2)]
    [InlineData("emptyChannel", 1)]
    [InlineData("emptyChannel", 2)]
    [InlineData("missingField", 1)]
    [InlineData("missingField", 2)]
    [InlineData("duplicateProperty", 1)]
    [InlineData("duplicateProperty", 2)]
    [InlineData("faultOnCompleted", 1)]
    [InlineData("faultOnCompleted", 2)]
    [InlineData("unsafeFaultType", 1)]
    [InlineData("unsafeFaultType", 2)]
    [InlineData("nonUtc", 1)]
    [InlineData("nonUtc", 2)]
    [InlineData("defaultTime", 1)]
    [InlineData("defaultTime", 2)]
    [InlineData("negativeCount", 1)]
    [InlineData("negativeCount", 2)]
    [InlineData("badBoundary", 1)]
    [InlineData("badBoundary", 2)]
    [InlineData("tooMany", 1)]
    [InlineData("tooMany", 2)]
    [InlineData("nullEntries", 1)]
    [InlineData("nullEntries", 2)]
    public async Task InvalidAndUnsupportedOriginalsRemainUnchangedIncludingNewFactsAndShutdown(string mutation, int schemaVersion)
    {
        var bytes = InvalidBytes(mutation, schemaVersion);
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(StorePath, bytes);
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        Assert.Equal(mutation == "version" ? GenesisRecordingHistoryLoadStatus.UnsupportedVersion : GenesisRecordingHistoryLoadStatus.Invalid,
            coordinator.GetSnapshot().LoadStatus);
        Assert.False(coordinator.GetSnapshot().AutomaticPersistenceEnabled);
        Assert.True(coordinator.GetSnapshot().IsDegraded);
        Assert.Empty(history.GetSnapshot().Entries);
        history.ObserveTerminal(Fact());
        await coordinator.FlushAsync();
        await coordinator.ShutdownAsync();
        Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(StorePath));
        Assert.True(coordinator.GetSnapshot().Pending);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task OversizedInputIsRejectedAndProtected()
    {
        Directory.CreateDirectory(_root);
        using (var file = File.Create(StorePath)) file.SetLength(GenesisRecordingHistoryStore.MaximumByteCount + 1L);
        var before = new FileInfo(StorePath).Length;
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        Assert.Equal(GenesisRecordingHistoryStoreFailure.TooLarge, coordinator.GetSnapshot().Failure);
        Assert.Equal(GenesisRecordingHistoryLoadStatus.Invalid, coordinator.GetSnapshot().LoadStatus);
        history.ObserveTerminal(Fact());
        await coordinator.ShutdownAsync();
        Assert.Equal(before, new FileInfo(StorePath).Length);
    }

    [Fact]
    public async Task OversizedSerializedOutputDoesNotCreateDestination()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var original = Fact();
        history.ObserveTerminal(new(original.Session, new string('x', GenesisRecordingHistoryStore.MaximumByteCount), null, null,
            Time, Time, original.Kind, original.WriteResult));
        var result = await Store.SaveAsync(Metadata(history), null, default);
        Assert.Equal(GenesisRecordingHistoryStoreFailure.TooLarge, result.Failure);
        Assert.False(File.Exists(StorePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicTemporaryAndReplacementFailurePreserveOldStoreAndMedia(bool failReplace)
    {
        var history = new GenesisRecordingHistoryReadModel();
        history.ObserveTerminal(Fact());
        var first = await Store.SaveAsync(Metadata(history), null, default);
        var before = await File.ReadAllBytesAsync(StorePath);
        var media = Path.Combine(_root, "untouched.flv");
        await File.WriteAllTextAsync(media, "media sentinel");
        history.ObserveTerminal(Fact());
        var failing = new GenesisRecordingHistoryStore(StorePath,
            writeTemporary: failReplace ? null : async (path, bytes, token) =>
            { await File.WriteAllBytesAsync(path, bytes[..10], token); throw new IOException("temporary failure"); },
            replace: failReplace ? (_, _) => throw new IOException("replace failure") : null);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Failed, (await failing.SaveAsync(Metadata(history), first.Fingerprint, default)).Status);
        Assert.Equal(before, await File.ReadAllBytesAsync(StorePath));
        Assert.Equal("media sentinel", await File.ReadAllTextAsync(media));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        var saved = await Store.SaveAsync(Metadata(history), first.Fingerprint, default);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Saved, saved.Status);
        Assert.Equal(2, Store.Load().Snapshot!.Entries.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalMutationBeforeOrDuringTemporaryWriteIsConflict(bool duringWrite)
    {
        var history = new GenesisRecordingHistoryReadModel();
        history.ObserveTerminal(Fact());
        var first = await Store.SaveAsync(Metadata(history), null, default);
        var external = Encoding.UTF8.GetBytes("external owner document");
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        {
            await File.WriteAllBytesAsync(path, bytes, token);
            if (duringWrite) await File.WriteAllBytesAsync(StorePath, external, token);
        });
        history.ObserveTerminal(Fact());
        if (!duringWrite) await File.WriteAllBytesAsync(StorePath, external);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Conflict, (await store.SaveAsync(Metadata(history), first.Fingerprint, default)).Status);
        Assert.Equal(external, await File.ReadAllBytesAsync(StorePath));
    }

    [Fact]
    public async Task ConflictingCompositionDisablesFurtherWrites()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(StorePath, "external");
        history.ObserveTerminal(Fact());
        await coordinator.FlushAsync();
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Conflict, coordinator.GetSnapshot().LastWriteStatus);
        Assert.False(coordinator.GetSnapshot().AutomaticPersistenceEnabled);
        history.ObserveTerminal(Fact());
        await coordinator.ShutdownAsync();
        Assert.Equal("external", await File.ReadAllTextAsync(StorePath));
    }

    [Fact]
    public async Task CooperativeWriterLockPreventsConcurrentReplacement()
    {
        var entered = Gate(); var release = Gate();
        var history = new GenesisRecordingHistoryReadModel(); history.ObserveTerminal(Fact());
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        { entered.TrySetResult(); await release.Task.WaitAsync(token); await File.WriteAllBytesAsync(path, bytes, token); });
        var write = store.SaveAsync(Metadata(history), null, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var other = await Store.SaveAsync(Metadata(history), null, default);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Failed, other.Status);
        Assert.False(File.Exists(StorePath));
        release.TrySetResult();
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Saved, (await write).Status);
    }

    [Fact]
    public async Task SlowWriteCoalescesRapidRevisionsAndPersistsLatestWithoutBlockingCapture()
    {
        var entered = Gate(); var release = Gate(); var writes = 0;
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        {
            if (Interlocked.Increment(ref writes) == 1) { entered.TrySetResult(); await release.Task.WaitAsync(token); }
            await File.WriteAllBytesAsync(path, bytes, token);
        });
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store);
        history.ObserveTerminal(Fact());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 100; i++) history.ObserveTerminal(Fact());
        Assert.Equal(101, history.GetSnapshot().Entries.Count);
        Assert.True(coordinator.GetSnapshot().Pending);
        release.TrySetResult();
        await coordinator.FlushAsync();
        Assert.Equal(101, Store.Load().Snapshot!.Entries.Count);
        Assert.Equal(2, writes);
        var last = history.GetSnapshot().Entries[0];
        history.ObserveTerminal(last);
        await coordinator.FlushAsync();
        await coordinator.ShutdownAsync();
        Assert.Equal(2, writes);
        Assert.False(coordinator.GetSnapshot().Pending);
    }

    [Fact]
    public async Task PersistentFailureAttemptsOncePerRevisionThenLaterChangeRetriesLatest()
    {
        var attempts = 0; var fail = true;
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        { Interlocked.Increment(ref attempts); if (fail) throw new IOException("disk"); await File.WriteAllBytesAsync(path, bytes, token); });
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store);
        history.ObserveTerminal(Fact());
        await coordinator.FlushAsync();
        Assert.True(coordinator.GetSnapshot().Pending);
        Assert.True(coordinator.GetSnapshot().IsDegraded);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Failed, coordinator.GetSnapshot().LastWriteStatus);
        for (var i = 0; i < 20; i++) await coordinator.FlushAsync();
        Assert.Equal(1, attempts);
        fail = false;
        history.ObserveTerminal(Fact());
        await coordinator.FlushAsync();
        Assert.Equal(2, attempts);
        Assert.Equal(2, Store.Load().Snapshot!.Entries.Count);
        Assert.False(coordinator.GetSnapshot().Pending);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task ShutdownMakesOneFinalRetryAndReportsFailureWithoutThrowing()
    {
        var attempts = 0;
        var store = new GenesisRecordingHistoryStore(StorePath, (_, _, _) =>
        { Interlocked.Increment(ref attempts); throw new IOException("disk"); });
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store);
        history.ObserveTerminal(Fact());
        await coordinator.FlushAsync();
        await coordinator.ShutdownAsync();
        await coordinator.ShutdownAsync();
        Assert.Equal(2, attempts);
        Assert.True(coordinator.GetSnapshot().Pending);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Failed, coordinator.GetSnapshot().LastWriteStatus);
        Assert.Single(history.GetSnapshot().Entries);
    }

    [Fact]
    public async Task CancelledWriteCannotReplaceDestination()
    {
        var entered = Gate();
        var store = new GenesisRecordingHistoryStore(StorePath, async (_, _, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        var history = new GenesisRecordingHistoryReadModel(); history.ObserveTerminal(Fact());
        using var cancellation = new CancellationTokenSource();
        var write = store.SaveAsync(Metadata(history), null, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Cancelled, (await write).Status);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task UnavailablePathIsProtectedWithoutBlockingInMemoryHistory()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, new(null));
        Assert.Equal(GenesisRecordingHistoryLoadStatus.Unavailable, coordinator.GetSnapshot().LoadStatus);
        history.ObserveTerminal(Fact()); await coordinator.ShutdownAsync();
        Assert.Single(history.GetSnapshot().Entries);
        Assert.False(coordinator.GetSnapshot().AutomaticPersistenceEnabled);
    }

    [Fact]
    public void DedicatedPathAndForwardOptionalPropertiesHaveExplicitPolicy()
    {
        Assert.Equal(Path.Combine(_root, "PeerCast Nova", "recording-history.json"), GenesisRecordingHistoryStore.ResolvePath(_root));
        Assert.Null(GenesisRecordingHistoryStore.ResolvePath(""));
        var json = ValidDocument(); json["futureOptional"] = "ignored";
        Assert.Single(GenesisRecordingHistoryJsonCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())).Entries);
    }

    private RecordingSessionTerminalFact Fact(bool automatic = false, bool stopped = false, int sequence = 0) => new(
        automatic ? new RecordingSinkSession(new(Guid.NewGuid()), Channel, Rule) : RecordingSinkSession.Manual(new(Guid.NewGuid()), Channel),
        "Original channel", "DATA", "bin", Time.AddSeconds(sequence), Time.AddSeconds(sequence + 1),
        RecordingSinkSessionLifecycleKind.Completed,
        RecordingFileWriteResult.FromFinalizedFacts(Path.GetFullPath(Path.Combine(_root, "not-required", $"{Guid.NewGuid():N}.bin")), 1234567,
            stopped ? RecordingFileWriteCompletionKind.Stopped : RecordingFileWriteCompletionKind.EndOfStream));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCompositionShutdownCapturesThenFlushesAndRestartHydratesBeforeAdmission(bool automatic)
    {
        var output = Path.Combine(_root, "media");
        var settings = new GenesisRecordingOutputDirectorySettings(new(Path.Combine(_root, "settings.json")), output);
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, Store);
        using var stream = new BlockingStream();
        using var registration = composition.RelayRuntime.RecordingStreamSourceLeaseRegistry.Register(Channel, new LeaseFactory(stream));
        var candidate = new RecordingCandidate(Channel, new(name: "admitted name", streamType: "DATA", streamExtension: "bin"));
        var session = automatic
            ? (await composition.Recording.EvaluateAutomaticAsync(candidate, new RecordingRuleSet([new(Rule, "current mutable rule", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll)]))).State.Session!
            : (await composition.Recording.StartManualAsync(candidate)).State.Session!;
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(composition.RecordingHistory.GetSnapshot().Entries);
        await composition.DisposeAsync();
        var fact = Assert.Single(composition.RecordingHistory.GetSnapshot().Entries);
        Assert.Equal(session, fact.Session);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, fact.WriteResult!.CompletionKind);
        Assert.Equal(3, fact.WriteResult.BytesWritten);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(fact.WriteResult.Path));
        Assert.False(composition.GetHistoryDurabilitySnapshot()!.Pending);
        await using var next = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, Store);
        Assert.Equal(fact, Assert.Single(next.RecordingHistory.GetSnapshot().Entries));
        Assert.Empty(await next.Recording.GetActiveRecordingSessionsAsync());
        Assert.Null(next.Recording.GetState(Channel).Session);
        Assert.Equal(0, next.RelayRuntime.RecordingStreamSourceLeaseRegistry.Count);
        Assert.Equal(0, next.RelayRuntime.OwnedCoordinatorCount);
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWriterSuccessIsIsolatedFromPersistenceFailureIncludingShutdown(bool shutdown)
    {
        var failingStore = new GenesisRecordingHistoryStore(StorePath, (_, _, _) => throw new IOException("store disk failure"));
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, failingStore);
        using var source = new BlockingStream();
        var runtime = new RecordingRuntime(new SourceProvider(source), new DefaultRecordingFilePathProvider(Path.Combine(_root, "media")),
            terminalObserver: history);
        var session = (await runtime.StartManualAsync(new(Channel, new(name: "actual", streamType: "DATA", streamExtension: "bin")))).State.Session!;
        await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (shutdown) await runtime.DisposeAsync();
        else
        {
            Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await runtime.StopManualAsync(Channel, session.SessionId)).Kind);
            await runtime.DisposeAsync();
        }
        await coordinator.ShutdownAsync();
        var fact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(RecordingSinkSessionLifecycleKind.Completed, fact.Kind);
        Assert.Null(fact.Fault);
        Assert.Equal(3, fact.WriteResult!.BytesWritten);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(fact.WriteResult.Path));
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Failed, coordinator.GetSnapshot().LastWriteStatus);
        Assert.True(coordinator.GetSnapshot().Pending);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task SlowMetadataWriteNeverBlocksActualRecorderStop()
    {
        var entered = Gate(); var release = Gate();
        var history = new GenesisRecordingHistoryReadModel();
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        { entered.TrySetResult(); await release.Task.WaitAsync(token); await File.WriteAllBytesAsync(path, bytes, token); });
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store);
        using var source = new BlockingStream();
        await using var runtime = new RecordingRuntime(new SourceProvider(source), new DefaultRecordingFilePathProvider(Path.Combine(_root, "media")), terminalObserver: history);
        var session = (await runtime.StartManualAsync(new(Channel, new(streamType: "DATA", streamExtension: "bin")))).State.Session!;
        await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stop = runtime.StopManualAsync(Channel, session.SessionId).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await stop.WaitAsync(TimeSpan.FromSeconds(10))).Kind);
        Assert.False(release.Task.IsCompleted);
        Assert.True(File.Exists(Assert.Single(history.GetSnapshot().Entries).WriteResult!.Path));
        release.TrySetResult();
        await coordinator.ShutdownAsync();
        Assert.Single(Store.Load().Snapshot!.Entries);
    }

    [Fact]
    public async Task InvalidLoadDoesNotPreventActualRecordingFinalization()
    {
        Directory.CreateDirectory(_root); await File.WriteAllTextAsync(StorePath, "invalid original");
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store);
        using var source = new BlockingStream();
        await using var runtime = new RecordingRuntime(new SourceProvider(source), new DefaultRecordingFilePathProvider(Path.Combine(_root, "media")), terminalObserver: history);
        var session = (await runtime.StartManualAsync(new(Channel, new(streamType: "DATA", streamExtension: "bin")))).State.Session!;
        await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await runtime.StopManualAsync(Channel, session.SessionId)).Kind);
        await coordinator.ShutdownAsync();
        Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal("invalid original", await File.ReadAllTextAsync(StorePath));
    }

    [Fact]
    public async Task ShutdownWaitIsBoundedAndCancelledWorkerCannotWriteLater()
    {
        var entered = Gate();
        var store = new GenesisRecordingHistoryStore(StorePath, async (_, _, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        var history = new GenesisRecordingHistoryReadModel();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store);
        history.ObserveTerminal(Fact()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Cancellation is a shutdown gate, not a sleep-based race assertion.
        await coordinator.ShutdownAsync(TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(10));
        await coordinator.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Cancelled, coordinator.GetSnapshot().LastWriteStatus);
        Assert.True(coordinator.GetSnapshot().Pending);
        Assert.False(File.Exists(StorePath));
    }

    private sealed class SourceProvider(Stream stream) : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(stream);
    }
    private sealed class LeaseFactory(Stream stream) : IRecordingStreamSourceLeaseFactory
    {
        public ValueTask<IRecordingStreamSourceLease?> AcquireAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IRecordingStreamSourceLease?>(new Lease(stream));
    }
    private sealed class Lease(Stream stream) : IRecordingStreamSourceLease
    {
        public Stream ReadStream => stream;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
    private sealed class BlockingStream() : MemoryStream([1, 2, 3], writable: false)
    {
        public TaskCompletionSource Waiting { get; } = Gate();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            Waiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private static GenesisRecordingMetadataSnapshot Metadata(GenesisRecordingHistoryReadModel history) =>
        new(history.GetSnapshot(), new(0, Array.Empty<GenesisRecordingLibraryKnownRoot>()));

    private JsonObject ValidDocument()
    {
        var history = new GenesisRecordingHistoryReadModel(); history.ObserveTerminal(Fact());
        return JsonNode.Parse(GenesisRecordingHistoryJsonCodec.Serialize(history.GetSnapshot()))!.AsObject();
    }

    private byte[] InvalidBytes(string mutation, int schemaVersion = 2)
    {
        if (mutation == "malformed") return Encoding.UTF8.GetBytes("{broken");
        if (mutation == "null") return Encoding.UTF8.GetBytes("null");
        var json = ValidDocument(); json["schemaVersion"] = schemaVersion; if (schemaVersion == 1) json.Remove("knownRoots"); var entries = json["entries"]!.AsArray(); var entry = entries[0]!.AsObject();
        switch (mutation)
        {
            case "version": json["schemaVersion"] = 99; break;
            case "duplicateSession": entries.Add(entry.DeepClone()); break;
            case "enum": entry["origin"] = "Mystery"; break;
            case "active": entry["kind"] = "Active"; break;
            case "noWriter": entry["writeResult"] = null; break;
            case "noStartWriter": entry["kind"] = "NoDecodableVideoStart"; break;
            case "nonAbsolutePath": entry["writeResult"]!["path"] = "relative.bin"; break;
            case "negativeBytes": entry["writeResult"]!["bytesWritten"] = -1; break;
            case "manualRule": entry["automaticRuleId"] = Rule; break;
            case "automaticNoRule": entry["origin"] = "AutomaticRule"; break;
            case "emptyId": entry["sessionId"] = Guid.Empty; break;
            case "emptyChannel": entry["channelId"] = Guid.Empty; break;
            case "missingField": entry.Remove("channelName"); break;
            case "duplicateProperty": return Encoding.UTF8.GetBytes(json.ToJsonString().Replace($"\"schemaVersion\":{schemaVersion}", $"\"schemaVersion\":{schemaVersion},\"schemaVersion\":{schemaVersion}", StringComparison.Ordinal));
            case "faultOnCompleted": entry["fault"] = new JsonObject { ["stage"] = "SourceRead", ["exceptionType"] = "IOException" }; break;
            case "unsafeFaultType": entry["kind"] = "Faulted"; entry["fault"] = new JsonObject { ["stage"] = "SourceRead", ["exceptionType"] = "credential://secret" }; break;
            case "nonUtc": entry["admissionObservedAtUtc"] = Time.ToOffset(TimeSpan.FromHours(9)); break;
            case "defaultTime": entry["admissionObservedAtUtc"] = default(DateTimeOffset); break;
            case "negativeCount": json["publicationFailureCount"] = -1; break;
            case "badBoundary": json["evictedEntryCount"] = 1; break;
            case "tooMany": for (var i = 0; i < 4096; i++) entries.Add(entry.DeepClone()); break;
            case "nullEntries": json["entries"] = null; break;
        }
        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingHistoryReadModelTests : IDisposable
{
    private static readonly ChannelId Channel = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    private static readonly Guid RuleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset AdmissionTime = new(2026, 10, 7, 1, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Nova-history-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalEofRetainsWriterFactsAndStartSnapshotBeforeCleanup(bool automatic)
    {
        using var source = new ControlledStream([1, 2, 3, 4]);
        var history = new GenesisRecordingHistoryReadModel();
        var clock = new TestClock(AdmissionTime);
        var observer = new CaptureObserver(history);
        await using var runtime = Runtime(source, observer, clock);
        var session = await Start(runtime, automatic);
        await source.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(history.GetSnapshot().Entries);
        clock.Now = AdmissionTime.AddSeconds(12);
        source.ReleaseEnd.TrySetResult();
        var fact = await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(session, fact.Session);
        Assert.Equal(automatic ? RecordingSessionOrigin.AutomaticRule : RecordingSessionOrigin.Manual, fact.Session.Origin);
        Assert.Equal(automatic ? RuleId : (Guid?)null, fact.Session.AutomaticRuleId);
        Assert.Equal("Original channel", fact.ChannelName);
        Assert.Equal("DATA", fact.StreamType);
        Assert.Equal("bin", fact.RequestedFileExtension);
        Assert.Equal(AdmissionTime, fact.AdmissionObservedAtUtc);
        Assert.Equal(clock.Now, fact.TerminalObservedAtUtc);
        Assert.Equal(RecordingSinkSessionLifecycleKind.Completed, fact.Kind);
        Assert.Equal(RecordingFileWriteCompletionKind.EndOfStream, fact.WriteResult!.CompletionKind);
        Assert.Equal(4, fact.WriteResult.BytesWritten);
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(fact.WriteResult.Path));
        Assert.StartsWith(Path.GetFullPath(_directory), fact.WriteResult.Path, StringComparison.Ordinal);
        Assert.Null(fact.Fault);

        Assert.Empty(await runtime.GetActiveRecordingSessionsAsync());
        Assert.Same(fact, Assert.Single(history.GetSnapshot().Entries));
        Assert.Equal(session.SessionId, history.Find(session.SessionId)!.Session.SessionId);
        Assert.Empty(new GenesisRecordingHistoryReadModel().GetSnapshot().Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopRetainsExactSessionOnceIncludingAdmittedAutomaticStop(bool automatic)
    {
        using var source = new ControlledStream([5, 6]);
        var history = new GenesisRecordingHistoryReadModel();
        await using var runtime = Runtime(source, history);
        var session = await Start(runtime, automatic);
        await source.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (automatic)
        {
            var result = await runtime.RequestAutomaticStopAsync(Channel, session.SessionId);
            Assert.NotNull(result.Admission);
            Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await result.Admission.Completion).Kind);
        }
        else
        {
            Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await runtime.StopManualAsync(Channel, session.SessionId)).Kind);
        }

        var snapshot = history.GetSnapshot();
        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal(session, entry.Session);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, entry.WriteResult!.CompletionKind);
        Assert.Equal(2, entry.WriteResult.BytesWritten);
        Assert.True(File.Exists(entry.WriteResult.Path));
        _ = await runtime.StopAsync(Channel);
        _ = await runtime.StopManualAsync(Channel, session.SessionId);
        Assert.Equal(snapshot, history.GetSnapshot() with { Entries = snapshot.Entries });
        Assert.Single(history.GetSnapshot().Entries);
        Assert.Empty(await runtime.GetActiveRecordingSessionsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownCapturesKnownFinalizationWithoutChangingCleanup(bool automatic)
    {
        using var source = new ControlledStream([7]);
        var history = new GenesisRecordingHistoryReadModel();
        var runtime = Runtime(source, history);
        var session = await Start(runtime, automatic);
        await source.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.DisposeAsync();
        await runtime.DisposeAsync();
        var fact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(session, fact.Session);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, fact.WriteResult!.CompletionKind);
        Assert.True(source.Disposed);
        Assert.True(File.Exists(fact.WriteResult.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoDecodableStartIsSeparateAndHasNoFabricatedFile(bool automatic)
    {
        var history = new GenesisRecordingHistoryReadModel();
        var observer = new CaptureObserver(history);
        await using var runtime = new RecordingRuntime(
            new SourceProvider(new MemoryStream(AvcConfigurationWithoutMedia())),
            new DefaultRecordingFilePathProvider(_directory),
            terminalObserver: observer);
        var session = await Start(runtime, automatic, "FLV", "flv");
        var fact = await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(session, fact.Session);
        Assert.Equal(RecordingSinkSessionLifecycleKind.NoDecodableVideoStart, fact.Kind);
        Assert.Null(fact.WriteResult);
        Assert.Null(fact.Fault);
        Assert.Equal("FLV", fact.StreamType);
        Assert.Equal("flv", fact.RequestedFileExtension);
        Assert.Empty(await runtime.GetActiveRecordingSessionsAsync());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultRetainsSafeTypeAndStageWithoutExceptionPayload(bool automatic)
    {
        using var source = new ControlledStream([1]) { FailRead = true };
        var history = new GenesisRecordingHistoryReadModel();
        var observer = new CaptureObserver(history);
        await using var runtime = Runtime(source, observer);
        var session = await Start(runtime, automatic);
        source.ReleaseEnd.TrySetResult();
        var fact = await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(session, fact.Session);
        Assert.Equal(RecordingSinkSessionLifecycleKind.Faulted, fact.Kind);
        Assert.Equal(RecordingFailureStage.SourceRead, fact.Fault!.Stage);
        Assert.Equal(nameof(IOException), fact.Fault.ExceptionType);
        Assert.DoesNotContain("credential-secret", fact.ToString(), StringComparison.Ordinal);
        Assert.Null(fact.WriteResult);
        Assert.Empty(await runtime.GetActiveRecordingSessionsAsync());
        var savedFile = Assert.Single(Directory.GetFiles(_directory));
        Assert.False(savedFile.EndsWith(".partial", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(savedFile));
    }

    [Fact]
    public async Task SourceDisposeFaultKeepsOnlyAlreadyProvenWriterFileFacts()
    {
        using var source = new ControlledStream([1, 2]) { FailDispose = true };
        var history = new GenesisRecordingHistoryReadModel();
        var observer = new CaptureObserver(history);
        await using var runtime = Runtime(source, observer);
        _ = await Start(runtime, false);
        source.ReleaseEnd.TrySetResult();
        var fact = await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordingSinkSessionLifecycleKind.Faulted, fact.Kind);
        Assert.Equal(nameof(IOException), fact.Fault!.ExceptionType);
        Assert.Equal(RecordingFailureStage.Unknown, fact.Fault.Stage);
        Assert.Equal(2, fact.WriteResult!.BytesWritten);
        Assert.True(File.Exists(fact.WriteResult.Path));
        Assert.Empty(await runtime.GetActiveRecordingSessionsAsync());
    }

    [Theory]
    [InlineData("cancel-before")]
    [InlineData("cancel-open")]
    [InlineData("reject-open")]
    public async Task UnadmittedStartsCreateNoTerminalHistory(string scenario)
    {
        var history = new GenesisRecordingHistoryReadModel();
        using var cancellation = new CancellationTokenSource();
        var provider = new PendingSourceProvider();
        await using var runtime = new RecordingRuntime(provider, new DefaultRecordingFilePathProvider(_directory), terminalObserver: history);
        if (scenario == "cancel-before") cancellation.Cancel();
        var start = runtime.StartManualAsync(Candidate(), cancellation.Token).AsTask();
        if (scenario != "cancel-before")
        {
            await provider.Opening.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (scenario == "cancel-open") cancellation.Cancel();
            else provider.Release.TrySetException(new IOException("source unavailable"));
        }

        if (scenario == "reject-open")
            await Assert.ThrowsAsync<IOException>(() => start);
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Empty(history.GetSnapshot().Entries);
        Assert.False(history.GetSnapshot().HasGap);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task CancelledStopWaitStillCapturesLaterActualTerminal()
    {
        using var source = new ControlledStream([1, 2]) { HoldStop = true };
        var history = new GenesisRecordingHistoryReadModel();
        await using var runtime = Runtime(source, history);
        var session = await Start(runtime, false);
        await source.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var stop = runtime.StopManualAsync(Channel, session.SessionId, cancellation.Token).AsTask();
        await source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        Assert.Empty(history.GetSnapshot().Entries);
        source.ReleaseStop.TrySetResult();
        _ = await runtime.StopManualAsync(Channel, session.SessionId);
        var fact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(session, fact.Session);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, fact.WriteResult!.CompletionKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EofBeforeOuterStartReturnNeedsNoControllerRegistration(bool automatic)
    {
        using var source = new ControlledStream([3, 4]);
        var history = new GenesisRecordingHistoryReadModel();
        var observer = new CaptureObserver(history);
        await using var runtime = Runtime(source, observer);
        var beforeOuterReturn = new TaskCompletionSource<RecordingSinkSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOuter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RecordingSinkSession> OuterStart()
        {
            var session = await Start(runtime, automatic);
            beforeOuterReturn.TrySetResult(session);
            await releaseOuter.Task;
            return session;
        }

        var outer = OuterStart();
        var admitted = await beforeOuterReturn.Task.WaitAsync(TimeSpan.FromSeconds(10));
        source.ReleaseEnd.TrySetResult();
        var fact = await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(outer.IsCompleted);
        Assert.Equal(admitted, Assert.Single(history.GetSnapshot().Entries).Session);
        Assert.Equal("Original channel", fact.ChannelName);
        Assert.Equal(automatic ? RuleId : (Guid?)null, fact.Session.AutomaticRuleId);
        Assert.Equal(2, fact.WriteResult!.BytesWritten);
        releaseOuter.TrySetResult();
        Assert.Equal(admitted, await outer);
        history.ObserveTerminal(fact);
        Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(1, history.GetSnapshot().Revision);
    }

    [Fact]
    public async Task DelayedOldTerminalAfterNewSessionStartsCannotChangeCurrentActiveSession()
    {
        using var first = new ControlledStream([1]);
        using var second = new ControlledStream([2]);
        var history = new GenesisRecordingHistoryReadModel();
        var deferred = new CaptureObserver(history) { DeferCapture = true };
        await using var runtime = new RecordingRuntime(
            new SourceProvider(first, second),
            new DefaultRecordingFilePathProvider(_directory),
            terminalObserver: deferred);
        var a = await Start(runtime, true);
        await first.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _ = await runtime.StopAsync(Channel);
        var oldFact = await deferred.Terminal.Task;
        var b = await Start(runtime, true);
        Assert.NotEqual(a.SessionId, b.SessionId);
        history.ObserveTerminal(oldFact);
        history.ObserveTerminal(oldFact);
        Assert.Equal(b, Assert.Single(await runtime.GetActiveRecordingSessionsAsync()));
        Assert.Equal(a, Assert.Single(history.GetSnapshot().Entries).Session);
        deferred.DeferCapture = false;
        await second.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _ = await runtime.StopAsync(Channel);
        Assert.Equal(2, history.GetSnapshot().Entries.Count);
        Assert.NotEqual(history.GetSnapshot().Entries[0].WriteResult!.Path, history.GetSnapshot().Entries[1].WriteResult!.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingObserverCannotChangeEofOrStopOrDeleteFinalFile(bool stop)
    {
        using var source = new ControlledStream([8, 9]);
        var history = new GenesisRecordingHistoryReadModel();
        var observer = new FailingObserver(history);
        await using var runtime = Runtime(source, observer);
        var session = await Start(runtime, false);
        await source.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (!stop)
        {
            source.ReleaseEnd.TrySetResult();
            await observer.Observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var result = await runtime.StopAsync(Channel);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, result.Kind);
        Assert.Null(result.Fault);
        Assert.Empty(history.GetSnapshot().Entries);
        Assert.Equal(1, history.GetSnapshot().PublicationFailureCount);
        Assert.True(history.GetSnapshot().HasGap);
        Assert.Equal(1, history.GetSnapshot().Revision);
        Assert.Equal([8, 9], await File.ReadAllBytesAsync(observer.Fact!.WriteResult!.Path));
        Assert.Equal(session, observer.Fact.Session);
        Assert.Equal(stop ? RecordingFileWriteCompletionKind.Stopped : RecordingFileWriteCompletionKind.EndOfStream,
            observer.Fact.WriteResult.CompletionKind);
    }

    [Fact]
    public async Task ThrowingObservationClockDoesNotFabricateTimestampsOrFaultRecording()
    {
        using var source = new ControlledStream([1]);
        var history = new GenesisRecordingHistoryReadModel();
        await using var runtime = Runtime(source, history, new BrokenClock());
        _ = await Start(runtime, false);
        await source.WaitingForEnd.Task;
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await runtime.StopAsync(Channel)).Kind);
        Assert.Empty(history.GetSnapshot().Entries);
        Assert.Equal(1, history.GetSnapshot().PublicationFailureCount);
        Assert.Single(Directory.GetFiles(_directory, "*.bin"));
    }

    [Fact]
    public async Task TerminalClockFailureLeavesWriterSuccessAndSeparatelyVisibleHistoryGap()
    {
        using var source = new ControlledStream([1, 2]);
        var history = new GenesisRecordingHistoryReadModel();
        var clock = new TestClock(AdmissionTime);
        await using var runtime = Runtime(source, history, clock);
        _ = await Start(runtime, false);
        await source.WaitingForEnd.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Fail = true;
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await runtime.StopAsync(Channel)).Kind);
        Assert.Empty(history.GetSnapshot().Entries);
        Assert.Equal(1, history.GetSnapshot().PublicationFailureCount);
        Assert.Single(Directory.GetFiles(_directory, "*.bin"));
    }

    [Fact]
    public async Task ConcurrentDuplicatePublicationIsIdempotent()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var fact = Fact(1);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => history.ObserveTerminal(fact))));
        Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(1, history.GetSnapshot().Revision);
        Assert.False(history.GetSnapshot().HasGap);
    }

    [Fact]
    public void ObservationTimesAreNormalizedUtcWithoutInventingMediaDuration()
    {
        var local = AdmissionTime.ToOffset(TimeSpan.FromHours(9));
        var fact = new RecordingSessionTerminalFact(Fact(1).Session, null, null, null,
            local, local.AddSeconds(1), RecordingSinkSessionLifecycleKind.NoDecodableVideoStart);
        Assert.Equal(TimeSpan.Zero, fact.AdmissionObservedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, fact.TerminalObservedAtUtc.Offset);
        Assert.Equal(AdmissionTime, fact.AdmissionObservedAtUtc);
        Assert.DoesNotContain(typeof(RecordingSessionTerminalFact).GetProperties(),
            p => p.Name.Contains("Duration", StringComparison.Ordinal));
    }

    [Fact]
    public void RevisionIsStableForDuplicateAndAdvancesForGap()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var fact = Fact(1);
        history.ObserveTerminal(fact);
        history.ObserveTerminal(fact with { });
        Assert.Equal(1, history.GetSnapshot().Revision);
        Assert.Single(history.GetSnapshot().Entries);
        history.RecordPublicationFailure();
        Assert.Equal(2, history.GetSnapshot().Revision);
        Assert.Equal(1, history.GetSnapshot().PublicationFailureCount);
    }

    [Fact]
    public void ConflictingReplayReportsGapWithoutRewritingPast()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var fact = Fact(1);
        history.ObserveTerminal(fact);
        history.ObserveTerminal(new(fact.Session, "changed", null, null, AdmissionTime, AdmissionTime,
            RecordingSinkSessionLifecycleKind.NoDecodableVideoStart));
        Assert.Same(fact, history.Find(fact.Session.SessionId));
        Assert.Equal(1, history.GetSnapshot().PublicationFailureCount);
    }

    [Fact]
    public void SnapshotCannotMutateOwnerAndEarlierSnapshotStaysUnchanged()
    {
        var history = new GenesisRecordingHistoryReadModel();
        history.ObserveTerminal(Fact(1));
        var first = history.GetSnapshot();
        Assert.Throws<NotSupportedException>(() => ((IList<RecordingSessionTerminalFact>)first.Entries).Clear());
        history.ObserveTerminal(Fact(2));
        Assert.Single(first.Entries);
        Assert.Equal(2, history.GetSnapshot().Entries.Count);
        Assert.Equal(1, first.Revision);
    }

    [Fact]
    public void NewestObservationThenSessionIdOrderingIsDeterministic()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var old = Fact(3, AdmissionTime.AddSeconds(-1));
        var a = Fact(1);
        var b = Fact(2);
        history.ObserveTerminal(b);
        history.ObserveTerminal(old);
        history.ObserveTerminal(a);
        Assert.Equal(new[] { b, a, old }, history.GetSnapshot().Entries);
    }

    [Fact]
    public void TerminalCapacityEvictsOldestAndRejectsEvictedReplayWithoutFilesOrActiveRegistry()
    {
        var history = new GenesisRecordingHistoryReadModel();
        var first = Fact(1);
        history.ObserveTerminal(first);
        for (var i = 2; i <= GenesisRecordingHistoryReadModel.MaximumTerminalEntryCount + 1; i++)
            history.ObserveTerminal(Fact(i));
        var snapshot = history.GetSnapshot();
        Assert.Equal(4096, snapshot.Entries.Count);
        Assert.Equal(1, snapshot.EvictedEntryCount);
        Assert.True(snapshot.IsTruncated);
        Assert.True(snapshot.HasGap);
        Assert.Null(history.Find(first.Session.SessionId));
        history.ObserveTerminal(first);
        Assert.Equal(snapshot.Revision, history.GetSnapshot().Revision);
        Assert.Equal(snapshot.EvictedEntryCount, history.GetSnapshot().EvictedEntryCount);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task TerminalEvictionCannotRemovePinnedRuntimeSession()
    {
        using var source = new ControlledStream([1]);
        var history = new GenesisRecordingHistoryReadModel();
        await using var runtime = Runtime(source, history);
        var active = await Start(runtime, false);
        await source.WaitingForEnd.Task;
        for (var i = 1; i <= 4097; i++) history.ObserveTerminal(Fact(i));
        Assert.Equal(active, Assert.Single(await runtime.GetActiveRecordingSessionsAsync()));
        Assert.True(history.GetSnapshot().IsTruncated);
        _ = await runtime.StopAsync(Channel);
        Assert.NotNull(history.Find(active.SessionId));
        Assert.True(File.Exists(history.Find(active.SessionId)!.WriteResult!.Path));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("FLV", null)]
    [InlineData(null, "bin")]
    public void MissingMetadataRemainsUnknownWithoutFilenameInference(string? type, string? extension)
    {
        var session = RecordingSinkSession.Manual(new(Guid.NewGuid()), Channel);
        var fact = new RecordingSessionTerminalFact(session, null, type, extension, AdmissionTime, AdmissionTime,
            RecordingSinkSessionLifecycleKind.NoDecodableVideoStart);
        Assert.Null(fact.ChannelName);
        Assert.Equal(type, fact.StreamType);
        Assert.Equal(extension, fact.RequestedFileExtension);
        Assert.Null(fact.Session.AutomaticRuleId);
        Assert.Null(fact.WriteResult);
    }

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.Active)]
    [InlineData(RecordingSinkSessionLifecycleKind.Completed)]
    [InlineData((RecordingSinkSessionLifecycleKind)99)]
    public void TerminalFactRejectsUnsupportedOrUnprovenCompletion(RecordingSinkSessionLifecycleKind kind)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecordingSessionTerminalFact(
            RecordingSinkSession.Manual(new(Guid.NewGuid()), Channel), null, null, null,
            AdmissionTime, AdmissionTime, kind));
    }

    [Fact]
    public async Task ProductionCompositionProvidesEmptyHistoryWithoutCreatingOutputOrStartingOwners()
    {
        var path = Path.Combine(_directory, "not-created");
        await using var composition = new PcpRecordingRuntimeComposition(path);
        Assert.Empty(composition.RecordingHistory.GetSnapshot().Entries);
        Assert.Empty(await composition.Recording.GetActiveRecordingSessionsAsync());
        Assert.False(Directory.Exists(_directory));
        Assert.Equal(Path.GetFullPath(path), composition.RecordingDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private RecordingRuntime Runtime(Stream source, IRecordingSessionTerminalObserver observer, TimeProvider? clock = null) =>
        new(new SourceProvider(source), new DefaultRecordingFilePathProvider(_directory),
            terminalObserver: observer, observationTimeProvider: clock);

    private static RecordingCandidate Candidate(string? type = "DATA", string? extension = "bin") =>
        new(Channel, new(name: "Original channel", streamType: type, streamExtension: extension));

    private static async Task<RecordingSinkSession> Start(RecordingRuntime runtime, bool automatic,
        string? type = "DATA", string? extension = "bin")
    {
        var candidate = Candidate(type, extension);
        if (automatic)
        {
            var rules = new RecordingRuleSet([new(RuleId, "mutable rule", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll)]);
            return (await runtime.EvaluateAutomaticAsync(candidate, rules)).State.Session!;
        }

        return (await runtime.StartManualAsync(candidate)).State.Session!;
    }

    private static RecordingSessionTerminalFact Fact(int id, DateTimeOffset? time = null) =>
        new(RecordingSinkSession.Manual(new(new Guid(id, 0, 0, new byte[8])), Channel), "channel", null, null,
            AdmissionTime, time ?? AdmissionTime, RecordingSinkSessionLifecycleKind.NoDecodableVideoStart);

    private static byte[] AvcConfigurationWithoutMedia() =>
    [
        (byte)'F', (byte)'L', (byte)'V', 1, 5, 0, 0, 0, 9, 0, 0, 0, 0,
        9, 0, 0, 22, 0, 0, 0, 0, 0, 0, 0,
        0x17, 0, 0, 0, 0,
        1, 0x64, 0, 0x1F, 0xFF, 0xE1, 0, 4, 0x67, 0x64, 0, 0x1F, 1, 0, 2, 0x68, 0xEE,
        0, 0, 0, 33,
    ];

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public bool Fail { get; set; }
        public override DateTimeOffset GetUtcNow() =>
            Fail ? throw new InvalidOperationException("clock") : Now;
    }

    private sealed class BrokenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("clock");
    }

    private sealed class SourceProvider(params Stream[] streams) : IRecordingStreamSourceProvider
    {
        private readonly Queue<Stream> _streams = new(streams);
        public ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_streams.Dequeue());
        }
    }

    private sealed class PendingSourceProvider : IRecordingStreamSourceProvider
    {
        public TaskCompletionSource Opening { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Stream> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken)
        {
            Opening.TrySetResult();
            return await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CaptureObserver(GenesisRecordingHistoryReadModel history) : IRecordingSessionTerminalObserver
    {
        public bool DeferCapture { get; set; }
        public TaskCompletionSource<RecordingSessionTerminalFact> Terminal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ObserveTerminal(RecordingSessionTerminalFact fact)
        {
            if (!DeferCapture) history.ObserveTerminal(fact);
            Terminal.TrySetResult(fact);
        }
        public void RecordPublicationFailure() => history.RecordPublicationFailure();
    }

    private sealed class FailingObserver(GenesisRecordingHistoryReadModel history) : IRecordingSessionTerminalObserver
    {
        public RecordingSessionTerminalFact? Fact { get; private set; }
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ObserveTerminal(RecordingSessionTerminalFact fact)
        {
            Fact = fact;
            Observed.TrySetResult();
            throw new InvalidOperationException("credential-secret");
        }
        public void RecordPublicationFailure() => history.RecordPublicationFailure();
    }

    private sealed class ControlledStream(byte[] payload) : MemoryStream(payload, writable: false)
    {
        public bool FailRead { get; init; }
        public bool FailDispose { get; init; }
        public bool HoldStop { get; init; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource WaitingForEnd { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseEnd { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            WaitingForEnd.TrySetResult();
            try { await ReleaseEnd.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StopObserved.TrySetResult();
                if (HoldStop) await ReleaseStop.Task;
                throw;
            }

            if (FailRead) throw new IOException("credential-secret");
            return 0;
        }
        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            if (FailDispose) throw new IOException("credential-secret");
            return base.DisposeAsync();
        }
    }
}

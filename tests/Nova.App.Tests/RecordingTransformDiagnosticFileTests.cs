using System.Text;
using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class RecordingTransformDiagnosticFileTests : IDisposable
{
    private static readonly string[] SchemaFields = ["DiagnosticSchemaVersion", "EventKind", "ActualSessionId", "TransformPhase",
        "ThrowSiteId", "SafeReason", "FaultStage", "SafeExceptionType", "TerminalKind"];
    private readonly string root = Path.Combine(Path.GetTempPath(), "Nova-safe-transform-evidence", Guid.NewGuid().ToString("N"));
    private string RunPath => Path.Combine(root, "run");

    [Fact]
    public void DisabledAndFailedActivationCreateNoArtifact()
    {
        Assert.Null(RecordingTransformDiagnosticFile.TryCreate(null));
        Assert.Null(RecordingTransformDiagnosticFile.TryCreate(" "));
        Assert.Null(RecordingTransformDiagnosticFile.FromEnvironment(() => null));
        Assert.Null(RecordingTransformDiagnosticFile.FromEnvironment(() => throw new IOException("private env")));
        Assert.Null(RecordingTransformDiagnosticFile.TryCreate("relative-run"));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task ExplicitNewRunWritesOnlyFixedSchemaAndNeverOverwrites()
    {
        var writer = Assert.IsType<RecordingTransformDiagnosticFile>(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        var session = Guid.NewGuid();
        var diagnostic = Failure(session);
        Assert.True(writer.TryObserve(in diagnostic));
        Assert.False(writer.TryObserve(in diagnostic));
        await writer.DisposeAsync();
        await writer.DisposeAsync();
        Assert.False(writer.TryObserve(in diagnostic));
        var file = Path.Combine(RunPath, RecordingTransformDiagnosticFile.FileName);
        var bytes = await File.ReadAllBytesAsync(file);
        var text = Encoding.UTF8.GetString(bytes);
        using var json = JsonDocument.Parse(text.Trim());
        Assert.Equal(SchemaFields,
            json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(session, json.RootElement.GetProperty("ActualSessionId").GetGuid());
        Assert.Equal("DecodableStartSignature", json.RootElement.GetProperty("ThrowSiteId").GetString());
        Assert.Equal("StreamTransform", json.RootElement.GetProperty("FaultStage").GetString());
        Assert.DoesNotContain(root, text, StringComparison.Ordinal);
        Assert.DoesNotContain("pcp://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Message", text, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InnerException", text, StringComparison.Ordinal);
        Assert.Null(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public void ExistingFileAndRepositoryAncestorAreRejectedWithoutArtifact()
    {
        Directory.CreateDirectory(root);
        var existing = Path.Combine(root, "existing");
        File.WriteAllText(existing, "keep");
        Assert.Null(RecordingTransformDiagnosticFile.TryCreate(existing));
        Assert.Equal("keep", File.ReadAllText(existing));
        File.WriteAllText(Path.Combine(root, ".git"), "synthetic repository marker");
        Assert.Null(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        Assert.False(Directory.Exists(RunPath));
    }

    [Fact]
    public async Task EmptySessionAndUndefinedEnumsFailClosedWithoutInventedCorrelation()
    {
        var writer = Assert.IsType<RecordingTransformDiagnosticFile>(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        var empty = Failure(Guid.Empty);
        var invalid = Failure(Guid.NewGuid()) with { ThrowSiteId = (RecordingTransformThrowSite)999 };
        Assert.False(writer.TryObserve(in empty));
        Assert.False(writer.TryObserve(in invalid));
        var unknown = new RecordingDiagnosticEvent(Guid.NewGuid(), RecordingDiagnosticEventKind.TransformFailed);
        Assert.True(writer.TryObserve(in unknown));
        await writer.DisposeAsync();
        var text = await File.ReadAllTextAsync(Path.Combine(RunPath, RecordingTransformDiagnosticFile.FileName));
        Assert.Contains("\"ThrowSiteId\":\"Unknown\"", text, StringComparison.Ordinal);
        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task SlowOutputDropsQueueOverflowAndDisposalCancelsWithoutBlockingRecorder()
    {
        var output = new BlockingOutput();
        var writer = new RecordingTransformDiagnosticFile(output);
        var first = Failure(Guid.NewGuid());
        Assert.True(writer.TryObserve(in first));
        await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var admitted = 0;
        for (var i = 0; i < RecordingTransformDiagnosticFile.QueueCapacity + 10; i++)
        {
            var item = Failure(Guid.NewGuid());
            if (writer.TryObserve(in item)) admitted++;
        }
        Assert.Equal(RecordingTransformDiagnosticFile.QueueCapacity, admitted);
        await writer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        await output.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.False(writer.TryObserve(in first));
    }

    [Fact]
    public async Task OutputFailureIsIsolatedAndDoesNotRetry()
    {
        var output = new FailingOutput();
        var writer = new RecordingTransformDiagnosticFile(output);
        var diagnostic = Failure(Guid.NewGuid());
        Assert.True(writer.TryObserve(in diagnostic));
        await output.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await writer.DisposeAsync();
        Assert.Equal(1, output.Attempts);
        Assert.False(writer.TryObserve(in diagnostic));
    }

    [Fact]
    public async Task SessionRetentionRecordCountAndFileSizeRemainBounded()
    {
        var writer = Assert.IsType<RecordingTransformDiagnosticFile>(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        var accepted = 0;
        var retained = 0;
        for (var i = 0; i < RecordingTransformDiagnosticFile.MaximumSessions + 20; i++)
        {
            var session = Guid.NewGuid();
            var admission = new RecordingDiagnosticEvent(session, RecordingDiagnosticEventKind.WriterAdmitted);
            // Queue overflow may drop an event; do not retry recording commands.
            if (!writer.TryObserve(in admission)) { await Task.Yield(); continue; }
            retained++;
            accepted++;
            var failure = Failure(session);
            if (writer.TryObserve(in failure)) accepted++;
            var terminal = failure with { EventKind = RecordingDiagnosticEventKind.Terminal, TerminalKind = RecordingSinkSessionLifecycleKind.Faulted };
            if (writer.TryObserve(in terminal)) accepted++;
            Assert.False(writer.TryObserve(in admission));
            await Task.Yield();
        }
        await writer.DisposeAsync();
        var file = Path.Combine(RunPath, RecordingTransformDiagnosticFile.FileName);
        var bytes = await File.ReadAllBytesAsync(file);
        Assert.True(retained <= RecordingTransformDiagnosticFile.MaximumSessions);
        Assert.True(accepted <= RecordingTransformDiagnosticFile.MaximumRecords);
        Assert.True(bytes.Length <= RecordingTransformDiagnosticFile.MaximumFileBytes);
        var records = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(accepted, records.Length);
        var sessions = records.Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .GroupBy(e => e.GetProperty("ActualSessionId").GetGuid()).ToArray();
        Assert.True(sessions.Length <= RecordingTransformDiagnosticFile.MaximumSessions);
        Assert.All(sessions, group => Assert.InRange(group.Count(), 1, 3));
    }

    [Fact]
    public async Task ExistingCompositionOwnsDiagnosticShutdownWithoutStartingNetwork()
    {
        var writer = Assert.IsType<RecordingTransformDiagnosticFile>(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        var composition = new PcpRecordingRuntimeComposition(Path.Combine(root, "recordings"), null, diagnostics: writer);
        Assert.Empty(await composition.Recording.GetActiveRecordingSessionsAsync());
        await composition.DisposeAsync();
        var diagnostic = Failure(Guid.NewGuid());
        Assert.False(writer.TryObserve(in diagnostic));
        Assert.False(Directory.Exists(Path.Combine(root, "recordings")));
    }

    [Fact]
    public async Task ProcessConfigurationCanExplicitlyOptInWithoutGlobalSettings()
    {
        var writer = Assert.IsType<RecordingTransformDiagnosticFile>(RecordingTransformDiagnosticFile.FromEnvironment(() => RunPath));
        await writer.DisposeAsync();
        Assert.True(File.Exists(Path.Combine(RunPath, RecordingTransformDiagnosticFile.FileName)));
    }

    [Fact]
    public async Task DistinctSessionLimitIsEnforcedAfterQueueDrains()
    {
        using var output = new SignallingOutput();
        var writer = new RecordingTransformDiagnosticFile(output);
        for (var i = 0; i < RecordingTransformDiagnosticFile.MaximumSessions; i++)
        {
            var diagnostic = Failure(Guid.NewGuid());
            Assert.True(writer.TryObserve(in diagnostic));
            await output.RecordWritten.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var overflow = Failure(Guid.NewGuid());
        Assert.False(writer.TryObserve(in overflow));
        await writer.DisposeAsync();
    }

    [Fact]
    public async Task RecordAndByteLimitsDropRatherThanExtendRetention()
    {
        using var output = new SignallingOutput();
        var writer = new RecordingTransformDiagnosticFile(output, maximumRecords: 1);
        var first = Failure(Guid.NewGuid());
        Assert.True(writer.TryObserve(in first));
        await output.RecordWritten.WaitAsync(TimeSpan.FromSeconds(5));
        var second = first with { EventKind = RecordingDiagnosticEventKind.Terminal };
        Assert.False(writer.TryObserve(in second));
        await writer.DisposeAsync();

        using var bytes = new MemoryStream();
        var limited = new RecordingTransformDiagnosticFile(bytes, maximumFileBytes: 1);
        Assert.True(limited.TryObserve(in first));
        await limited.DisposeAsync();
        Assert.Empty(bytes.ToArray());
        Assert.False(limited.TryObserve(in second));
    }

    [Fact]
    public async Task RealSinkFailureProducesSafeOutputAndUnchangedDurableHistory()
    {
        var writer = Assert.IsType<RecordingTransformDiagnosticFile>(RecordingTransformDiagnosticFile.TryCreate(RunPath));
        var history = new GenesisRecordingHistoryReadModel();
        var store = new GenesisRecordingHistoryStore(Path.Combine(root, "history.json"));
        var persistence = new GenesisRecordingHistoryPersistenceCoordinator(history, store);
        var bridge = new CaptureHistory(history);
        await using var runtime = new RecordingRuntime(new InvalidSource(), new RecordingPaths(root),
            terminalObserver: bridge, diagnosticObserver: writer);
        var channel = new ChannelId(Guid.NewGuid());
        var candidate = new RecordingCandidate(channel, new ChannelMetadataSnapshot(name: "PRIVATE-TITLE-MUST-NOT-LEAK"));
        var started = await runtime.StartManualAsync(candidate);
        await bridge.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.DisposeAsync();
        await persistence.ShutdownAsync();
        await writer.DisposeAsync();
        var fact = Assert.Single(history.GetSnapshot().Entries);
        Assert.Equal(started.State.Session!.SessionId, fact.Session.SessionId);
        Assert.Equal(RecordingSinkSessionLifecycleKind.Faulted, fact.Kind);
        Assert.Null(fact.WriteResult);
        Assert.Equal(RecordingFailureStage.StreamTransform, fact.Fault!.Stage);
        Assert.Equal("RecordingStreamTransformException", fact.Fault.ExceptionType);
        Assert.Equal(fact, Assert.Single(store.Load().Snapshot!.Entries));
        var evidence = await File.ReadAllTextAsync(Path.Combine(RunPath, RecordingTransformDiagnosticFile.FileName));
        Assert.DoesNotContain("PRIVATE-TITLE", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain(root, evidence, StringComparison.Ordinal);
        var records = evidence.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, records.Length);
        foreach (var record in records)
        {
            using var json = JsonDocument.Parse(record);
            Assert.Equal(fact.Session.SessionId.Value, json.RootElement.GetProperty("ActualSessionId").GetGuid());
        }
        var historyJson = await File.ReadAllTextAsync(Path.Combine(root, "history.json"));
        Assert.Contains("\"schemaVersion\": 2", historyJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowSite", historyJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeReason", historyJson, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(root, "*.flv*"));
    }

    private static RecordingDiagnosticEvent Failure(Guid session) => new(session, RecordingDiagnosticEventKind.TransformFailed,
        RecordingTransformPhase.DecodableStart, RecordingTransformThrowSite.DecodableStartSignature,
        RecordingTransformFailureReason.InvalidFlvHeader, RecordingFailureStage.StreamTransform,
        RecordingDiagnosticExceptionType.RecordingStreamTransformException);

    private sealed class BlockingOutput : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
    }

    private sealed class FailingOutput : MemoryStream
    {
        public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Attempts++;
            Failed.TrySetResult();
            return ValueTask.FromException(new IOException("pcp://PRIVATE-TRACKER PRIVATE-PAYLOAD PRIVATE-PATH"));
        }
    }

    private sealed class SignallingOutput : MemoryStream
    {
        public SemaphoreSlim RecordWritten { get; } = new(0);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            if (buffer.Length == 1) RecordWritten.Release();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) RecordWritten.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class InvalidSource : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Stream>(new MemoryStream(new byte[13]));
    }

    private sealed class RecordingPaths(string root) : IRecordingFilePathProvider
    {
        public string GetPath(RecordingSinkStartRequest request, RecordingSessionId sessionId) =>
            Path.Combine(root, sessionId.Value.ToString("N") + ".flv");
    }

    private sealed class CaptureHistory(GenesisRecordingHistoryReadModel history) : IRecordingSessionTerminalObserver
    {
        public TaskCompletionSource Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ObserveTerminal(RecordingSessionTerminalFact fact)
        {
            history.ObserveTerminal(fact);
            Terminal.TrySetResult();
        }
        public void RecordPublicationFailure() => history.RecordPublicationFailure();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }
}

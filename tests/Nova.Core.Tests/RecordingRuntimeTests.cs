using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingRuntimeTests : IDisposable
{
    private const int TestTimeoutMilliseconds = 5000;

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-RecordingRuntimeTests",
        Guid.NewGuid().ToString("N"));

    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public async Task Stop_UnknownChannelReturnsNoActiveSession()
    {
        await using var runtime = CreateRuntime(
            new SingleStreamSourceProvider(new MemoryStream([1])));

        var result = await runtime.StopAsync(TestChannelId);

        Assert.Equal(
            RecordingAutomationStopResultKind.NoActiveSession,
            result.Kind);
        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            result.State);
        Assert.False(runtime.TryGetState(TestChannelId, out _));
    }

    [Fact]
    public async Task EmptyRuntimeReturnsNoActiveSessions()
    {
        await using var runtime = CreateRuntime(
            new SingleStreamSourceProvider(new MemoryStream([1])));

        var sessions = await runtime.GetActiveRecordingSessionsAsync();

        Assert.Empty(sessions);
        Assert.False(runtime.TryGetState(TestChannelId, out _));
    }

    [Fact]
    public async Task RuntimeSnapshotReturnsActiveManualSessionIdentity()
    {
        var provider = new GatedSourceProvider();
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "active-session-snapshot.bin"));
        await using var runtime = new RecordingRuntime(provider, pathProvider);
        var starting = runtime.StartManualAsync(CreateCandidate()).AsTask();
        await provider.OpenStarted.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var source = new ProgressiveRecordingStream([1, 2, 3], [4, 5, 6]);
        provider.Release(source);
        var started = await starting.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await source.WaitingForSecondChunk.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var session = Assert.Single(await runtime.GetActiveRecordingSessionsAsync());

        Assert.Equal(TestChannelId, session.ChannelId);
        Assert.Equal(started.State.Session!.SessionId, session.SessionId);
        Assert.Equal(RecordingSessionOrigin.Manual, session.Origin);
        Assert.Null(session.AutomaticRuleId);

        var stopped = await runtime.StopManualAsync(
            TestChannelId,
            session.SessionId);
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Empty(await runtime.GetActiveRecordingSessionsAsync());
    }

    [Fact]
    public async Task RuntimeDisposeRacesSnapshotWithoutDeadlock()
    {
        var provider = new GatedSourceProvider();
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "dispose-snapshot-race.bin"));
        await using var runtime = new RecordingRuntime(provider, pathProvider);
        var starting = runtime.StartManualAsync(CreateCandidate()).AsTask();
        await provider.OpenStarted.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var source = new ProgressiveRecordingStream([7, 8], [9, 10]);
        provider.Release(source);
        var started = await starting.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await source.WaitingForSecondChunk.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var snapshotTask = runtime.GetActiveRecordingSessionsAsync().AsTask();
        var disposeTask = runtime.DisposeAsync().AsTask();
        await Task.WhenAll(snapshotTask, disposeTask).WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var snapshot = await snapshotTask;
        if (snapshot.Count == 1)
        {
            Assert.Equal(started.State.Session, snapshot[0]);
        }
        else
        {
            Assert.Empty(snapshot);
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await runtime.GetActiveRecordingSessionsAsync());
    }

    [Fact]
    public async Task AutomaticStartPreparationRunsOnlyForStartAndCoversSinkStart()
    {
        var provider = new GatedSourceProvider();
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "prepared-automatic.bin"));
        await using var runtime = new RecordingRuntime(provider, pathProvider);
        var candidate = CreateCandidate();
        var ruleSet = new RecordingRuleSet(
            [CreateRule(RecordingRuleAction.Record)]);
        var lease = new TestLease();
        var preparationCalls = 0;

        var unchanged = await runtime.EvaluateAutomaticAsync(
            candidate,
            new RecordingRuleSet([]),
            (_, _) =>
            {
                preparationCalls++;
                return ValueTask.FromResult<IAsyncDisposable?>(lease);
            });

        Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, unchanged.Kind);
        Assert.Equal(0, preparationCalls);
        Assert.Equal(0, provider.OpenCount);

        var starting = runtime.EvaluateAutomaticAsync(
            candidate,
            ruleSet,
            (preparedCandidate, _) =>
            {
                Assert.Equal(candidate.ChannelId, preparedCandidate.ChannelId);
                preparationCalls++;
                return ValueTask.FromResult<IAsyncDisposable?>(lease);
            }).AsTask();
        await provider.OpenStarted.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        Assert.Equal(1, preparationCalls);
        Assert.Equal(0, lease.DisposeCount);
        Assert.Equal(1, pathProvider.CallCount);

        var source = new ProgressiveRecordingStream([1, 2, 3], [4, 5, 6]);
        provider.Release(source);
        var started = await starting.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await source.FirstChunkRead.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        Assert.Equal(RecordingAutomationExecutionResultKind.Started, started.Kind);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, started.State.Session!.Origin);
        Assert.Equal(ruleSet.Rules[0].Id, started.State.Session.AutomaticRuleId);
        Assert.Equal(1, lease.DisposeCount);

        var stopped = await runtime.EvaluateAutomaticAsync(
            candidate,
            new RecordingRuleSet([]),
            (_, _) =>
            {
                preparationCalls++;
                return ValueTask.FromResult<IAsyncDisposable?>(lease);
            });

        Assert.Equal(RecordingAutomationExecutionResultKind.Stopped, stopped.Kind);
        Assert.Equal(1, preparationCalls);
        Assert.Equal(1, provider.OpenCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task AutomaticStartWithoutPreparedOwnershipFailsClosed()
    {
        var provider = new GatedSourceProvider();
        await using var runtime = CreateRuntime(provider);

        var result = await runtime.EvaluateAutomaticAsync(
            CreateCandidate(),
            new RecordingRuleSet([CreateRule(RecordingRuleAction.Record)]),
            static (_, _) => ValueTask.FromResult<IAsyncDisposable?>(null));

        Assert.Equal(RecordingAutomationExecutionResultKind.StartRejected, result.Kind);
        Assert.Same(RecordingAutomationExecutionState.Idle, result.State);
        Assert.Equal(0, provider.OpenCount);
        Assert.Same(RecordingAutomationExecutionState.Idle,
            runtime.GetState(TestChannelId));
    }

    [Fact]
    public async Task AutomaticStartPreparationCancellationDoesNotOpenRecordingSource()
    {
        var provider = new GatedSourceProvider();
        await using var runtime = CreateRuntime(provider);
        using var cancellation = new CancellationTokenSource();
        var evaluation = runtime.EvaluateAutomaticAsync(
            CreateCandidate(),
            new RecordingRuleSet([CreateRule(RecordingRuleAction.Record)]),
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new TestLease();
            },
            cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => evaluation.WaitAsync(
                TimeSpan.FromMilliseconds(TestTimeoutMilliseconds)));
        Assert.Equal(0, provider.OpenCount);
        Assert.Same(RecordingAutomationExecutionState.Idle,
            runtime.GetState(TestChannelId));
    }

    [Fact]
    public async Task StopManual_UnknownChannelReturnsNoActiveSession()
    {
        await using var runtime = CreateRuntime(
            new SingleStreamSourceProvider(new MemoryStream([1])));

        var result = await runtime.StopManualAsync(
            TestChannelId,
            new RecordingSessionId(Guid.NewGuid()));

        Assert.Equal(
            RecordingAutomationStopResultKind.NoActiveSession,
            result.Kind);
        Assert.Same(RecordingAutomationExecutionState.Idle, result.State);
        Assert.False(runtime.TryGetState(TestChannelId, out _));
    }

    [Fact]
    public async Task ManualAndAutomaticStarts_ShareOneSessionAndFinalizeFile()
    {
        var firstChunk = CreatePayload(64 * 1024, seed: 1);
        var secondChunk = CreatePayload(64 * 1024, seed: 2);
        var source = new ProgressiveRecordingStream(
            firstChunk,
            secondChunk);
        var provider = new GatedSourceProvider();
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "manual.bin"));
        await using var runtime = new RecordingRuntime(
            provider,
            pathProvider);
        var candidate = CreateCandidate();

        var manualStartTask = runtime.StartManualAsync(candidate).AsTask();
        await provider.OpenStarted.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var duplicateStartTask = runtime.StartManualAsync(candidate).AsTask();
        var automaticTask = runtime.EvaluateAutomaticAsync(
                candidate,
                new RecordingRuleSet(
                    [CreateRule(RecordingRuleAction.Record)]))
            .AsTask();

        Assert.False(manualStartTask.IsCompleted);
        Assert.False(duplicateStartTask.IsCompleted);
        Assert.False(automaticTask.IsCompleted);
        Assert.Equal(1, provider.OpenCount);

        provider.Release(source);

        var manualStart = await manualStartTask.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        var duplicateStart = await duplicateStartTask.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        var automatic = await automaticTask.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        Assert.Equal(
            RecordingManualStartResultKind.Started,
            manualStart.Kind);
        Assert.Equal(
            RecordingSessionOrigin.Manual,
            manualStart.State.Session!.Origin);
        Assert.Null(manualStart.State.Session.AutomaticRuleId);
        Assert.Equal(
            RecordingManualStartResultKind.AlreadyRunning,
            duplicateStart.Kind);
        Assert.Equal(
            RecordingAutomationExecutionResultKind.NoChange,
            automatic.Kind);
        Assert.Same(
            manualStart.State.Session,
            automatic.State.Session);
        Assert.Equal(1, provider.OpenCount);
        Assert.Equal(1, pathProvider.CallCount);

        await source.FirstChunkRead.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await source.WaitingForSecondChunk.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await WaitForPartialSizeAsync(
            pathProvider.Path,
            expectedSize: firstChunk.Length);

        source.ReleaseSecondChunk.TrySetResult();
        await source.WaitingForTail.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await WaitForPartialSizeAsync(
            pathProvider.Path,
            expectedSize: firstChunk.Length + secondChunk.Length);

        var stopped = await runtime.StopAsync(TestChannelId);

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        Assert.Same(RecordingAutomationExecutionState.Idle, stopped.State);
        Assert.Equal(
            firstChunk.Concat(secondChunk).ToArray(),
            await File.ReadAllBytesAsync(pathProvider.Path));
        Assert.Empty(GetPartialFiles(directory));
    }

    [Fact]
    public async Task StopDuringSourceStartup_CancelsManualStartWithoutCreatingFile()
    {
        var provider = new GatedSourceProvider();
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "canceled.bin"));
        await using var runtime = new RecordingRuntime(
            provider,
            pathProvider);

        var startTask = runtime.StartManualAsync(CreateCandidate()).AsTask();
        await provider.OpenStarted.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var stopTask = runtime.StopAsync(TestChannelId).AsTask();
        var start = await startTask.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        var stop = await stopTask.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        Assert.Equal(RecordingManualStartResultKind.Canceled, start.Kind);
        Assert.Same(RecordingAutomationExecutionState.Idle, start.State);
        Assert.Equal(
            RecordingAutomationStopResultKind.NoActiveSession,
            stop.Kind);
        Assert.Same(RecordingAutomationExecutionState.Idle, runtime.GetState(TestChannelId));
        Assert.Empty(Directory.Exists(directory)
            ? Directory.GetFiles(directory)
            : []);
    }

    [Fact]
    public async Task SourceFailure_LeavesManualSessionIdleAndRetryable()
    {
        await using var runtime = CreateRuntime(
            new ThrowingSourceProvider());

        await Assert.ThrowsAsync<IOException>(
            async () => await runtime.StartManualAsync(CreateCandidate()));

        Assert.Same(
            RecordingAutomationExecutionState.Idle,
            runtime.GetState(TestChannelId));
        Assert.Equal(
            RecordingAutomationStopResultKind.NoActiveSession,
            (await runtime.StopAsync(TestChannelId)).Kind);
    }

    [Fact]
    public async Task AutomaticEvaluation_ReconcilesCompletedManualSession()
    {
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "completed.bin"));
        await using var runtime = new RecordingRuntime(
            new SingleStreamSourceProvider(new MemoryStream([9, 8, 7])),
            pathProvider);

        var start = await runtime.StartManualAsync(CreateCandidate());

        Assert.Equal(RecordingManualStartResultKind.Started, start.Kind);
        var evaluated = await WaitForIdleAsync(async () =>
        {
            var result = await runtime.EvaluateAutomaticAsync(
                CreateCandidate(),
                new RecordingRuleSet([]));
            Assert.Equal(RecordingAutomationExecutionResultKind.NoChange, result.Kind);
            return result.State;
        });

        Assert.Same(RecordingAutomationExecutionState.Idle, evaluated);
        Assert.Same(RecordingAutomationExecutionState.Idle, runtime.GetState(TestChannelId));
        Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(pathProvider.Path));
    }

    [Fact]
    public async Task ReconcileState_RefreshesCompletedManualSessionWithoutAnotherEvaluation()
    {
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "reconcile-completed.bin"));
        var source = new CompletionGatedStream([6, 5, 4]);
        await using var runtime = new RecordingRuntime(
            new SingleStreamSourceProvider(source),
            pathProvider);

        var started = await runtime.StartManualAsync(CreateCandidate());
        Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);
        try
        {
            await source.DisposeStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
            // File finalization precedes source disposal and writer completion.
            // Reconciliation must preserve the active session until then.
            Assert.True(File.Exists(pathProvider.Path));
            Assert.Same(started.State, await runtime.ReconcileStateAsync(TestChannelId));
        }
        finally { source.ReleaseDispose.TrySetResult(); }

        var reconciled = await WaitForIdleAsync(() => runtime.ReconcileStateAsync(TestChannelId));

        Assert.Same(RecordingAutomationExecutionState.Idle, reconciled);
        Assert.Same(RecordingAutomationExecutionState.Idle, runtime.GetState(TestChannelId));
        Assert.Equal([6, 5, 4], await File.ReadAllBytesAsync(pathProvider.Path));
    }

    [Fact]
    public async Task ManualSourceReadFailure_IsAvailableFromRuntimeStopResult()
    {
        const string privateFailureDetail =
            "PRIVATE-RECORDING-MARKER https://example.invalid/private/path";
        var source = new FailingReadStream(privateFailureDetail);
        await using var runtime = CreateRuntime(
            new SingleStreamSourceProvider(source));

        var started = await runtime.StartManualAsync(CreateCandidate());
        Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);
        await source.ReadAttempted.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));

        var stopped = await runtime.StopAsync(TestChannelId);

        Assert.Equal(RecordingAutomationStopResultKind.Faulted, stopped.Kind);
        Assert.Equal(RecordingFailureStage.SourceRead, stopped.Fault!.Stage);
        Assert.Equal(nameof(IOException), stopped.Fault.ExceptionType);
        Assert.DoesNotContain(privateFailureDetail, stopped.Fault.ToString());
        Assert.Empty(GetPartialFiles(directory));
        Assert.False(File.Exists(Path.Combine(directory, "recording.bin")));
    }

    [Fact]
    public async Task Shutdown_StopsManualSessionAndFinalizesPartialFile()
    {
        var firstChunk = CreatePayload(64 * 1024, seed: 5);
        var source = new ProgressiveRecordingStream(firstChunk, []);
        var pathProvider = new FixedPathProvider(
            Path.Combine(directory, "shutdown.bin"));
        await using var runtime = new RecordingRuntime(
            new SingleStreamSourceProvider(source),
            pathProvider);

        var start = await runtime.StartManualAsync(CreateCandidate());
        await source.FirstChunkRead.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await source.WaitingForSecondChunk.Task.WaitAsync(
            TimeSpan.FromMilliseconds(TestTimeoutMilliseconds));
        await WaitForPartialSizeAsync(
            pathProvider.Path,
            expectedSize: firstChunk.Length);

        var report = await runtime.ShutdownAsync();

        Assert.True(report.AllInactive);
        Assert.Equal(firstChunk, await File.ReadAllBytesAsync(pathProvider.Path));
        Assert.Empty(GetPartialFiles(directory));
        Assert.Same(RecordingAutomationExecutionState.Idle, runtime.GetState(TestChannelId));
        Assert.Equal(RecordingSessionOrigin.Manual, start.State.Session!.Origin);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private RecordingRuntime CreateRuntime(
        IRecordingStreamSourceProvider sourceProvider)
    {
        return new RecordingRuntime(
            sourceProvider,
            new FixedPathProvider(
                Path.Combine(directory, "recording.bin")));
    }

    private static RecordingCandidate CreateCandidate()
    {
        return new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(
                name: "Channel",
                genre: "Music",
                bitrateKbps: 128));
    }

    private static RecordingRule CreateRule(RecordingRuleAction action)
    {
        return new RecordingRule(
            Guid.NewGuid(),
            "Recording rule",
            action,
            RecordingRuleCriteria.MatchAll);
    }

    private static byte[] CreatePayload(int length, int seed)
    {
        return Enumerable.Range(0, length)
            .Select(index => (byte)((index + seed) % 251))
            .ToArray();
    }

    private static async Task WaitForPartialSizeAsync(
        string finalPath,
        long expectedSize)
    {
        var directory = Path.GetDirectoryName(finalPath)!;
        var deadline = DateTime.UtcNow.AddMilliseconds(
            TestTimeoutMilliseconds);

        while (DateTime.UtcNow < deadline)
        {
            foreach (var partialPath in GetPartialFiles(directory))
            {
                if (new FileInfo(partialPath).Length >= expectedSize)
                {
                    return;
                }
            }

            await Task.Delay(10);
        }

        var observedFiles = Directory.Exists(directory)
            ? Directory.GetFiles(directory)
                .Select(static path =>
                    $"{System.IO.Path.GetFileName(path)} ({new FileInfo(path).Length} bytes)")
            : [];
        Assert.Fail(
            $"No partial recording reached {expectedSize} bytes at '{finalPath}'. " +
            $"Observed files: {string.Join(", ", observedFiles)}.");
    }

    private static async Task<RecordingAutomationExecutionState> WaitForIdleAsync(
        Func<ValueTask<RecordingAutomationExecutionState>> observe)
    {
        using var timeout = new CancellationTokenSource(TestTimeoutMilliseconds);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var state = await observe();
            if (ReferenceEquals(RecordingAutomationExecutionState.Idle, state))
            {
                return state;
            }

            // Wait for the actual terminal state, not elapsed wall-clock time
            // or the earlier appearance of a finalized file.
            await Task.Yield();
        }
    }

    private static string[] GetPartialFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory)
                .Where(static path => path.EndsWith(
                    ".partial",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];
    }

    private sealed class CompletionGatedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            await ReleaseDispose.Task;
            await base.DisposeAsync();
        }
    }

    private sealed class FixedPathProvider(string path) : IRecordingFilePathProvider
    {
        public int CallCount { get; private set; }

        public string Path => path;

        public string GetPath(
            RecordingSinkStartRequest request,
            RecordingSessionId sessionId)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(sessionId);
            CallCount++;
            return path;
        }
    }

    private sealed class SingleStreamSourceProvider(Stream source) :
        IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(source);
        }
    }

    private sealed class TestLease : IAsyncDisposable
    {
        private int disposeCount;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GatedSourceProvider : IRecordingStreamSourceProvider
    {
        private readonly TaskCompletionSource<Stream> source = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int openCount;

        public TaskCompletionSource OpenStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int OpenCount => Volatile.Read(ref openCount);

        public void Release(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            Assert.True(source.TrySetResult(stream));
        }

        public async ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Interlocked.Increment(ref openCount);
            OpenStarted.TrySetResult();
            return await source.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingSourceProvider : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException<Stream>(
                new IOException("Synthetic source failure."));
        }
    }

    private sealed class FailingReadStream(string failureMessage) : MemoryStream
    {
        public TaskCompletionSource ReadAttempted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadAttempted.TrySetResult();
            return ValueTask.FromException<int>(
                new IOException(failureMessage));
        }
    }

    private sealed class ProgressiveRecordingStream(
        byte[] firstChunk,
        byte[] secondChunk) : Stream
    {
        private int readIndex;

        public TaskCompletionSource FirstChunkRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WaitingForSecondChunk { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecondChunk { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WaitingForTail { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (readIndex == 0)
            {
                readIndex++;
                FirstChunkRead.TrySetResult();
                return CopyChunk(firstChunk, buffer);
            }

            if (readIndex == 1)
            {
                readIndex++;
                WaitingForSecondChunk.TrySetResult();
                await ReleaseSecondChunk.Task.WaitAsync(cancellationToken);
                return CopyChunk(secondChunk, buffer);
            }

            WaitingForTail.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return ReadAsync(
                    buffer.AsMemory(offset, count),
                    cancellationToken)
                .AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private static int CopyChunk(byte[] source, Memory<byte> destination)
        {
            var count = Math.Min(source.Length, destination.Length);
            source.AsMemory(0, count).CopyTo(destination);
            return count;
        }
    }
}

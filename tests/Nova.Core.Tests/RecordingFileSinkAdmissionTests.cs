using System.Collections;
using System.Reflection;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingFileSinkAdmissionTests
{
    [Fact]
    public async Task RuntimeForwardsAdmissionAndShutdownDrainsRealSourceCleanup()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Nova-StopAdmission", Guid.NewGuid().ToString("N"), "runtime.bin");
        var source = new ControlledSource(false, false, false);
        var runtime = new RecordingRuntime(new SourceProvider(source), new PathProvider(path));
        var channel = new ChannelId(Guid.NewGuid());
        var rule = new RecordingRule(Guid.NewGuid(), "test", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll);
        var start = await runtime.EvaluateAutomaticAsync(
            new(channel, new ChannelMetadataSnapshot(name: "test")), new([rule]));
        await source.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var receipt = (await runtime.RequestAutomaticStopAsync(channel, start.State.Session!.SessionId)).Admission!;
        Assert.Equal(rule.Id, receipt.ActiveRuleId);
        Assert.True(runtime.TryGetAutomaticStopAdmission(channel, receipt.SessionId, out var recovered));
        Assert.Same(receipt, recovered);
        await source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = runtime.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        source.ReleaseRead.TrySetResult();
        await source.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(closing.IsCompleted);
        Assert.False(receipt.Completion.IsCompleted);
        source.ReleaseDispose.TrySetResult();
        Assert.Equal(RecordingAutomationStopResultKind.Stopped, (await receipt.Completion).Kind);
        await closing;
        Assert.Equal(1, source.Disposals);
        Directory.Delete(System.IO.Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public async Task ExactReceiptIsRecoverableAndDuplicateDoesNotNotifyTwice()
    {
        await using var test = await Harness.CreateAsync();
        var result = test.Sink.RequestStopAdmission(new(test.Session));
        var receipt = Assert.IsType<RecordingSinkStopAdmission>(result.Admission);
        Assert.Same(test.Session, receipt.Session);
        Assert.True(test.Sink.TryGetStopAdmission(test.Session, out var recovered));
        Assert.Same(receipt, recovered);
        var duplicate = test.Sink.RequestStopAdmission(new(test.Session));
        Assert.Equal(RecordingSinkStopNotAdmittedReason.AlreadyStoppingByOtherOperation, duplicate.Reason);
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, test.Source.Notifications);
        test.Release();
        Assert.Equal(RecordingSinkStopResultKind.Stopped, (await receipt.Completion).Kind);
        Assert.True(test.Sink.TryGetStopAdmission(test.Session, out recovered));
        Assert.Same(receipt, recovered);
        Assert.False(test.Sink.TryGetStatus(test.Session, out _));
        Assert.Equal(1, test.Source.Disposals);
    }

    [Fact]
    public async Task CallerWaitCancellationCannotCancelCanonicalCompletion()
    {
        await using var test = await Harness.CreateAsync();
        using var caller = new CancellationTokenSource();
        var receipt = test.Sink.RequestStopAdmission(new(test.Session), caller.Token).Admission!;
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receipt.Completion.WaitAsync(caller.Token));
        Assert.False(receipt.Completion.IsCompleted);
        Assert.True(test.Sink.TryGetStopAdmission(test.Session, out var recovered));
        Assert.Same(receipt, recovered);
        test.Release();
        Assert.Equal(RecordingSinkStopResultKind.Stopped, (await receipt.Completion).Kind);
    }

    [Fact]
    public async Task PreCancelledSinkAdmissionHasNoEffect()
    {
        await using var test = await Harness.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = test.Sink.RequestStopAdmission(new(test.Session), cancelled.Token);
        Assert.Equal(RecordingSinkStopNotAdmittedReason.CancelledBeforeAdmission, result.Reason);
        Assert.Null(result.Admission);
        Assert.False(test.Source.StopObserved.Task.IsCompleted);
        Assert.False(test.Sink.TryGetStopAdmission(test.Session, out _));
    }

    [Fact]
    public async Task TerminalPublicationBeforeAdmissionCreatesNoReceipt()
    {
        await using var test = await Harness.CreateAsync();
        test.Release();
        await test.ExactWriter.WaitAsync(TimeSpan.FromSeconds(5));
        var result = test.Sink.RequestStopAdmission(new(test.Session));
        Assert.Equal(RecordingSinkStopNotAdmittedReason.AlreadyTerminal, result.Reason);
        Assert.Null(result.Admission);
        Assert.Equal(0, test.Source.Notifications);
    }

    [Fact]
    public async Task TerminalPublicationAndAdmissionUseSameGate()
    {
        await using var test = await Harness.CreateAsync();
        // Natural EOF has reached source disposal, but terminal cleanup has
        // not committed. Admission must win, not infer EOF from writer status.
        test.Source.ReleaseRead.TrySetResult();
        await test.Source.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var receipt = test.Sink.RequestStopAdmission(new(test.Session)).Admission!;
        Assert.False(receipt.Completion.IsCompleted);
        test.Source.ReleaseDispose.TrySetResult();
        Assert.Equal(RecordingSinkStopResultKind.Stopped, (await receipt.Completion).Kind);
        Assert.True(test.Sink.TryGetStopAdmission(test.Session, out var recovered));
        Assert.Same(receipt, recovered);
    }

    [Fact]
    public async Task CompletionKnownRequiresCleanupNotOnlyWriterExit()
    {
        await using var test = await Harness.CreateAsync();
        var receipt = test.Sink.RequestStopAdmission(new(test.Session)).Admission!;
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Source.ReleaseRead.TrySetResult();
        await test.Source.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(receipt.Completion.IsCompleted);
        Assert.True(test.Sink.TryGetStatus(test.Session, out _));
        test.Source.ReleaseDispose.TrySetResult();
        await receipt.Completion;
        Assert.Equal(1, test.Source.Disposals);
        Assert.False(test.Sink.TryGetStatus(test.Session, out _));
    }

    [Fact]
    public async Task CallbackExceptionIsObservedAndDoesNotEraseAdmission()
    {
        await using var test = await Harness.CreateAsync(throwCallback: true);
        var receipt = test.Sink.RequestStopAdmission(new(test.Session)).Admission!;
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(test.Sink.TryGetStopAdmission(test.Session, out var recovered));
        Assert.Same(receipt, recovered);
        Assert.False(receipt.Completion.IsCompleted);
        test.Release();
        var terminal = await receipt.Completion;
        Assert.Equal(RecordingSinkStopResultKind.Faulted, terminal.Kind);
        Assert.NotNull(terminal.Fault);
        Assert.DoesNotContain("private", terminal.Fault.ToString());
        Assert.Equal(1, test.Source.Disposals);
    }

    [Fact]
    public async Task SourceExceptionAfterAdmissionResolvesTypedFaultAfterCleanup()
    {
        await using var test = await Harness.CreateAsync(throwRead: true);
        var receipt = test.Sink.RequestStopAdmission(new(test.Session)).Admission!;
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Release();
        var terminal = await receipt.Completion;
        Assert.Equal(RecordingSinkStopResultKind.Faulted, terminal.Kind);
        Assert.Equal(RecordingFailureStage.SourceRead, terminal.Fault!.Stage);
        Assert.Equal(1, test.Source.Disposals);
    }

    [Fact]
    public async Task AdmittedNoDecodableVideoStartRemovesPartialArtifact()
    {
        await using var test = await Harness.CreateAsync(flv: true);
        var receipt = test.Sink.RequestStopAdmission(new(test.Session)).Admission!;
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Release();
        Assert.Equal(RecordingSinkStopResultKind.NoDecodableVideoStart, (await receipt.Completion).Kind);
        Assert.False(File.Exists(test.Path));
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(test.Path)!));
    }

    [Fact]
    public async Task LegacyStopWaitCancellationRetryRemainsEquivalent()
    {
        await using var test = await Harness.CreateAsync();
        using var caller = new CancellationTokenSource();
        var legacy = test.Sink.StopAsync(new(test.Session), caller.Token).AsTask();
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => legacy);
        Assert.True(test.Sink.TryGetStatus(test.Session, out _));
        Assert.Equal(RecordingSinkStopNotAdmittedReason.AlreadyStoppingByOtherOperation,
            test.Sink.RequestStopAdmission(new(test.Session)).Reason);
        Assert.False(test.Sink.TryGetStopAdmission(test.Session, out _));
        test.Release();
        Assert.Equal(RecordingSinkStopResultKind.Stopped,
            (await test.Sink.StopAsync(new(test.Session), CancellationToken.None)).Kind);
        Assert.Equal(RecordingSinkStopResultKind.AlreadyStopped,
            (await test.Sink.StopAsync(new(test.Session), CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task LegacyStopJoiningAdmissionUsesExactOwnedCompletion()
    {
        await using var test = await Harness.CreateAsync();
        var receipt = test.Sink.RequestStopAdmission(new(test.Session)).Admission!;
        var legacy = test.Sink.StopAsync(new(test.Session), CancellationToken.None).AsTask();
        Assert.False(legacy.IsCompleted);
        await test.Source.StopObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Release();
        Assert.Same(await receipt.Completion, await legacy);
        Assert.Equal(1, test.Source.Notifications);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required RecordingFileSink Sink { get; init; }
        public required RecordingSinkSession Session { get; init; }
        public required ControlledSource Source { get; init; }
        public required string Path { get; init; }
        public required Task ExactWriter { get; init; }

        public static async Task<Harness> CreateAsync(
            bool throwCallback = false, bool throwRead = false, bool flv = false)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "Nova-StopAdmission", Guid.NewGuid().ToString("N"), flv ? "recording.flv" : "recording.bin");
            var source = new ControlledSource(throwCallback, throwRead, flv);
            var sink = new RecordingFileSink(new SourceProvider(source), new PathProvider(path));
            var started = await sink.StartAsync(new RecordingSinkStartRequest(
                new RecordingCandidate(new ChannelId(Guid.NewGuid()), new ChannelMetadataSnapshot(name: "test")),
                Guid.NewGuid()), CancellationToken.None);
            await source.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Observe the exact task to order natural-terminal tests without
            // sleeps or production instrumentation.
            var dictionary = (IDictionary)typeof(RecordingFileSink).GetField(
                "activeByChannel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sink)!;
            var active = dictionary[started.Session!.ChannelId]!;
            var writer = (Task)active.GetType().GetField(
                "writeTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(active)!;
            return new Harness { Sink = sink, Session = started.Session, Source = source, Path = path, ExactWriter = writer };
        }

        public void Release()
        {
            Source.ReleaseRead.TrySetResult();
            Source.ReleaseDispose.TrySetResult();
        }

        public async ValueTask DisposeAsync()
        {
            Release();
            await Sink.StopAsync(new(Session), CancellationToken.None);
            var directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class SourceProvider(Stream source) : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(source);
    }

    private sealed class PathProvider(string path) : IRecordingFilePathProvider
    {
        public string GetPath(RecordingSinkStartRequest request, RecordingSessionId sessionId) => path;
    }

    private sealed class ControlledSource(bool throwCallback, bool throwRead, bool flv) : Stream
    {
        private int prefixOffset;
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Notifications;
        public int Disposals;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Valid AVC configuration, no IDR. Header-only input can use the
            // existing passthrough path and does not prove NoDecodable.
            byte[] prefix = flv
                ? [
                    0x46, 0x4c, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0,
                    9, 0, 0, 22, 0, 0, 0, 0, 0, 0, 0,
                    0x17, 0, 0, 0, 0, 1, 0x64, 0, 0x1f, 0xff, 0xe1,
                    0, 4, 0x67, 0x64, 0, 0x1f, 1, 0, 2, 0x68, 0xee,
                    0, 0, 0, 33,
                ]
                : [1];
            if (prefixOffset < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - prefixOffset);
                prefix.AsMemory(prefixOffset, count).CopyTo(buffer);
                prefixOffset += count;
                return count;
            }
            using var registration = cancellationToken.Register(() =>
            {
                Interlocked.Increment(ref Notifications);
                StopObserved.TrySetResult();
                if (throwCallback)
                {
                    throw new InvalidOperationException("private callback");
                }
            });
            ReadEntered.TrySetResult();
            await ReleaseRead.Task.ConfigureAwait(false);
            if (throwRead)
            {
                throw new IOException("private source");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }

        public override async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await ReleaseDispose.Task.ConfigureAwait(false);
            Interlocked.Increment(ref Disposals);
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

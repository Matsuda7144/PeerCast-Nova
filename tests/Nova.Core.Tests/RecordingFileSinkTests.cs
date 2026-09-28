using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingFileSinkTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-RecordingFileSinkTests",
        Guid.NewGuid().ToString("N"));

    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public async Task Start_EndOfStream_ProducesCompletedSessionAndExactFile()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        var path = GetPath("recording.bin");
        var sink = CreateSink(
            _ => new MemoryStream(payload),
            path);
        var request = CreateStartRequest();

        var started = await sink.StartAsync(
            request,
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStartResultKind.Started,
            started.Kind);

        var session = Assert.IsType<RecordingSinkSession>(
            started.Session);

        await WaitForStatusAsync(
            sink,
            session,
            RecordingFileSinkSessionStatusKind.Completed);

        Assert.True(sink.TryGetStatus(
            session,
            out var status));
        Assert.NotNull(status);
        Assert.Equal(payload.Length, status.WriteResult!.BytesWritten);
        Assert.Null(status.Fault);
        Assert.Equal(
            RecordingFileWriteCompletionKind.EndOfStream,
            status.WriteResult.CompletionKind);
        Assert.Equal(
            payload,
            await File.ReadAllBytesAsync(path));

        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStopResultKind.Stopped,
            stopped.Kind);
        Assert.False(sink.TryGetStatus(
            session,
            out _));
    }

    [Fact]
    public async Task Start_DuplicateChannel_ReturnsExistingSession()
    {
        using var source = new BlockingReadStream();
        var provider = new QueueSourceProvider([source]);
        var sink = new RecordingFileSink(
            provider,
            new ConstantPathProvider(GetPath("recording.bin")));
        var request = CreateStartRequest();

        var first = await sink.StartAsync(
            request,
            CancellationToken.None);
        var second = await sink.StartAsync(
            request,
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStartResultKind.Started,
            first.Kind);
        Assert.Equal(
            RecordingSinkStartResultKind.AlreadyRunning,
            second.Kind);
        Assert.Equal(
            first.Session!.SessionId,
            second.Session!.SessionId);
        Assert.Equal(1, provider.OpenCount);

        _ = await sink.StopAsync(
            new RecordingSinkStopRequest(first.Session),
            CancellationToken.None);
    }

    [Fact]
    public async Task Stop_DuringSourceOpen_WaitsAndDisposesLateSource()
    {
        using var source = new TrackingReadableStream();
        var provider = new DelayedIgnoringCancellationSourceProvider(source);
        var path = GetPath("stopped-before-write.bin");
        var sink = new RecordingFileSink(
            provider,
            new ConstantPathProvider(path));
        var request = CreateStartRequest();

        var starting = sink.StartAsync(
                request,
                CancellationToken.None)
            .AsTask();

        await provider.OpenStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var duplicate = await sink.StartAsync(
            request,
            CancellationToken.None);
        var session = Assert.IsType<RecordingSinkSession>(
            duplicate.Session);

        Assert.Equal(
            RecordingSinkStartResultKind.AlreadyRunning,
            duplicate.Kind);

        var stopping = sink.StopAsync(
                new RecordingSinkStopRequest(session),
                CancellationToken.None)
            .AsTask();

        try
        {
            await provider.StopCancellationObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.False(stopping.IsCompleted);
        }
        finally
        {
            provider.ReleaseOpen.TrySetResult();
        }

        var startResult = await starting.WaitAsync(
            TimeSpan.FromSeconds(5));
        var stopResult = await stopping.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(
            RecordingSinkStartResultKind.Rejected,
            startResult.Kind);
        Assert.Equal(
            RecordingSinkStopResultKind.Stopped,
            stopResult.Kind);
        Assert.True(source.Disposed);
        Assert.False(File.Exists(path));
        Assert.False(sink.TryGetStatus(
            session,
            out _));
    }

    [Fact]
    public async Task Start_NewChannel_WhenActiveSessionLimitReached_FailsClosed()
    {
        using var firstSource = new BlockingReadStream();
        using var secondSource = new BlockingReadStream();
        var provider = new QueueSourceProvider(
            [
                firstSource,
                secondSource,
            ]);
        var sink = new RecordingFileSink(
            provider,
            new ConstantPathProvider(GetPath("recording.bin")),
            maximumActiveSessionCount: 1);

        var first = await sink.StartAsync(
            CreateStartRequest(),
            CancellationToken.None);

        var secondRequest = new RecordingSinkStartRequest(
            new RecordingCandidate(
                new ChannelId(
                    Guid.ParseExact(
                        "642F3B7B36444205A3345FEA2D8CB2AA",
                        "N")),
                new ChannelMetadataSnapshot(
                    name: "Second Channel",
                    genre: "Games",
                    bitrateKbps: 2000)),
            Guid.ParseExact(
                "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                "N"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sink.StartAsync(
                secondRequest,
                CancellationToken.None));

        Assert.Equal(1, provider.OpenCount);

        _ = await sink.StopAsync(
            new RecordingSinkStopRequest(first.Session!),
            CancellationToken.None);

        var retry = await sink.StartAsync(
            secondRequest,
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStartResultKind.Started,
            retry.Kind);

        _ = await sink.StopAsync(
            new RecordingSinkStopRequest(retry.Session!),
            CancellationToken.None);
    }

    [Fact]
    public void Constructor_InvalidMaximumActiveSessionCount_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RecordingFileSink(
                new QueueSourceProvider([]),
                new ConstantPathProvider(GetPath("recording.bin")),
                maximumActiveSessionCount: 0));
    }

    [Fact]
    public async Task Stop_CancelsLiveWriteAndFinalizesBytesAlreadyReceived()
    {
        byte[] payload = [10, 20, 30, 40];
        using var source = new PayloadThenBlockStream(payload);
        var path = GetPath("live.bin");
        var sink = CreateSink(
            _ => source,
            path);

        var started = await sink.StartAsync(
            CreateStartRequest(),
            CancellationToken.None);
        var session = started.Session!;

        await source.WaitingForStop.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStopResultKind.Stopped,
            stopped.Kind);
        Assert.Null(stopped.Fault);
        Assert.Equal(
            payload,
            await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Stop_WaitCancellation_PreservesSessionForRetry()
    {
        using var source = new SlowStopStream([1, 2, 3]);
        var sink = CreateSink(
            _ => source,
            GetPath("slow-stop.bin"));

        var started = await sink.StartAsync(
            CreateStartRequest(),
            CancellationToken.None);
        var session = started.Session!;

        await source.WaitingForStop.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        using var waitCancellation = new CancellationTokenSource();

        var stopTask = sink.StopAsync(
                new RecordingSinkStopRequest(session),
                waitCancellation.Token)
            .AsTask();

        await source.StopObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        waitCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await stopTask);

        Assert.True(sink.TryGetStatus(
            session,
            out var status));
        Assert.NotNull(status);

        source.ReleaseStop.TrySetResult();

        var retried = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStopResultKind.Stopped,
            retried.Kind);
        Assert.False(sink.TryGetStatus(
            session,
            out _));
    }

    [Fact]
    public async Task Stop_UnknownSession_ReturnsAlreadyStopped()
    {
        var sink = CreateSink(
            _ => new MemoryStream([1]),
            GetPath("recording.bin"));

        var unknown = new RecordingSinkSession(
            new RecordingSessionId(Guid.NewGuid()),
            TestChannelId,
            Guid.NewGuid());

        var result = await sink.StopAsync(
            new RecordingSinkStopRequest(unknown),
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStopResultKind.AlreadyStopped,
            result.Kind);
    }

    [Fact]
    public async Task SourceFailure_IsReportedFaultedAndStopCleansSession()
    {
        const string privateFailureDetail =
            "PRIVATE-RECORDING-MARKER https://example.invalid/private/path";
        var sink = CreateSink(
            _ => new FailingReadStream(privateFailureDetail),
            GetPath("faulted.bin"));

        var started = await sink.StartAsync(
            CreateStartRequest(),
            CancellationToken.None);
        var session = started.Session!;

        await WaitForStatusAsync(
            sink,
            session,
            RecordingFileSinkSessionStatusKind.Faulted);

        Assert.True(sink.TryGetStatus(session, out var fileStatus));
        Assert.Equal(RecordingFailureStage.SourceRead, fileStatus!.Fault!.Stage);
        Assert.Equal(nameof(IOException), fileStatus.Fault.ExceptionType);
        Assert.DoesNotContain(
            privateFailureDetail,
            fileStatus.Fault.ToString());

        Assert.True(sink.TryGetSessionStatus(
            session,
            out var lifecycle));
        Assert.Equal(
            RecordingSinkSessionLifecycleKind.Faulted,
            lifecycle!.Kind);
        Assert.Equal(fileStatus.Fault, lifecycle.Fault);

        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStopResultKind.Faulted,
            stopped.Kind);
        Assert.Equal(fileStatus.Fault, stopped.Fault);
        Assert.False(sink.TryGetStatus(
            session,
            out _));
    }

    [Fact]
    public async Task FinalizeFailure_IsReportedWithoutExposingPath()
    {
        var path = GetPath("existing.bin");
        Directory.CreateDirectory(directory);
        byte[] existingBytes = [9, 8, 7];
        await File.WriteAllBytesAsync(path, existingBytes);
        var sink = CreateSink(
            _ => new MemoryStream([1, 2, 3]),
            path);

        var started = await sink.StartAsync(
            CreateStartRequest(),
            CancellationToken.None);
        var session = started.Session!;

        await WaitForStatusAsync(
            sink,
            session,
            RecordingFileSinkSessionStatusKind.Faulted);

        Assert.True(sink.TryGetStatus(session, out var status));
        Assert.Equal(RecordingFailureStage.FileFinalize, status!.Fault!.Stage);
        Assert.Equal(nameof(IOException), status.Fault.ExceptionType);
        Assert.DoesNotContain(path, status.Fault.ToString());

        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(RecordingSinkStopResultKind.Faulted, stopped.Kind);
        Assert.Equal(status.Fault, stopped.Fault);
        Assert.Equal(existingBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Start_SourceProviderFailure_ReleasesChannelReservation()
    {
        var provider = new FailingThenSuccessfulSourceProvider();
        var sink = new RecordingFileSink(
            provider,
            new ConstantPathProvider(GetPath("recording.bin")));
        var request = CreateStartRequest();

        await Assert.ThrowsAsync<IOException>(
            async () => await sink.StartAsync(
                request,
                CancellationToken.None));

        var retry = await sink.StartAsync(
            request,
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStartResultKind.Started,
            retry.Kind);

        _ = await sink.StopAsync(
            new RecordingSinkStopRequest(retry.Session!),
            CancellationToken.None);
    }

    [Fact]
    public async Task Start_UnreadableSource_IsRejectedAndDisposed()
    {
        var source = new UnreadableStream();
        var sink = CreateSink(
            _ => source,
            GetPath("recording.bin"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sink.StartAsync(
                CreateStartRequest(),
                CancellationToken.None));

        Assert.True(source.Disposed);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(
                directory,
                recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static RecordingFileSink CreateSink(
        Func<RecordingSinkStartRequest, Stream> sourceFactory,
        string path)
    {
        return new RecordingFileSink(
            new DelegateSourceProvider(sourceFactory),
            new ConstantPathProvider(path));
    }

    private string GetPath(string fileName)
    {
        return Path.Combine(
            directory,
            fileName);
    }

    private static RecordingSinkStartRequest CreateStartRequest()
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                TestChannelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    genre: "Games",
                    bitrateKbps: 2000)),
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));
    }

    private static async Task WaitForStatusAsync(
        RecordingFileSink sink,
        RecordingSinkSession session,
        RecordingFileSinkSessionStatusKind expected)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();

            if (sink.TryGetStatus(
                    session,
                    out var status) &&
                status!.Kind == expected)
            {
                return;
            }

            await Task.Delay(
                10,
                timeout.Token);
        }
    }

    private sealed class DelegateSourceProvider :
        IRecordingStreamSourceProvider
    {
        private readonly Func<RecordingSinkStartRequest, Stream> factory;

        public DelegateSourceProvider(
            Func<RecordingSinkStartRequest, Stream> factory)
        {
            this.factory = factory;
        }

        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(factory(request));
        }
    }

    private sealed class QueueSourceProvider :
        IRecordingStreamSourceProvider
    {
        private readonly Queue<Stream> streams;

        public QueueSourceProvider(IEnumerable<Stream> streams)
        {
            this.streams = new Queue<Stream>(streams);
        }

        public int OpenCount { get; private set; }

        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            return ValueTask.FromResult(streams.Dequeue());
        }
    }

    private sealed class DelayedIgnoringCancellationSourceProvider(
        Stream source) : IRecordingStreamSourceProvider
    {
        public TaskCompletionSource OpenStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseOpen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            using var registration = cancellationToken.Register(
                () => StopCancellationObserved.TrySetResult());
            OpenStarted.TrySetResult();

            await ReleaseOpen.Task.ConfigureAwait(false);
            return source;
        }
    }

    private sealed class FailingThenSuccessfulSourceProvider :
        IRecordingStreamSourceProvider
    {
        private int count;

        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Increment(ref count) == 1)
            {
                return ValueTask.FromException<Stream>(
                    new IOException("Synthetic open failure."));
            }

            return ValueTask.FromResult<Stream>(
                new MemoryStream([1, 2, 3]));
        }
    }

    private sealed class ConstantPathProvider :
        IRecordingFilePathProvider
    {
        private readonly string path;

        public ConstantPathProvider(string path)
        {
            this.path = path;
        }

        public string GetPath(
            RecordingSinkStartRequest request,
            RecordingSessionId sessionId)
        {
            return path;
        }
    }

    private sealed class TrackingReadableStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class PayloadThenBlockStream : Stream
    {
        private readonly byte[] payload;
        private bool delivered;

        public PayloadThenBlockStream(byte[] payload)
        {
            this.payload = payload;
        }

        public TaskCompletionSource WaitingForStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!delivered)
            {
                delivered = true;
                payload.CopyTo(buffer);
                return payload.Length;
            }

            WaitingForStop.TrySetResult();

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class SlowStopStream : Stream
    {
        private readonly byte[] payload;
        private bool delivered;

        public SlowStopStream(byte[] payload)
        {
            this.payload = payload;
        }

        public TaskCompletionSource WaitingForStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!delivered)
            {
                delivered = true;
                payload.CopyTo(buffer);
                return payload.Length;
            }

            WaitingForStop.TrySetResult();

            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                StopObserved.TrySetResult();
                await ReleaseStop.Task;
                throw;
            }

            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class FailingReadStream : Stream
    {
        private readonly string failureMessage;

        public FailingReadStream(
            string failureMessage = "Synthetic recording source failure.")
        {
            this.failureMessage = failureMessage;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromException<int>(
                new IOException(failureMessage));
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class UnreadableStream : Stream
    {
        public bool Disposed { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
        }
    }
}

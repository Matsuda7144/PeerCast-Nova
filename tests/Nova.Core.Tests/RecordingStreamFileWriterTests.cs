using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingStreamFileWriterTests : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "PeerCast-Nova-RecordingStreamFileWriterTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Write_EndOfStream_FinalizesExactBytes()
    {
        var path = GetPath("recording.bin");
        byte[] payload = [1, 2, 3, 4, 5];

        await using var source = new MemoryStream(payload);

        var result = await RecordingStreamFileWriter.WriteAsync(
            source,
            path,
            CancellationToken.None);

        Assert.Equal(
            RecordingFileWriteCompletionKind.EndOfStream,
            result.CompletionKind);
        Assert.Equal(payload.Length, result.BytesWritten);
        Assert.Equal(
            System.IO.Path.GetFullPath(path),
            result.Path);
        Assert.Equal(
            payload,
            await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Write_StopCancellation_FinalizesBytesAlreadyReceived()
    {
        var path = GetPath("stopped.bin");
        byte[] payload = [10, 20, 30, 40];
        using var source = new StopAfterFirstReadStream(payload);
        using var stopping = new CancellationTokenSource();

        var writeTask = RecordingStreamFileWriter.WriteAsync(
                source,
                path,
                stopping.Token)
            .AsTask();

        await source.WaitingForStop.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        stopping.Cancel();

        var result = await writeTask;

        Assert.Equal(
            RecordingFileWriteCompletionKind.Stopped,
            result.CompletionKind);
        Assert.Equal(payload.Length, result.BytesWritten);
        Assert.Equal(
            payload,
            await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Write_PreCancelled_DoesNotCreateDirectoryOrFile()
    {
        var path = GetPath("cancelled.bin");
        using var source = new MemoryStream([1, 2, 3]);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await RecordingStreamFileWriter.WriteAsync(
                source,
                path,
                stopping.Token));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Write_SourceFailure_DeletesPartialAndLeavesNoFinalFile()
    {
        var path = GetPath("failed.bin");
        using var source = new FailingAfterFirstReadStream(
            [1, 2, 3]);

        await Assert.ThrowsAsync<IOException>(
            async () => await RecordingStreamFileWriter.WriteAsync(
                source,
                path,
                CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Write_ExistingFinalFile_IsPreserved()
    {
        var path = GetPath("existing.bin");
        Directory.CreateDirectory(directory);
        byte[] existing = [9, 9, 9];
        await File.WriteAllBytesAsync(path, existing);

        await using var source = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<IOException>(
            async () => await RecordingStreamFileWriter.WriteAsync(
                source,
                path,
                CancellationToken.None));

        Assert.Equal(
            existing,
            await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Write_UnreadableSource_IsRejectedBeforeFilesystemMutation()
    {
        var path = GetPath("unreadable.bin");
        await using var source = new WriteOnlyStream();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await RecordingStreamFileWriter.WriteAsync(
                source,
                path,
                CancellationToken.None));

        Assert.False(Directory.Exists(directory));
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

    private string GetPath(string fileName)
    {
        return System.IO.Path.Combine(
            directory,
            fileName);
    }

    private sealed class StopAfterFirstReadStream : Stream
    {
        private readonly byte[] payload;
        private bool delivered;

        public StopAfterFirstReadStream(byte[] payload)
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

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
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

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FailingAfterFirstReadStream : Stream
    {
        private readonly byte[] payload;
        private bool delivered;

        public FailingAfterFirstReadStream(byte[] payload)
        {
            this.payload = payload;
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

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!delivered)
            {
                delivered = true;
                payload.CopyTo(buffer);
                return ValueTask.FromResult(payload.Length);
            }

            return ValueTask.FromException<int>(
                new IOException("Synthetic source failure."));
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class WriteOnlyStream : Stream
    {
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

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
        }
    }
}

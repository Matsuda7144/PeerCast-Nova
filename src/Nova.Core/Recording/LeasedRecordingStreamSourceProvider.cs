namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Adapts channel-owner reader leases to the Stream contract consumed by
/// <see cref="RecordingFileSink"/>.
/// </summary>
public sealed class LeasedRecordingStreamSourceProvider :
    IRecordingStreamSourceProvider
{
    private readonly IRecordingStreamSourceLeaseFactory leaseFactory;

    public LeasedRecordingStreamSourceProvider(
        IRecordingStreamSourceLeaseFactory leaseFactory)
    {
        ArgumentNullException.ThrowIfNull(leaseFactory);
        this.leaseFactory = leaseFactory;
    }

    /// <summary>
    /// The lease factory used by this provider. Runtime composition can use
    /// this identity to verify that the recording consumer is bound to the
    /// shared owner registry.
    /// </summary>
    public IRecordingStreamSourceLeaseFactory LeaseFactory => leaseFactory;

    public async ValueTask<Stream> OpenReadAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var lease = await leaseFactory.AcquireAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            throw new InvalidOperationException(
                "No recording stream source is available for the requested channel.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readStream = lease.ReadStream;
            ArgumentNullException.ThrowIfNull(readStream);

            if (!readStream.CanRead)
            {
                throw new InvalidOperationException(
                    "The recording stream source lease returned an unreadable stream.");
            }

            return new LeaseOwnedReadStream(
                readStream,
                lease);
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class LeaseOwnedReadStream : Stream
    {
        private readonly Stream inner;
        private readonly IRecordingStreamSourceLease lease;
        private int disposed;

        public LeaseOwnedReadStream(
            Stream inner,
            IRecordingStreamSourceLease lease)
        {
            this.inner = inner;
            this.lease = lease;
        }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

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
            return inner.Read(
                buffer,
                offset,
                count);
        }

        public override int Read(Span<byte> buffer)
        {
            return inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(
                buffer,
                cancellationToken);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing &&
                Interlocked.Exchange(ref disposed, 1) == 0)
            {
                lease.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}

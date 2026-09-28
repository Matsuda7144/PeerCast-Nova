using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Adapts an injected recording-compatible PCP packet pump into the readable
/// Stream contract consumed by <see cref="RecordingFileSink"/>.
/// </summary>
public sealed class PcpRecordingStreamSourceProvider :
    IRecordingStreamSourceProvider
{
    private readonly IPcpRecordingPacketPump packetPump;
    private readonly PcpRecordingPacketStreamBridgeOptions bridgeOptions;

    public PcpRecordingStreamSourceProvider(
        IPcpRecordingPacketPump packetPump,
        PcpRecordingPacketStreamBridgeOptions? bridgeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(packetPump);

        var effectiveOptions = bridgeOptions is null
            ? new PcpRecordingPacketStreamBridgeOptions()
            : bridgeOptions with { };
        effectiveOptions.Validate();

        this.packetPump = packetPump;
        this.bridgeOptions = effectiveOptions;
    }

    public ValueTask<Stream> OpenReadAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var bridge = new PcpRecordingPacketStreamBridge(
            request.Candidate.ChannelId,
            bridgeOptions);

        Stream readStream;

        try
        {
            readStream = bridge.OpenReadStream();
        }
        catch
        {
            bridge.DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            throw;
        }

        var lifetime = new CancellationTokenSource();
        var pumpTask = RunPumpAsync(
            packetPump,
            request,
            bridge,
            lifetime.Token);

        Stream ownedStream = new PumpOwnedReadStream(
            readStream,
            bridge,
            lifetime,
            pumpTask);

        return ValueTask.FromResult(
            ownedStream);
    }

    private static async Task RunPumpAsync(
        IPcpRecordingPacketPump packetPump,
        RecordingSinkStartRequest request,
        PcpRecordingPacketStreamBridge bridge,
        CancellationToken cancellationToken)
    {
        try
        {
            await packetPump.RunAsync(
                request,
                bridge,
                cancellationToken).ConfigureAwait(false);

            await TryCompleteBridgeAsync(
                bridge,
                exception: null).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await TryCompleteBridgeAsync(
                bridge,
                exception: null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TryCompleteBridgeAsync(
                bridge,
                exception).ConfigureAwait(false);
        }
    }

    private static async ValueTask TryCompleteBridgeAsync(
        PcpRecordingPacketStreamBridge bridge,
        Exception? exception)
    {
        try
        {
            await bridge.CompleteAsync(
                exception).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The owning Stream already completed bridge teardown.
        }
    }

    private sealed class PumpOwnedReadStream : Stream
    {
        private readonly Stream inner;
        private readonly PcpRecordingPacketStreamBridge bridge;
        private readonly CancellationTokenSource lifetime;
        private readonly Task pumpTask;
        private int disposed;

        public PumpOwnedReadStream(
            Stream inner,
            PcpRecordingPacketStreamBridge bridge,
            CancellationTokenSource lifetime,
            Task pumpTask)
        {
            this.inner = inner;
            this.bridge = bridge;
            this.lifetime = lifetime;
            this.pumpTask = pumpTask;
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

        public override int Read(
            Span<byte> buffer)
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

        public override void SetLength(
            long value)
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

        protected override void Dispose(
            bool disposing)
        {
            if (disposing &&
                Interlocked.Exchange(
                    ref disposed,
                    1) == 0)
            {
                lifetime.Cancel();

                try
                {
                    inner.Dispose();
                    pumpTask.GetAwaiter().GetResult();
                }
                finally
                {
                    bridge.DisposeAsync()
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                    lifetime.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(
                    ref disposed,
                    1) == 0)
            {
                lifetime.Cancel();

                try
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                    await pumpTask.ConfigureAwait(false);
                }
                finally
                {
                    await bridge.DisposeAsync().ConfigureAwait(false);
                    lifetime.Dispose();
                }
            }

            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}

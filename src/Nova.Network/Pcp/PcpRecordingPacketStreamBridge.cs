using System.Buffers;
using System.IO.Pipelines;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Copies callback-scoped PCP packet payloads into a bounded Pipe and exposes
/// the copied byte sequence as one readable Stream for recording.
/// </summary>
public sealed class PcpRecordingPacketStreamBridge :
    IPcpChannelPacketConsumer,
    IAsyncDisposable
{
    private readonly Pipe pipe;
    private readonly SemaphoreSlim writerGate = new(1, 1);
    private long expectedSequence;
    private bool hasConsumedPacket;
    private int streamOpened;
    private int writerCompleted;
    private int disposed;

    public PcpRecordingPacketStreamBridge(
        ChannelId channelId,
        PcpRecordingPacketStreamBridgeOptions? options = null,
        long initialPacketSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentOutOfRangeException.ThrowIfNegative(
            initialPacketSequence);

        var effectiveOptions = options is null
            ? new PcpRecordingPacketStreamBridgeOptions()
            : options with { };
        effectiveOptions.Validate();

        ChannelId = channelId;
        expectedSequence = initialPacketSequence;

        pipe = new Pipe(
            new PipeOptions(
                pool: MemoryPool<byte>.Shared,
                readerScheduler: PipeScheduler.ThreadPool,
                writerScheduler: PipeScheduler.ThreadPool,
                pauseWriterThreshold:
                    effectiveOptions.PauseWriterThresholdBytes,
                resumeWriterThreshold:
                    effectiveOptions.ResumeWriterThresholdBytes,
                minimumSegmentSize:
                    effectiveOptions.MinimumSegmentSizeBytes,
                useSynchronizationContext: false));
    }

    public ChannelId ChannelId { get; }

    public long ExpectedSequence =>
        Interlocked.Read(ref expectedSequence);

    public Stream OpenReadStream()
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(
                ref streamOpened,
                1,
                0) != 0)
        {
            throw new InvalidOperationException(
                "The PCP recording bridge read stream can only be opened once.");
        }

        return pipe.Reader.AsStream(
            leaveOpen: false);
    }

    public async ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        await writerGate.WaitAsync(
            cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            ThrowIfWriterCompleted();

            if (packet.ChannelId != ChannelId)
            {
                throw new InvalidDataException(
                    "A PCP recording bridge cannot accept a packet from another channel.");
            }

            if (hasConsumedPacket &&
                (packet.Sequence != expectedSequence || packet.Discontinuity is not null))
            {
                // すでに受信済みの録画データを保護するため、途中の連番ギャップや不連続では
                // 例外で破棄せず正常 EOF としてストリームを閉じる。
                await CompleteWriterUnderGateAsync(
                    exception: null).ConfigureAwait(false);
                return;
            }

            ValidatePacket(packet);

            if (!packet.Payload.IsEmpty)
            {
                var flush = await pipe.Writer.WriteAsync(
                    packet.Payload,
                    cancellationToken).ConfigureAwait(false);

                if (flush.IsCanceled)
                {
                    throw new OperationCanceledException(
                        cancellationToken);
                }

                if (flush.IsCompleted)
                {
                    throw new IOException(
                        "The recording bridge reader completed before the PCP producer.");
                }
            }

            hasConsumedPacket = true;
            expectedSequence = checked(
                expectedSequence + 1);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await CompleteWriterUnderGateAsync(
                exception).ConfigureAwait(false);
            throw;
        }
        finally
        {
            writerGate.Release();
        }
    }

    public async ValueTask CompleteAsync(
        Exception? exception = null)
    {
        ThrowIfDisposed();

        await writerGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await CompleteWriterUnderGateAsync(
                exception).ConfigureAwait(false);
        }
        finally
        {
            writerGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref disposed,
                1) != 0)
        {
            return;
        }

        await writerGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await CompleteWriterUnderGateAsync(
                exception: null).ConfigureAwait(false);

            try
            {
                await pipe.Reader.CompleteAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // A read Stream may already have completed the PipeReader.
            }
        }
        finally
        {
            writerGate.Release();
            writerGate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void ValidatePacket(
        PcpChannelPacket packet)
    {
        if (packet.ChannelId != ChannelId)
        {
            throw new InvalidDataException(
                "A PCP recording bridge cannot accept a packet from another channel.");
        }

        if (packet.Sequence != expectedSequence)
        {
            throw new InvalidDataException(
                $"Expected PCP packet sequence {expectedSequence} but observed {packet.Sequence}.");
        }

        if (packet.Discontinuity is not null)
        {
            throw new InvalidDataException(
                "A PCP stream discontinuity cannot be appended to the current recording file.");
        }
    }

    private async ValueTask CompleteWriterUnderGateAsync(
        Exception? exception)
    {
        if (Interlocked.Exchange(
                ref writerCompleted,
                1) != 0)
        {
            return;
        }

        await pipe.Writer.CompleteAsync(
            exception).ConfigureAwait(false);
    }

    private void ThrowIfWriterCompleted()
    {
        if (Volatile.Read(ref writerCompleted) != 0)
        {
            throw new InvalidOperationException(
                "The PCP recording bridge has already completed its writer.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
    }
}

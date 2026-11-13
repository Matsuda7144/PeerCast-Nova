using System.IO.Pipelines;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Reads a shared post-handshake PCP Pipe one top-level Atom at a time. Each
/// owned packet is released before the following Atom is decoded, and the
/// packet consumer is awaited to propagate backpressure upstream.
/// </summary>
public sealed class PcpChannelStreamReader
{
    private readonly PipeReader _reader;
    private readonly PcpChannelStateAdapter _stateAdapter;
    private readonly IPcpChannelPacketConsumer _packetConsumer;
    private readonly PcpAtomDecoder _decoder;
    private readonly PcpChannelStreamOptions _options;
    private readonly TimeSpan _readIdleTimeout;
    private readonly bool _completeReader;
    private readonly long _initialPacketSequence;
    private readonly Action? _channelMessageObserved;
    private readonly Action<PcpRelayHostCandidate>? _hostObserved;
    private int _started;

    public PcpChannelStreamReader(
        PipeReader reader,
        PcpChannelStateAdapter stateAdapter,
        IPcpChannelPacketConsumer packetConsumer,
        PcpChannelStreamOptions? options = null,
        bool completeReader = true,
        long initialPacketSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stateAdapter);
        ArgumentNullException.ThrowIfNull(packetConsumer);

        if (initialPacketSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialPacketSequence),
                initialPacketSequence,
                "The initial packet sequence cannot be negative.");
        }

        var effectiveOptions = options is null
            ? new PcpChannelStreamOptions()
            : options with { };
        effectiveOptions.Validate();

        _reader = reader;
        _stateAdapter = stateAdapter;
        _packetConsumer = packetConsumer;
        _decoder = new PcpAtomDecoder(
            effectiveOptions.CreateProtocolLimits());
        _options = effectiveOptions;
        _readIdleTimeout = effectiveOptions.ReadIdleTimeout;
        _completeReader = completeReader;
        _initialPacketSequence = initialPacketSequence;
    }

    internal PcpChannelStreamReader(
        PipeReader reader,
        PcpChannelStateAdapter stateAdapter,
        IPcpChannelPacketConsumer packetConsumer,
        PcpChannelStreamOptions options,
        bool completeReader,
        long initialPacketSequence,
        Action channelMessageObserved,
        Action<PcpRelayHostCandidate>? hostObserved = null)
        : this(
            reader,
            stateAdapter,
            packetConsumer,
            options,
            completeReader,
            initialPacketSequence)
    {
        ArgumentNullException.ThrowIfNull(channelMessageObserved);
        _channelMessageObserved = channelMessageObserved;
        _hostObserved = hostObserved;
    }

    public async Task<PcpChannelStreamResult> ReadToEndAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException(
                "A PCP channel stream reader instance can only be run once.");
        }

        Exception? failure = null;
        long channelMessageCount = 0;
        long packetCount = 0;
        long payloadByteCount = 0;
        var sequence = _initialPacketSequence;
        var sink = new PcpChannelStreamSink(_options, hostObserved: _hostObserved);

        try
        {
            while (true)
            {
                var readResult = await ReadWithIdleTimeoutAsync(
                    cancellationToken).ConfigureAwait(false);
                var buffer = readResult.Buffer;
                var advanced = false;

                try
                {
                    if (readResult.IsCanceled)
                    {
                        _reader.AdvanceTo(buffer.Start, buffer.End);
                        advanced = true;
                        throw new OperationCanceledException(cancellationToken);
                    }

                    var decodeResult = _decoder.Decode(
                        buffer,
                        readResult.IsCompleted,
                        sink,
                        stopAfterTopLevelAtom: true);

                    _reader.AdvanceTo(
                        decodeResult.Consumed,
                        decodeResult.Examined);
                    advanced = true;

                    if (decodeResult.Status == PcpDecodeStatus.ProtocolError)
                    {
                        throw new PcpProtocolException(
                            decodeResult.Error,
                            decodeResult.ErrorOffset);
                    }

                    if (decodeResult.Status == PcpDecodeStatus.Completed)
                    {
                        return new PcpChannelStreamResult(
                            PcpChannelStreamEndReason.ConnectionEnded,
                            channelMessageCount,
                            packetCount,
                            payloadByteCount);
                    }

                    if (decodeResult.Status ==
                        PcpDecodeStatus.TopLevelAtomCompleted)
                    {
                        var frame = sink.TakeCompletedFrame();

                        if (frame is not null)
                        {
                            using (frame)
                            {
                                var applied = _stateAdapter.Apply(
                                    frame.Control);
                                channelMessageCount = checked(
                                    channelMessageCount + 1);
                                _channelMessageObserved?.Invoke();

                                if (applied.Packet is not null)
                                {
                                    var packet = new PcpChannelPacket(
                                        applied.Snapshot.ChannelId,
                                        applied.Snapshot.SessionId,
                                        sequence,
                                        applied.Packet.Type,
                                        applied.Packet.Position,
                                        applied.Discontinuity is null
                                            ? applied.Snapshot.StreamPosition
                                            : null,
                                        frame.Payload,
                                        applied.Discontinuity,
                                        continuation: applied.Packet.Continuation);

                                    try
                                    {
                                        await _packetConsumer.ConsumeAsync(
                                            packet,
                                            cancellationToken).ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException)
                                        when (cancellationToken.IsCancellationRequested)
                                    {
                                        throw;
                                    }
                                    catch (Exception exception)
                                    {
                                        throw new PcpChannelConsumerException(
                                            "The PCP channel packet consumer failed.",
                                            exception);
                                    }

                                    sequence = checked(sequence + 1);
                                    packetCount = checked(packetCount + 1);
                                    payloadByteCount = checked(
                                        payloadByteCount + frame.Payload.Length);
                                }
                            }
                        }

                        if (sink.TakeQuit() is { } quit)
                        {
                            return new PcpChannelStreamResult(
                                PcpChannelStreamEndReason.QuitReceived,
                                channelMessageCount,
                                packetCount,
                                payloadByteCount)
                            { Quit = quit };
                        }

                        continue;
                    }

                    if (readResult.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "The PCP decoder requested more data after the pipe completed.");
                    }
                }
                finally
                {
                    if (!advanced)
                    {
                        _reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            sink.Dispose();

            if (_completeReader)
            {
                try
                {
                    await _reader.CompleteAsync(failure).ConfigureAwait(false);
                }
                catch when (failure is not null)
                {
                    // Preserve the primary framing, consumer, or cancellation failure.
                }
            }
        }
    }

    private async ValueTask<ReadResult> ReadWithIdleTimeoutAsync(
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(
            _readIdleTimeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            return await _reader.ReadAsync(
                linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The PCP channel stream was idle for {_readIdleTimeout}.",
                exception);
        }
    }
}

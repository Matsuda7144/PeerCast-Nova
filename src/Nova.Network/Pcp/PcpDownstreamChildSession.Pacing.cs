namespace PeerCast.Nova.Network.Pcp;

public sealed partial class PcpDownstreamChildSession : IPcpRelayPacingBackpressure
{
    private readonly PcpDownstreamPacer? _pacing;
    private int _pacingLimited;
    private readonly PcpPacingWaitState _pacingWait = new();
    bool IPcpRelayPacingBackpressure.WaitingForPacing => _pacingWait.Waiting;

    private async ValueTask SendPacedAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
    {
        var pacing = _pacing!;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _operationToken);
        using var deadline = new CancellationTokenSource(PcpDownstreamPacer.PacketDeadline, pacing.Clock);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, deadline.Token);
        var remainingWriteTime = _options.WriteTimeout;
        try
        {
            await _handshakeReady.Task.WaitAsync(_options.WriteTimeout, pacing.Clock, operation.Token).ConfigureAwait(false);
            var prefixLength = PcpChannelPacketWireWriter.WritePrefix(_packetPrefix, _channelId.Value, packet, _options.MaxPacketBytes);
            await WriteMediaAsync(_packetPrefix.AsMemory(0, prefixLength)).ConfigureAwait(false);
            await WriteMediaAsync(packet.Payload).ConfigureAwait(false);
            Volatile.Write(ref _packetWriteIncomplete, 0);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !operation.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _pacingLimited, 1);
            throw new PcpPacingLimitException();
        }
        catch (PcpPacingLimitException)
        {
            Interlocked.Exchange(ref _pacingLimited, 1);
            throw;
        }
        catch (TimeoutException)
        {
            Interlocked.Exchange(ref _writeTimedOut, 1);
            throw;
        }
        catch (Exception) when (!operation.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _writeFailed, 1);
            throw;
        }

        async ValueTask WriteMediaAsync(ReadOnlyMemory<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                var chunk = bytes[..Math.Min(bytes.Length, pacing.BurstBytes)];
                await pacing.AcquireMediaAsync(chunk.Length, _pacingWait, wait.Token).ConfigureAwait(false);
                // Granted credit is conservative: cancellation before the write
                // discards it; failed/unknown partial writes never refund it.
                operation.Token.ThrowIfCancellationRequested();
                if (remainingWriteTime <= TimeSpan.Zero) throw new TimeoutException();
                using var writeTimeout = new CancellationTokenSource(remainingWriteTime, pacing.Clock);
                using var write = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, writeTimeout.Token);
                var started = pacing.Clock.GetTimestamp();
                Volatile.Write(ref _packetWriteIncomplete, 1);
                try { await _packetWriter.WriteAsync(chunk, write.Token).ConfigureAwait(false); }
                catch (OperationCanceledException exception) when (writeTimeout.IsCancellationRequested && !operation.IsCancellationRequested)
                { throw new TimeoutException("The bounded downstream network write timed out.", exception); }
                finally { remainingWriteTime -= pacing.Clock.GetElapsedTime(started); }
                bytes = bytes[chunk.Length..];
            }
        }
    }
}

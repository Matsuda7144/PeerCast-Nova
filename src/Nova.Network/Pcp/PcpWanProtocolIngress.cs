using System.Buffers;
using System.IO.Pipelines;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>Peeks HTTP, consumes only a validated PCP preamble. Always keeps the caller's single PipeReader.</summary>
internal static class PcpWanProtocolIngress
{
    internal static async Task<bool> IsDirectPcpAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            var consumed = buffer.Start;
            var examined = buffer.End;
            try
            {
                if (result.IsCanceled) throw new OperationCanceledException(cancellationToken);
                var prefix = buffer.Slice(0, Math.Min(buffer.Length, PcpPreamble.Size)).ToArray();
                var count = Math.Min(prefix.Length, 4);
                var direct = prefix.AsSpan(0, count).SequenceEqual("pcp\n"u8[..count]);
                var http = prefix.AsSpan(0, count).SequenceEqual("GET "u8[..count]);
                if (!direct && !http) throw new InvalidDataException("Unknown publishing ingress protocol.");
                if (http && prefix.Length >= 4) { examined = buffer.Start; return false; }
                if (direct && prefix.Length == PcpPreamble.Size)
                {
                    if (!PcpPreamble.TryRead(prefix, new(), out _, out _)) throw new InvalidDataException("Invalid PCP v1 preamble.");
                    consumed = buffer.GetPosition(PcpPreamble.Size); examined = consumed;
                    return true;
                }
                if (result.IsCompleted) throw new EndOfStreamException();
            }
            finally { reader.AdvanceTo(consumed, examined); }
        }
    }

    internal static async ValueTask<PcpDownstreamChildEndReason> RunPingAsync(PcpAtomPipeMessageReader reader,
        Guid relaySid, PcpWanIngressPolicy.Lease lease, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> write,
        CancellationToken lifetime)
    {
        if (!lease.TryBeginPing()) return PcpDownstreamChildEndReason.AdmissionClosed;
        using var deadline = new CancellationTokenSource(lease.Remaining(lease.PingTimeout), lease.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, deadline.Token);
        var atom = await reader.ReadNextAsync(linked.Token).ConfigureAwait(false);
        if (atom.Name == PcpKnownAtomNames.Quit)
        {
            if (atom.IsParent) throw new InvalidDataException("Invalid ping QUIT.");
            PcpWireValueCodec.DecodeUInt32(atom.Content.Span);
            return PcpDownstreamChildEndReason.QuitReceived;
        }
        var helo = PcpHeloCodec.ParseHelo(atom);
        if (helo.SessionId == Guid.Empty) throw new InvalidDataException("Ping SID is required.");
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, PcpHeloCodec.CreateOleh("PeerCastNova", relaySid));
        await write(writer.WrittenMemory, linked.Token).ConfigureAwait(false);
        lease.Handshaken(pingResponse: true);
        return PcpDownstreamChildEndReason.PingCompleted;
    }
}

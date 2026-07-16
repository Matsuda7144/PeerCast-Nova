using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp;

public readonly record struct PcpParentStarted(
    PcpAtomHeader Header,
    int Depth,
    long StartOffset);

/// <summary>
/// A leaf atom exposed while the underlying PipeReader buffer is still valid.
/// The payload must not be retained after the sink callback returns.
/// Call <see cref="CopyPayload"/> when ownership beyond the callback is required.
/// </summary>
public readonly record struct PcpLeafAtom(
    PcpAtomHeader Header,
    int Depth,
    long StartOffset,
    ReadOnlySequence<byte> Payload)
{
    public byte[] CopyPayload()
    {
        return Payload.ToArray();
    }
}

public readonly record struct PcpParentEnded(
    PcpAtomName Name,
    int Depth,
    long StartOffset,
    long EndOffset);

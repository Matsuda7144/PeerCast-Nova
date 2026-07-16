using System.Buffers;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpOwnedChannelFrame : IDisposable
{
    private IMemoryOwner<byte>? _owner;

    public PcpOwnedChannelFrame(
        PcpChannelControlMessage control,
        IMemoryOwner<byte>? owner,
        ReadOnlyMemory<byte> payload)
    {
        Control = control;
        _owner = owner;
        Payload = payload;
    }

    public PcpChannelControlMessage Control { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _owner, null)?.Dispose();
    }
}

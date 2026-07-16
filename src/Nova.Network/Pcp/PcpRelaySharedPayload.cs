using System.Buffers;

namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpRelaySharedPayload : IDisposable
{
    private IMemoryOwner<byte>? _owner;
    private int _referenceCount = 1;

    private PcpRelaySharedPayload(
        IMemoryOwner<byte>? owner,
        ReadOnlyMemory<byte> memory)
    {
        _owner = owner;
        Memory = memory;
    }

    public ReadOnlyMemory<byte> Memory { get; }

    public static PcpRelaySharedPayload CopyFrom(ReadOnlyMemory<byte> source)
    {
        if (source.IsEmpty)
        {
            return new PcpRelaySharedPayload(
                owner: null,
                ReadOnlyMemory<byte>.Empty);
        }

        var owner = MemoryPool<byte>.Shared.Rent(source.Length);

        try
        {
            source.CopyTo(owner.Memory);
            return new PcpRelaySharedPayload(
                owner,
                owner.Memory[..source.Length]);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    public void AddReference()
    {
        while (true)
        {
            var referenceCount = Volatile.Read(ref _referenceCount);
            ObjectDisposedException.ThrowIf(referenceCount <= 0, this);

            if (Interlocked.CompareExchange(
                    ref _referenceCount,
                    checked(referenceCount + 1),
                    referenceCount) == referenceCount)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        var referenceCount = Interlocked.Decrement(ref _referenceCount);

        if (referenceCount == 0)
        {
            Interlocked.Exchange(ref _owner, null)?.Dispose();
        }
        ObjectDisposedException.ThrowIf(referenceCount < 0, this);
    }
}

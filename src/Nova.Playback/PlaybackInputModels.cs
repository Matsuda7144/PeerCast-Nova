using System.Buffers;

namespace PeerCast.Nova.Playback;

[Flags]
public enum PlaybackDiscontinuityKind
{
    None = 0,
    Source = 1,
    LatencySkip = 2,
}

public enum PlaybackInputState
{
    Accepting,
    Draining,
    Completed,
    Stopped,
}

public enum PlaybackInputEndReason
{
    SourceCompleted,
    OwnerStopped,
}

/// <summary>
/// Fixed-field, content-free status for the bounded player input.
/// Outstanding counts refer to payload leases currently owned by the reader.
/// </summary>
public sealed record PlaybackInputStatusSnapshot
{
    internal PlaybackInputStatusSnapshot(
        PlaybackInputState state,
        PlaybackInputEndReason? endReason,
        int pendingPacketCount,
        long pendingByteCount,
        int outstandingPacketCount,
        long outstandingByteCount,
        long receivedPacketCount,
        long receivedByteCount,
        long dequeuedPacketCount,
        long dequeuedByteCount,
        long skippedPacketCount,
        long skippedByteCount,
        long discontinuityPacketCount)
    {
        State = state;
        EndReason = endReason;
        PendingPacketCount = pendingPacketCount;
        PendingByteCount = pendingByteCount;
        OutstandingPacketCount = outstandingPacketCount;
        OutstandingByteCount = outstandingByteCount;
        ReceivedPacketCount = receivedPacketCount;
        ReceivedByteCount = receivedByteCount;
        DequeuedPacketCount = dequeuedPacketCount;
        DequeuedByteCount = dequeuedByteCount;
        SkippedPacketCount = skippedPacketCount;
        SkippedByteCount = skippedByteCount;
        DiscontinuityPacketCount = discontinuityPacketCount;
    }

    public PlaybackInputState State { get; }

    public PlaybackInputEndReason? EndReason { get; }

    public int PendingPacketCount { get; }

    public long PendingByteCount { get; }

    public int OutstandingPacketCount { get; }

    public long OutstandingByteCount { get; }

    public long ReceivedPacketCount { get; }

    public long ReceivedByteCount { get; }

    public long DequeuedPacketCount { get; }

    public long DequeuedByteCount { get; }

    public long SkippedPacketCount { get; }

    public long SkippedByteCount { get; }

    public long DiscontinuityPacketCount { get; }
}

/// <summary>
/// One player-input packet with explicit payload ownership. The payload remains
/// valid until this lease is disposed and must not be retained afterward.
/// </summary>
public sealed class PlaybackPacketLease : IDisposable
{
    private readonly int _payloadLength;
    private IMemoryOwner<byte>? _owner;
    private ReadOnlyMemory<byte> _payload;
    private Action<int>? _released;
    private int _disposed;

    internal PlaybackPacketLease(
        long sequence,
        long? streamPosition,
        PlaybackDiscontinuityKind discontinuity,
        IMemoryOwner<byte>? owner,
        ReadOnlyMemory<byte> payload,
        Action<int> released)
    {
        Sequence = sequence;
        StreamPosition = streamPosition;
        Discontinuity = discontinuity;
        _owner = owner;
        _payload = payload;
        _payloadLength = payload.Length;
        _released = released;
    }

    public long Sequence { get; }

    public long? StreamPosition { get; }

    public PlaybackDiscontinuityKind Discontinuity { get; }

    public ReadOnlyMemory<byte> Payload
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            return _payload;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _payload = ReadOnlyMemory<byte>.Empty;

        try
        {
            Interlocked.Exchange(ref _owner, null)?.Dispose();
        }
        finally
        {
            Interlocked.Exchange(ref _released, null)?.Invoke(_payloadLength);
        }
    }
}

internal sealed class PlaybackQueuedPacket : IDisposable
{
    private IMemoryOwner<byte>? _owner;
    private ReadOnlyMemory<byte> _payload;
    private bool _transferred;

    private PlaybackQueuedPacket(
        long sequence,
        long? streamPosition,
        PlaybackDiscontinuityKind discontinuity,
        IMemoryOwner<byte>? owner,
        ReadOnlyMemory<byte> payload)
    {
        Sequence = sequence;
        StreamPosition = streamPosition;
        Discontinuity = discontinuity;
        _owner = owner;
        _payload = payload;
    }

    public long Sequence { get; }

    public long? StreamPosition { get; }

    public PlaybackDiscontinuityKind Discontinuity { get; private set; }

    public int PayloadLength => _payload.Length;

    public static PlaybackQueuedPacket CopyFrom(
        long sequence,
        long? streamPosition,
        PlaybackDiscontinuityKind discontinuity,
        ReadOnlyMemory<byte> source)
    {
        if (source.IsEmpty)
        {
            return new PlaybackQueuedPacket(
                sequence,
                streamPosition,
                discontinuity,
                owner: null,
                ReadOnlyMemory<byte>.Empty);
        }

        var owner = MemoryPool<byte>.Shared.Rent(source.Length);

        try
        {
            source.CopyTo(owner.Memory);
            return new PlaybackQueuedPacket(
                sequence,
                streamPosition,
                discontinuity,
                owner,
                owner.Memory[..source.Length]);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    public void MarkDiscontinuity(PlaybackDiscontinuityKind discontinuity)
    {
        Discontinuity |= discontinuity;
    }

    public PlaybackPacketLease TakeLease(Action<int> released)
    {
        ObjectDisposedException.ThrowIf(_transferred, this);
        _transferred = true;

        var lease = new PlaybackPacketLease(
            Sequence,
            StreamPosition,
            Discontinuity,
            _owner,
            _payload,
            released);
        _owner = null;
        _payload = ReadOnlyMemory<byte>.Empty;
        return lease;
    }

    public void Dispose()
    {
        if (_transferred)
        {
            return;
        }

        _transferred = true;
        _payload = ReadOnlyMemory<byte>.Empty;
        Interlocked.Exchange(ref _owner, null)?.Dispose();
    }
}

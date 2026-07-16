using System.Buffers;

namespace PeerCast.Nova.Playback;

internal sealed class PlaybackDecoderBootstrapBuffer : IDisposable
{
    private byte[]? _buffer;
    private int _length;
    private bool _hasStart;
    private long _startSequence;
    private long? _startStreamPosition;
    private PlaybackDiscontinuityKind _startDiscontinuity;

    public PlaybackDecoderBootstrapBuffer(int capacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Length => _length;

    public int Append(PlaybackPacketLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(_buffer is null, this);

        var payload = lease.Payload;
        var copyLength = Math.Min(payload.Length, Capacity - _length);

        if (copyLength == 0)
        {
            return 0;
        }

        if (!_hasStart)
        {
            _hasStart = true;
            _startSequence = lease.Sequence;
            _startStreamPosition = lease.StreamPosition;
            _startDiscontinuity = lease.Discontinuity;
        }

        payload.Span[..copyLength].CopyTo(
            _buffer.AsSpan(_length, copyLength));
        _length += copyLength;
        return copyLength;
    }

    public PlaybackEncodedInput CreateInput()
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);

        if (!_hasStart || _length == 0)
        {
            throw new InvalidOperationException(
                "The decoder bootstrap is empty.");
        }

        return new PlaybackEncodedInput(
            PlaybackEncodedInputKind.Bootstrap,
            _startSequence,
            _startStreamPosition,
            _startDiscontinuity,
            _buffer.AsMemory(0, _length));
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);

        if (_length > 0)
        {
            _buffer.AsSpan(0, _length).Clear();
        }

        _length = 0;
        _hasStart = false;
        _startSequence = 0;
        _startStreamPosition = null;
        _startDiscontinuity = PlaybackDiscontinuityKind.None;
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);

        if (buffer is null)
        {
            return;
        }

        _length = 0;
        _hasStart = false;
        _startSequence = 0;
        _startStreamPosition = null;
        _startDiscontinuity = PlaybackDiscontinuityKind.None;
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
    }
}

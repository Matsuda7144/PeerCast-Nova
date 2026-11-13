using System.Buffers;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Bridges synchronous framing callbacks to one finite owned packet. The
/// async reader takes and disposes that packet before decoding another
/// top-level Atom.
/// </summary>
internal sealed class PcpChannelStreamSink : IPcpAtomSink, IDisposable
{
    private readonly int _maxPacketBytes;
    private readonly MemoryPool<byte> _memoryPool;
    private readonly PcpChannelControlSink _controlSink;
    private readonly List<PcpAtomName> _parents = [];
    private readonly PcpRelayHostAtomSink? _hosts;

    private IMemoryOwner<byte>? _payloadOwner;
    private int _payloadLength;
    private bool _hasPayload;
    private PcpQuitInfo? _quit;
    private PcpOwnedChannelFrame? _completedFrame;
    private int _disposed;

    public PcpChannelStreamSink(
        PcpChannelStreamOptions options,
        MemoryPool<byte>? memoryPool = null,
        Action<PcpRelayHostCandidate>? hostObserved = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _maxPacketBytes = options.MaxPacketBytes;
        _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        _hosts = hostObserved is null ? null : new PcpRelayHostAtomSink(hostObserved);
        _controlSink = new PcpChannelControlSink(
            CompleteControlMessage,
            options.CreateControlLimits());
    }

    public void OnParentStarted(PcpParentStarted atom)
    {
        ThrowIfDisposed();
        _controlSink.OnParentStarted(atom);
        _hosts?.OnParentStarted(atom);
        _parents.Add(atom.Header.Name);

        if (atom.Depth == 0 && atom.Header.Name == PcpKnownAtomNames.Quit)
        {
            throw new InvalidDataException("Expected a PCP QUIT leaf atom.");
        }
    }

    public void OnLeafAtom(PcpLeafAtom atom)
    {
        ThrowIfDisposed();
        _controlSink.OnLeafAtom(atom);
        _hosts?.OnLeafAtom(atom);

        if (atom.Depth == 0 && atom.Header.Name == PcpKnownAtomNames.Quit)
        {
            if (atom.Payload.Length != sizeof(uint))
                throw new InvalidDataException("PCP QUIT must contain a four-byte code.");
            Span<byte> code = stackalloc byte[sizeof(uint)];
            atom.Payload.CopyTo(code);
            _quit = PcpQuitCodec.Decode(code);
        }

        if (IsPacketData(atom))
        {
            CapturePayload(atom);
        }
    }

    public void OnParentEnded(PcpParentEnded atom)
    {
        ThrowIfDisposed();

        if (_parents.Count == 0 ||
            atom.Depth != _parents.Count - 1 ||
            atom.Name != _parents[^1])
        {
            throw new InvalidDataException(
                "The stream sink parent callback sequence is inconsistent.");
        }

        _controlSink.OnParentEnded(atom);
        _hosts?.OnParentEnded(atom);
        _parents.RemoveAt(_parents.Count - 1);
    }

    public PcpOwnedChannelFrame? TakeCompletedFrame()
    {
        ThrowIfDisposed();
        var frame = _completedFrame;
        _completedFrame = null;
        return frame;
    }

    public PcpQuitInfo? TakeQuit()
    {
        ThrowIfDisposed();
        var received = _quit;
        _quit = null;
        return received;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _payloadOwner?.Dispose();
        _payloadOwner = null;
        _completedFrame?.Dispose();
        _completedFrame = null;
    }

    private bool IsPacketData(PcpLeafAtom atom)
    {
        return atom.Depth == 2 &&
            atom.Header.Name == PcpKnownAtomNames.Data &&
            _parents.Count == 2 &&
            _parents[0] == PcpKnownAtomNames.Channel &&
            _parents[1] == PcpKnownAtomNames.ChannelPacket;
    }

    private void CapturePayload(PcpLeafAtom atom)
    {
        if (_hasPayload)
        {
            throw new InvalidDataException(
                "A CHAN message cannot contain more than one packet payload.");
        }

        if (atom.Header.Value > _maxPacketBytes)
        {
            throw new InvalidDataException(
                $"The CHAN packet exceeds the {_maxPacketBytes}-byte packet limit.");
        }

        _hasPayload = true;
        _payloadLength = atom.Header.Value;

        if (_payloadLength == 0)
        {
            return;
        }

        var owner = _memoryPool.Rent(_payloadLength);

        try
        {
            atom.Payload.CopyTo(owner.Memory.Span[.._payloadLength]);
            _payloadOwner = owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private void CompleteControlMessage(PcpChannelControlMessage control)
    {
        if (_completedFrame is not null)
        {
            throw new InvalidDataException(
                "A completed CHAN frame was not consumed before the next frame.");
        }

        if (control.Packet is not null && !_hasPayload)
        {
            throw new InvalidDataException(
                "A CHAN packet completed without a captured payload.");
        }

        if (control.Packet is null && _hasPayload)
        {
            throw new InvalidDataException(
                "A payload was captured without CHAN packet control fields.");
        }

        if (control.Packet is not null &&
            control.Packet.DataLength != _payloadLength)
        {
            throw new InvalidDataException(
                "The captured packet length does not match CHAN control data.");
        }

        var payload = _payloadOwner is null
            ? ReadOnlyMemory<byte>.Empty
            : _payloadOwner.Memory[.._payloadLength];

        _completedFrame = new PcpOwnedChannelFrame(
            control,
            _payloadOwner,
            payload);

        _payloadOwner = null;
        _payloadLength = 0;
        _hasPayload = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }
}

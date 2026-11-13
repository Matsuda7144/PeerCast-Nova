using System.Security.Cryptography;

namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpSmartRelayDiscontinuityException() : IOException("PCP media continuity cannot be proven; restart with a fresh HEAD.");

/// <summary>Serialized acceptance cursor, not the last observed packet start in Core.</summary>
internal sealed class PcpSmartRelayMediaConsumer(IPcpChannelPacketConsumer inner, TimeProvider clock,
    Func<CancellationToken, Task>? reset, Action? resume, Action progressed) : IPcpChannelPacketConsumer
{
    private const uint Head = 0x64616568, Data = 0x61746164;
    private byte[]? _headHash;
    private byte[]? _lastData;
    private uint _lastDataPosition;
    private long _lastDataAt;
    private bool _needsHead;
    private bool _pendingDiscontinuity;
    private long? _firstProgress;
    internal long? Cursor { get; private set; }
    internal long LastProgress { get; private set; } = clock.GetTimestamp();
    internal long Generation { get; private set; }
    internal long Discontinuities { get; private set; }
    internal bool Stable => _firstProgress is { } first && clock.GetElapsedTime(first, LastProgress) >= TimeSpan.FromSeconds(30);
    internal void BeginAttempt() { _firstProgress = null; LastProgress = clock.GetTimestamp(); }
    internal void ForgetDuplicateProof() => _lastData = null;

    internal async Task SuspendAsync(CancellationToken token)
    {
        _needsHead = Generation != 0 || _headHash is not null;
        _firstProgress = null;
        if (clock.GetElapsedTime(_lastDataAt) >= TimeSpan.FromSeconds(120)) ForgetDuplicateProof();
        _pendingDiscontinuity = Generation != 0;
        if (reset is not null) await reset(token).ConfigureAwait(false);
    }

    public async ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken token)
    {
        if (packet.Type == Head)
        {
            var hash = SHA256.HashData(packet.Payload.Span);
            if (_headHash is not null && !_headHash.AsSpan().SequenceEqual(hash) ||
                _headHash is null && Cursor is not null)
            {
                await SuspendAsync(token).ConfigureAwait(false);
                Cursor = null;
                _lastData = null;
            }
            if (_needsHead || Generation == 0)
            {
                Generation++;
                if (_pendingDiscontinuity) Discontinuities++;
            }
            _headHash = hash;
            _needsHead = false;
            resume?.Invoke();
        }
        else if (packet.Type == Data)
        {
            if (_needsHead)
            { Cursor = null; _lastData = null; throw new PcpSmartRelayDiscontinuityException(); }
            if (Cursor is { } end && packet.WirePosition != unchecked((uint)end))
            {
                // Only a byte-identical, bounded previous packet proves duplication.
                if (packet.WirePosition == _lastDataPosition && _lastData is not null &&
                    clock.GetElapsedTime(_lastDataAt) < TimeSpan.FromSeconds(120) &&
                    packet.Payload.Span.SequenceEqual(_lastData)) return;
                Cursor = null;
                _lastData = null;
                throw new PcpSmartRelayDiscontinuityException();
            }
            if (Generation == 0) { Generation = 1; resume?.Invoke(); }
        }
        var start = packet.Type == Data ? Cursor ?? packet.WirePosition : packet.StreamPosition;
        var discontinuity = _pendingDiscontinuity
            ? new PcpStreamDiscontinuity(unchecked((uint)(Cursor ?? packet.WirePosition)), packet.WirePosition, Cursor ?? packet.WirePosition)
            : packet.Discontinuity;
        await inner.ConsumeAsync(new(packet.ChannelId, packet.SessionId, packet.Sequence, packet.Type,
            packet.WirePosition, start, packet.Payload, discontinuity, packet.Continuation), token).ConfigureAwait(false);
        _pendingDiscontinuity = false;
        if (packet.Type == Data && !packet.Payload.IsEmpty)
        {
            Cursor = checked((start ?? packet.WirePosition) + packet.Payload.Length);
            _lastDataPosition = packet.WirePosition;
            _lastDataAt = clock.GetTimestamp();
            _lastData = packet.Payload.Length <= 1024 * 1024 ? packet.Payload.ToArray() : null;
            LastProgress = clock.GetTimestamp();
            _firstProgress ??= LastProgress;
            progressed();
        }
    }
}

using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>
/// An application-lifetime broadcast identity and a single generation slot.
/// Borrowed by the application-owned integration; construction performs no communication.
/// </summary>
internal sealed class PcpAnnounceOwner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IPcpAnnounceConnector _connector;
    private readonly PcpAnnounceOptions _options;
    private readonly TimeProvider _time;
    private PcpAnnounceWorker? _current;
    private bool _disposed;
    internal Guid BroadcastId { get; }
    internal PcpAnnounceOwner(IPcpAnnounceConnector? connector = null, PcpAnnounceOptions? options = null, TimeProvider? timeProvider = null, Guid? broadcastId = null)
    {
        BroadcastId = NormalizeOrCreateBroadcastId(broadcastId);
        _connector = connector ?? new PcpAnnounceLoopbackConnector(); _options = options ?? new(); _options.Validate(); _time = timeProvider ?? TimeProvider.System;
    }

    internal static Guid NormalizeOrCreateBroadcastId(Guid? candidate)
    {
        if (candidate is { } value && value != Guid.Empty)
        {
            var normalizedBytes = PcpWireValueCodec.EncodeGuid(value);
            normalizedBytes[0] = 0; // PeerCast broadcast identity convention; retained across generations.
            var normalized = PcpWireValueCodec.DecodeGuid(normalizedBytes);
            if (normalized != Guid.Empty)
            {
                return normalized;
            }
        }

        var bytes = PcpWireValueCodec.EncodeGuid(Guid.NewGuid());
        bytes[0] = 0; // PeerCast broadcast identity convention; retained for this owner's entire lifetime.
        return PcpWireValueCodec.DecodeGuid(bytes);
    }
    internal PcpAnnounceWorker Start(long generation, Guid channelId, Guid publishingSessionId, Guid relaySessionId,
        PcpAnnounceEndpoint tracker, PcpAnnounceDocument document, PcpAnnounceAccess? access = null, PublishingAnnounceCapability? capability = null, Func<PublishingAnnounceRuntimeSnapshot>? runtime = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (generation < 1 || channelId == Guid.Empty || publishingSessionId == Guid.Empty || relaySessionId != publishingSessionId || relaySessionId == BroadcastId)
                throw new ArgumentException("The publishing generation must match the existing downstream relay SID contract.");
            if (_current is { } previous && (generation <= previous.Identity.Generation || !previous.CleanupProven))
                throw new InvalidOperationException("The previous announcement generation has not proved cleanup.");
            var identity = new PcpAnnounceIdentity(generation, channelId, publishingSessionId, relaySessionId, BroadcastId);
            _current = new(identity, tracker, document, access ?? PcpAnnounceAccess.Disabled, _connector, _options, _time, capability, runtime);
            _current.Start(cancellationToken);
            return _current;
        }
    }
    public async ValueTask DisposeAsync()
    {
        PcpAnnounceWorker? worker;
        lock (_gate) { _disposed = true; worker = _current; }
        if (worker is not null) await worker.DisposeAsync().ConfigureAwait(false);
    }
}

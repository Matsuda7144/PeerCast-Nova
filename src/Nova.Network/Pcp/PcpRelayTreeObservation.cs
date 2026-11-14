using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

// Separate redacted projection: existing content-free status contracts
// stay unchanged. No endpoints, peer strings, exceptions or resource handles.
public enum PcpRelayConnectionPhase { Unknown, Connecting, Negotiating, Connected, Closing, Disconnected }
public enum PcpRelayObservedPath { Unknown, ChannelListener, SharedPublicListener, ChannelLoopback }
public sealed record PcpRelayConnectionObservation(long? ConnectionId, PcpRelayConnectionPhase Phase,
    PcpRelayObservedPath Path = PcpRelayObservedPath.Unknown, PcpDownstreamTrafficSnapshot? Traffic = null);
public sealed record PcpRelayTreeObservation(ChannelId ChannelId, ChannelSessionId OwnerId,
    bool Running, bool CleanupUnconfirmed, PcpRelayConnectionObservation? Upstream, bool? LocalInputConnected,
    PcpSmartRelayState Recovery, IReadOnlyList<PcpRelayConnectionObservation> Downstreams, int OmittedConnections)
{
    public const int MaximumConnections = 64;
}
public interface IPcpRelayTreeSource { PcpRelayTreeObservation ObserveRelayTree(); }
public interface IPcpRelayTreeRegistration { void ObserveSource(IPcpRelayTreeSource source); }

public sealed partial class PcpDownstreamRelayServer : IPcpRelayTreeSource
{
    internal const int MaximumObservedConnections = PcpRelayTreeObservation.MaximumConnections;
    private PcpRelayObservedPath _observedChannelPath;
    internal static PcpRelayObservedPath ClassifyObservedPath(IPAddress boundAddress) =>
        IPAddress.IsLoopback(boundAddress) ? PcpRelayObservedPath.ChannelLoopback : PcpRelayObservedPath.ChannelListener;
    private void ObserveBoundChannelPath() => _observedChannelPath = ClassifyObservedPath(_listener.LocalEndPoint.Address);
    PcpRelayTreeObservation IPcpRelayTreeSource.ObserveRelayTree() => ObserveRelayTree();
    internal PcpRelayTreeObservation ObserveRelayTree()
    {
        lock (_gate)
        {
            var entries = _sessions.Take(MaximumObservedConnections).ToArray();
            var connections = entries.Select(e => e.Session?.ObserveRelayConnection(e.Shared
                ? PcpRelayObservedPath.SharedPublicListener : _observedChannelPath))
                .OfType<PcpRelayConnectionObservation>().OrderBy(e => e.ConnectionId).ToArray();
            return new(_channelId, new(_localSessionId), _state == PcpDownstreamRelayServerState.Running,
                _pendingAdmissionCleanup.Any(t => !t.IsCompletedSuccessfully || !t.Result) ||
                    _admissionCleanupFailedCount != 0 || _cleanupIncompleteCount != 0,
                null, null, PcpSmartRelayState.Disabled, Array.AsReadOnly(connections),
                Math.Max(0, _sessions.Count - entries.Length));
        }
    }
}

public sealed partial class PcpDownstreamChildSession
{
    private int _identifiedRelay, _observationClosing;
    internal PcpRelayConnectionObservation? ObserveRelayConnection(PcpRelayObservedPath path)
    {
        // Ping and unvalidated/channel-mismatched TCPs are not PCP peers.
        if (Volatile.Read(ref _identifiedRelay) == 0) return null;
        var phase = _connection.IsDisposed || Volatile.Read(ref _observationClosing) != 0
            ? PcpRelayConnectionPhase.Closing : IsEstablishedRelay
                ? PcpRelayConnectionPhase.Connected : PcpRelayConnectionPhase.Negotiating;
        return new(_connection.ConnectionId, phase, path, TrafficSnapshot);
    }
}

public sealed partial class PcpChannelSession
{
    private PcpRelayConnectionObservation _observedConnection = new(null, PcpRelayConnectionPhase.Disconnected);
    internal PcpRelayConnectionObservation ObservedConnection => Volatile.Read(ref _observedConnection);
    private void ObserveConnection(long? id, PcpRelayConnectionPhase phase) =>
        Volatile.Write(ref _observedConnection, new(id, phase));
    private ConnectionObservationLifetime ObserveConnectionLifetime(TcpTransportConnection connection)
    {
        ObserveConnection(connection.ConnectionId, PcpRelayConnectionPhase.Negotiating);
        return new ConnectionObservationLifetime(this, connection);
    }
    private sealed class ConnectionObservationLifetime(PcpChannelSession session, TcpTransportConnection connection) : IDisposable
    {
        public void Dispose()
        {
            var cleaned = connection.CleanupCompletion.IsCompletedSuccessfully;
            session.ObserveConnection(cleaned ? null : connection.ConnectionId,
                cleaned ? PcpRelayConnectionPhase.Disconnected : PcpRelayConnectionPhase.Closing);
        }
    }
}

public sealed partial class PcpRelayCoordinator : IPcpRelayTreeSource
{
    PcpRelayTreeObservation IPcpRelayTreeSource.ObserveRelayTree() => ObserveRelayTree();
    internal PcpRelayTreeObservation ObserveRelayTree()
    {
        var downstream = _downstreamServer.ObserveRelayTree();
        return downstream with
        {
            Upstream = _upstreamSession.ObservedConnection,
            Recovery = SmartRelaySnapshot.State,
            CleanupUnconfirmed = downstream.CleanupUnconfirmed || !_upstreamSession.SmartRelayCleanupConfirmed,
        };
    }
}

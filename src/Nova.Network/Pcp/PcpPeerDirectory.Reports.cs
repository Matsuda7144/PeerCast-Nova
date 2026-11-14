using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Pcp;

// Local GUI value contract only. No internals friendship, live objects, arbitrary
// peer text or remote-management integration. Existing redacted APIs are unchanged.
public sealed record PcpLocalRelayEndpoint(uint Address, int Port)
{
    public string AddressText => new IPAddress(new[] { (byte)(Address >> 24), (byte)(Address >> 16), (byte)(Address >> 8), (byte)Address }).ToString();
    internal static PcpLocalRelayEndpoint? Copy(PcpRelayHostEndpoint? endpoint)
    {
        if (endpoint?.Address.AddressFamily != AddressFamily.InterNetwork) return null;
        var bytes = endpoint.Address.GetAddressBytes();
        return new(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes), endpoint.Port);
    }
}
public enum PcpLocalHostSourceKind { UpstreamHandshake, UpstreamStream, DownstreamControl }
public readonly record struct PcpLocalHostSource(PcpLocalHostSourceKind Kind, long ConnectionId);
public sealed record PcpLocalHostInfo(Guid? ChannelId, Guid? PeerSessionId,
    PcpLocalRelayEndpoint? GlobalEndpoint, PcpLocalRelayEndpoint? LocalEndpoint, PcpLocalRelayEndpoint? UpstreamEndpoint,
    byte? Flags, uint? DirectCount, uint? RelayCount, uint? Version, uint? VersionVp)
{
    public bool? Receiving => Flags is { } value ? (value & 0x10) != 0 : null;
    public bool? RelayAvailable => Flags is { } value ? (value & 0x02) != 0 : null;
    public bool? Firewalled => Flags is { } value ? (value & 0x08) != 0 : null;
    internal static PcpLocalHostInfo Copy(PcpRelayHostCandidate host) => new(host.ChannelId, host.PeerSessionId,
        PcpLocalRelayEndpoint.Copy(host.GlobalEndpoint), PcpLocalRelayEndpoint.Copy(host.LocalEndpoint),
        PcpLocalRelayEndpoint.Copy(host.UpstreamEndpoint), host.Flags, host.DirectCount, host.RelayCount, host.Version, host.VersionVp);
}
public sealed record PcpLocalReportedHostObservation(long ReportId, PcpLocalHostInfo Host,
    PcpLocalHostSource Source, bool ViaBroadcast, TimeSpan Age);
public sealed record PcpLocalPeerIdentity(Guid? SessionId, PcpLocalRelayEndpoint? Endpoint, PcpLocalRelayEndpoint? ListeningEndpoint = null)
{
    internal static PcpLocalPeerIdentity Copy(PcpRelayPeerIdentity peer) =>
        new(peer.SessionId, PcpLocalRelayEndpoint.Copy(peer.Endpoint), PcpLocalRelayEndpoint.Copy(peer.ListeningEndpoint));
}
public sealed record PcpLocalPeerConnection(long ConnectionId, PcpLocalPeerIdentity Peer);
/// <summary>Address-bearing in-process local-GUI data; never publish through status/remote APIs.</summary>
public sealed record PcpLocalRelayTreeObservation(Guid ChannelId, Guid OwnerId,
    PcpLocalRelayEndpoint? LocalEndpoint, IReadOnlyList<PcpLocalPeerConnection> Connections,
    IReadOnlyList<PcpLocalReportedHostObservation> Reports)
{
    public const int MaximumReports = PcpPeerDirectory.MaximumCandidates;
    public static TimeSpan MaximumReportAge => PcpPeerDirectory.Lifetime;
}
/// <summary>Optional local-GUI read path. No network operations or control capability.</summary>
public interface IPcpLocalRelayTreeSource
{
    PcpLocalRelayTreeObservation ObserveLocalRelayTree();
}

internal sealed partial class PcpPeerDirectory
{
    private sealed record Report(long Id, PcpRelayHostCandidate Host, PcpPeerSource Source, bool Broadcast, long At);
    private readonly List<Report> _reports = [];
    private long _reportSequence, _reportWindow;
    private int _reportUpdates;

    internal void ObserveReport(PcpRelayHostCandidate host, PcpPeerSource source)
    {
        lock (_gate)
            if (!_retired) ObserveReportCore(host, source, true, _clock.GetTimestamp());
    }

    private void ObserveReportCore(PcpRelayHostCandidate host, PcpPeerSource source, bool broadcast, long now)
    {
        if (_clock.GetElapsedTime(_reportWindow, now) >= TimeSpan.FromSeconds(1))
        { _reportWindow = now; _reportUpdates = 0; }
        if (_reportUpdates >= 64) return;
        _reportUpdates++;
        _reports.RemoveAll(r => _clock.GetElapsedTime(r.At, now) >= Lifetime);
        if (host.ChannelId != ChannelId || host.PeerSessionId is not { } sid || sid == Guid.Empty ||
            sid == LocalSessionId || !ReportEndpoint(host.GlobalEndpoint) && !ReportEndpoint(host.LocalEndpoint)) return;
        host = host with
        {
            GlobalEndpoint = ReportEndpoint(host.GlobalEndpoint) ? host.GlobalEndpoint : null,
            LocalEndpoint = ReportEndpoint(host.LocalEndpoint) ? host.LocalEndpoint : null,
            UpstreamEndpoint = ReportEndpoint(host.UpstreamEndpoint) ? host.UpstreamEndpoint : null,
        };
        // A shared endpoint does not merge different SIDs. Conflicting addresses
        // for the same SID remain separate observations for topology validation.
        var old = _reports.FindIndex(r => r.Host.PeerSessionId == sid &&
            r.Host.GlobalEndpoint == host.GlobalEndpoint && r.Host.LocalEndpoint == host.LocalEndpoint);
        if (old >= 0)
            _reports[old] = new(_reports[old].Id, host, source, broadcast, now);
        else
        {
            if (_reports.Count == MaximumCandidates) _reports.Remove(_reports.MinBy(r => r.At)!);
            _reports.Add(new(++_reportSequence, host, source, broadcast, now));
        }
    }

    internal static bool ReportEndpoint(PcpRelayHostEndpoint? endpoint)
    {
        if (endpoint is not { Port: > 0 and <= 65535 } || endpoint.Address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = endpoint.Address.GetAddressBytes();
        return bytes[0] is > 0 and < 224 && !endpoint.Address.Equals(IPAddress.Broadcast);
    }

    internal PcpLocalRelayTreeObservation ObserveLocalTree()
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            _reports.RemoveAll(r => _clock.GetElapsedTime(r.At, now) >= Lifetime);
            return new(ChannelId, LocalSessionId, _retired ? null : PcpLocalRelayEndpoint.Copy(_localEndpoint),
                Array.AsReadOnly((_retired ? [] : _downstreams.Select(p => new PcpLocalPeerConnection(p.Key, PcpLocalPeerIdentity.Copy(p.Value))).ToArray())),
                Array.AsReadOnly((_retired ? [] : _reports.Select(r => new PcpLocalReportedHostObservation(
                    r.Id, PcpLocalHostInfo.Copy(r.Host), new((PcpLocalHostSourceKind)r.Source.Kind, r.Source.ConnectionId),
                    r.Broadcast, _clock.GetElapsedTime(r.At, now))).ToArray())));
        }
    }
}

public sealed partial class PcpDownstreamRelayServer : IPcpLocalRelayTreeSource
{
    PcpLocalRelayTreeObservation IPcpLocalRelayTreeSource.ObserveLocalRelayTree() => Peers.ObserveLocalTree();
}

public sealed partial class PcpRelayCoordinator : IPcpLocalRelayTreeSource
{
    PcpLocalRelayTreeObservation IPcpLocalRelayTreeSource.ObserveLocalRelayTree()
    {
        var local = _downstreamServer.Peers.ObserveLocalTree();
        var upstream = _upstreamSession.ObservedLocalConnection;
        return upstream is null ? local : local with
        { Connections = Array.AsReadOnly(local.Connections.Prepend(upstream).ToArray()) };
    }
}

public sealed partial class PcpChannelSession
{
    private PcpLocalPeerConnection? _observedLocalConnection;
    internal PcpLocalPeerConnection? ObservedLocalConnection =>
        Volatile.Read(ref _observedLocalConnection) is { } peer && peer.ConnectionId == ObservedConnection.ConnectionId ? peer : null;
}

using System.IO.Pipelines;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// One routed ingress decision. A successful instance owns the route lease
/// until disposed while the caller keeps ownership of the PipeReader.
/// </summary>
internal sealed class PcpDownstreamRelayIngressResolution : IDisposable
{
    private PcpDownstreamRelayRouteResolution? _route;

    internal PcpDownstreamRelayIngressResolution(
        int statusCode,
        PcpDownstreamRelayRouteResolution? route)
    {
        StatusCode = statusCode;
        _route = route;
    }

    public int StatusCode { get; }

    public bool IsRouted
    {
        get
        {
            var route = Volatile.Read(ref _route);
            return route?.IsResolved == true;
        }
    }

    public PcpDownstreamRelayRequest? Request
    {
        get
        {
            var route = Volatile.Read(ref _route);
            return route?.Request;
        }
    }

    public PcpRelayFanout? Fanout
    {
        get
        {
            var route = Volatile.Read(ref _route);
            return route?.Fanout;
        }
    }

    public void Dispose()
    {
        var route = Interlocked.Exchange(ref _route, null);
        route?.Dispose();
    }
}

/// <summary>
/// Reads and validates one downstream relay request through the caller-owned
/// PipeReader, then resolves it against the current shared relay route table.
/// Bytes buffered after the HTTP header stay on the same reader for PCP handoff.
/// </summary>
internal static class PcpDownstreamRelayIngressRouter
{
    public static async ValueTask<PcpDownstreamRelayIngressResolution>
        ReadAndResolveAsync(
            PipeReader reader,
            PcpDownstreamRelayRouteRegistry registry,
            int maxHeaderBytes,
            int expectedPcpVersion,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(registry);

        var validation = await PcpDownstreamRelayRequestReader.ReadAsync(
            reader,
            maxHeaderBytes,
            expectedPcpVersion,
            cancellationToken).ConfigureAwait(false);
        var route = PcpDownstreamRelayRouteResolver.Resolve(
            validation,
            registry);

        if (route.IsResolved)
        {
            return new PcpDownstreamRelayIngressResolution(
                route.StatusCode,
                route);
        }

        var statusCode = route.StatusCode;
        route.Dispose();
        return new PcpDownstreamRelayIngressResolution(
            statusCode,
            route: null);
    }
}

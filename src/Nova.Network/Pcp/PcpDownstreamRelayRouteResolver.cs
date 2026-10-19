namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// One finite routing decision. A resolved decision owns a route lease until
/// disposed, keeping the selected caller-owned fan-out alive through the
/// downstream connection handoff.
/// </summary>
internal sealed class PcpDownstreamRelayRouteResolution : IDisposable
{
    private PcpDownstreamRelayRouteRegistry.PcpDownstreamRelayRouteLease? _lease;

    internal PcpDownstreamRelayRouteResolution(
        int statusCode,
        PcpDownstreamRelayRequest? request,
        PcpDownstreamRelayRouteRegistry.PcpDownstreamRelayRouteLease? lease)
    {
        StatusCode = statusCode;
        Request = request;
        _lease = lease;
    }

    public int StatusCode { get; }

    public PcpDownstreamRelayRequest? Request { get; }

    public bool IsResolved =>
        StatusCode == 200 &&
        Request is not null &&
        Volatile.Read(ref _lease) is not null;

    public PcpRelayFanout? Fanout
    {
        get
        {
            var lease = Volatile.Read(ref _lease);
            return lease?.Fanout;
        }
    }

    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        lease?.Dispose();
    }
}

internal static class PcpDownstreamRelayRouteResolver
{
    public static PcpDownstreamRelayRouteResolution Resolve(
        PcpDownstreamRelayRequestValidation validation,
        PcpDownstreamRelayRouteRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(registry);

        if (!validation.IsAccepted || validation.Request is null)
        {
            var rejectionStatus = validation.StatusCode is 400 or 404
                ? validation.StatusCode
                : 400;
            return new PcpDownstreamRelayRouteResolution(
                rejectionStatus,
                request: null,
                lease: null);
        }

        var request = validation.Request;
        if (!registry.TryAcquire(request.ChannelId, out var lease) ||
            lease is null)
        {
            return new PcpDownstreamRelayRouteResolution(
                404,
                request: null,
                lease: null);
        }

        return new PcpDownstreamRelayRouteResolution(
            200,
            request,
            lease);
    }
}

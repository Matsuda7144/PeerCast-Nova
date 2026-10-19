using System.Globalization;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Privacy-safe routing information extracted from one validated downstream
/// relay HTTP request. The shared ingress can use ChannelId as a routing key
/// without retaining request headers or other peer-provided text.
/// </summary>
internal sealed record PcpDownstreamRelayRequest(
    Guid ChannelId,
    long StreamPosition);

internal sealed record PcpDownstreamRelayRequestValidation(
    int StatusCode,
    PcpDownstreamRelayRequest? Request)
{
    public bool IsAccepted => StatusCode == 200 && Request is not null;
}

/// <summary>
/// Validates protocol-visible downstream HTTP relay request fields independently
/// from any one channel owner. Channel ownership is deliberately checked later so
/// one future shared listener can route a valid request by ChannelId.
/// </summary>
internal static class PcpDownstreamRelayRequestValidator
{
    private const string ChannelPathPrefix = "/channel/";

    public static PcpDownstreamRelayRequestValidation Validate(
        PcpHttpRequestHeader request,
        int expectedPcpVersion)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Method, "GET", StringComparison.Ordinal) ||
            (request.HttpVersion != new Version(1, 0) &&
                request.HttpVersion != new Version(1, 1)))
        {
            return new PcpDownstreamRelayRequestValidation(400, Request: null);
        }

        if (!request.RequestTarget.StartsWith(
                ChannelPathPrefix,
                StringComparison.Ordinal) ||
            request.RequestTarget.Length != ChannelPathPrefix.Length + 32 ||
            !Guid.TryParseExact(
                request.RequestTarget[ChannelPathPrefix.Length..],
                "N",
                out var requestedChannelId))
        {
            return new PcpDownstreamRelayRequestValidation(404, Request: null);
        }

        if (!request.Headers.TryGetValue(
                "x-peercast-pcp",
                out var versionValue) ||
            !int.TryParse(
                versionValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pcpVersion) ||
            pcpVersion != expectedPcpVersion)
        {
            return new PcpDownstreamRelayRequestValidation(400, Request: null);
        }

        if (!request.Headers.TryGetValue(
                "x-peercast-pos",
                out var positionValue) ||
            !long.TryParse(
                positionValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var streamPosition))
        {
            return new PcpDownstreamRelayRequestValidation(400, Request: null);
        }

        return new PcpDownstreamRelayRequestValidation(
            200,
            new PcpDownstreamRelayRequest(
                requestedChannelId,
                streamPosition));
    }
}

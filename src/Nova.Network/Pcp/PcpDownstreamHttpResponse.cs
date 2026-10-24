using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Network.Pcp;

internal static class PcpDownstreamHttpResponse
{
    public static byte[] CreatePcp(
        int statusCode,
        string serverAgent,
        int pcpVersion,
        long streamPosition)
    {
        var reasonPhrase = statusCode switch
        {
            200 => "OK",
            503 => "Service Unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "A PCP response must be 200 or 503."),
        };

        if (streamPosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamPosition),
                streamPosition,
                "The stream position cannot be negative.");
        }

        return Encoding.ASCII.GetBytes(string.Concat(
            "HTTP/1.0 ",
            statusCode.ToString(CultureInfo.InvariantCulture),
            " ",
            reasonPhrase,
            "\r\nServer:",
            serverAgent,
            "\r\nContent-Type:application/x-peercast-pcp",
            "\r\nx-peercast-pcp:",
            pcpVersion.ToString(CultureInfo.InvariantCulture),
            "\r\nx-peercast-pos:",
            streamPosition.ToString(CultureInfo.InvariantCulture),
            "\r\n\r\n"));
    }

    public static byte[] CreateRejection(
        int statusCode,
        string serverAgent)
    {
        var reasonPhrase = statusCode switch
        {
            400 => "Bad Request",
            404 => "Not Found",
            503 => "Service Unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "A rejection response must be 400, 404, or 503."),
        };

        return Encoding.ASCII.GetBytes(string.Concat(
            "HTTP/1.0 ",
            statusCode.ToString(CultureInfo.InvariantCulture),
            " ",
            reasonPhrase,
            "\r\nServer:",
            serverAgent,
            "\r\nContent-Length:0\r\nConnection:close\r\n\r\n"));
    }
}

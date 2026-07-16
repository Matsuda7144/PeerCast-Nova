using System.Text;

namespace PeerCast.Nova.Network.Pcp;

public sealed record PcpRelayRequest
{
    public required Guid ChannelId { get; init; }

    public long StreamPosition { get; init; }

    public int PcpVersion { get; init; } = 1;

    public string UserAgent { get; init; } = "PeerCastNova/0.0.6c2-fix1";

    public string? Host { get; init; }

    public byte[] Encode()
    {
        if (StreamPosition < 0)
        {
            throw new InvalidOperationException(
                "The stream position cannot be negative.");
        }

        if (PcpVersion < 1)
        {
            throw new InvalidOperationException(
                "The PCP version must be positive.");
        }

        ValidateHeaderValue(UserAgent, nameof(UserAgent));

        if (Host is not null)
        {
            ValidateHeaderValue(Host, nameof(Host));
        }

        var builder = new StringBuilder();

        builder.Append("GET /channel/");
        builder.Append(ChannelId.ToString("N").ToUpperInvariant());
        builder.Append(" HTTP/1.0\r\n");

        if (!string.IsNullOrWhiteSpace(Host))
        {
            builder.Append("Host:");
            builder.Append(Host);
            builder.Append("\r\n");
        }

        builder.Append("User-Agent:");
        builder.Append(UserAgent);
        builder.Append("\r\n");
        builder.Append("x-peercast-pcp:");
        builder.Append(PcpVersion);
        builder.Append("\r\n");
        builder.Append("x-peercast-pos:");
        builder.Append(StreamPosition);
        builder.Append("\r\n\r\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void ValidateHeaderValue(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException(
                "HTTP header values cannot contain CR or LF.",
                parameterName);
        }
    }
}

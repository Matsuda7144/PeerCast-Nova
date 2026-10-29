using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App;

internal sealed record DailyPcpPlaybackSource(
    string Host,
    int Port,
    ChannelId ChannelId);

internal static class DailyPcpPlaybackSourceParser
{
    private const string ChannelPathPrefix = "/channel/";

    internal static string Format(string host, int port, Guid channelId)
    {
        var value = $"pcp://{host}:{port}{ChannelPathPrefix}{channelId:N}";
        if (!TryParse(value, out _)) throw new ArgumentException("Invalid PCP endpoint.");
        return value;
    }

    public static bool TryParse(string? source, out DailyPcpPlaybackSource? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(source) ||
            source.Any(char.IsWhiteSpace) ||
            source.Contains('\\') ||
            source.Contains('%') ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "pcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.HostNameType == UriHostNameType.Unknown ||
            uri.UserInfo.Length != 0 ||
            uri.Port is < 1 or > 65_535 ||
            uri.Query.Length != 0 ||
            uri.Fragment.Length != 0)
        {
            return false;
        }

        var schemeDelimiter = source.IndexOf("://", StringComparison.Ordinal);
        if (schemeDelimiter < 0)
        {
            return false;
        }

        var authorityStart = schemeDelimiter + 3;
        var pathStart = source.IndexOf('/', authorityStart);
        if (pathStart < 0)
        {
            return false;
        }

        var rawPath = source[pathStart..];
        if (!string.Equals(rawPath, uri.AbsolutePath, StringComparison.Ordinal) ||
            rawPath.Length != ChannelPathPrefix.Length + 32 ||
            !rawPath.StartsWith(ChannelPathPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(rawPath[ChannelPathPrefix.Length..], "N", out var value) ||
            value == Guid.Empty)
        {
            return false;
        }

        result = new(uri.IdnHost, uri.Port, new ChannelId(value));
        return true;
    }
}

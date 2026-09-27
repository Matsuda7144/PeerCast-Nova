using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Playback;

/// <summary>A direct PCP channel source such as pcp://host:port/channel/{id}.</summary>
public sealed record PlaybackPcpFmp4LiveSource(
    string Host,
    int Port,
    ChannelId ChannelId,
    long InitialPosition = 0)
{
    public static bool TryParse(
        string? sourceUri,
        out PlaybackPcpFmp4LiveSource? source,
        long initialPosition = 0)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(sourceUri) || initialPosition < 0 ||
            !Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "pcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost) ||
            uri.Port is < 1 or > 65_535 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var path = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (path.Length != 2 ||
            !string.Equals(path[0], "channel", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(path[1], "N", out var channelGuid))
        {
            return false;
        }

        source = new(
            uri.DnsSafeHost,
            uri.Port,
            new ChannelId(channelGuid),
            initialPosition);
        return true;
    }
}

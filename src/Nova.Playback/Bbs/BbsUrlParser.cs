using System.Text.RegularExpressions;

namespace PeerCast.Nova.Playback.Bbs;

public static partial class BbsUrlParser
{
    private const string ShitarabaBaseUrl = "https://jbbs.shitaraba.net";
    private const string JpnknBaseUrl = "https://bbs.jpnkn.com";

    public static bool TryParse(string? rawUrl, out BbsTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var trimmed = rawUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;

        if (host is "jbbs.shitaraba.net" or "jbbs.livedoor.jp")
        {
            return TryParseShitaraba(path, out target);
        }

        if (host is "bbs.jpnkn.com")
        {
            return TryParseJpnkn(path, out target);
        }

        return false;
    }

    private static bool TryParseShitaraba(string path, out BbsTarget? target)
    {
        var threadMatch = ShitarabaThreadRegex().Match(path);
        if (threadMatch.Success)
        {
            target = new BbsTarget(
                BbsBoardKind.Shitaraba,
                ShitarabaBaseUrl,
                threadMatch.Groups["cat"].Value,
                threadMatch.Groups["bbs"].Value,
                threadMatch.Groups["key"].Value);
            return true;
        }

        var boardMatch = ShitarabaBoardRegex().Match(path);
        if (boardMatch.Success)
        {
            target = new BbsTarget(
                BbsBoardKind.Shitaraba,
                ShitarabaBaseUrl,
                boardMatch.Groups["cat"].Value,
                boardMatch.Groups["bbs"].Value,
                ThreadKey: null);
            return true;
        }

        target = null;
        return false;
    }

    private static bool TryParseJpnkn(string path, out BbsTarget? target)
    {
        var threadMatch = JpnknThreadRegex().Match(path);
        if (threadMatch.Success)
        {
            target = new BbsTarget(
                BbsBoardKind.Jpnkn,
                JpnknBaseUrl,
                Category: null,
                threadMatch.Groups["board"].Value,
                threadMatch.Groups["key"].Value);
            return true;
        }

        var boardMatch = JpnknBoardRegex().Match(path);
        if (boardMatch.Success)
        {
            var board = boardMatch.Groups["board"].Value;
            if (board.Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                target = null;
                return false;
            }

            target = new BbsTarget(
                BbsBoardKind.Jpnkn,
                JpnknBaseUrl,
                Category: null,
                board,
                ThreadKey: null);
            return true;
        }

        target = null;
        return false;
    }

    [GeneratedRegex(@"^/bbs/(?:read|rawmode)\.cgi/(?<cat>[A-Za-z0-9_-]+)/(?<bbs>\d+)/(?<key>\d+)(?:/.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ShitarabaThreadRegex();

    [GeneratedRegex(@"^/(?<cat>[A-Za-z0-9_-]+)/(?<bbs>\d+)(?:/.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ShitarabaBoardRegex();

    [GeneratedRegex(@"^/test/read\.cgi/(?<board>[A-Za-z0-9_-]+)/(?<key>\d+)(?:/.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex JpnknThreadRegex();

    [GeneratedRegex(@"^/(?<board>[A-Za-z0-9_-]+)(?:/(?:index\.html|subject\.txt)?)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex JpnknBoardRegex();
}

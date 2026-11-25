using System.Globalization;
using System.Text.RegularExpressions;

namespace PeerCast.Nova.Playback.Bbs;

public static partial class BbsParsers
{
    public static IReadOnlyList<BbsThreadSummary> ParseSubject(string text, BbsBoardKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var results = new List<BbsThreadSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryParseSubjectLine(line, kind, out var key, out var titleAndCount) ||
                !seen.Add(key))
            {
                continue;
            }

            var (title, count) = SplitTitleAndCount(titleAndCount);
            results.Add(new BbsThreadSummary(key, BbsTextCodec.ConvertHtmlToPlainText(title), count));
        }

        return results;
    }

    public static IReadOnlyList<BbsPost> ParseShitarabaRawMode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var posts = new List<BbsPost>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Format: num<>name<>mail<>date<>body<>title<>id
            var parts = line.Split("<>");
            if (parts.Length < 5 || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            var name = BbsTextCodec.ConvertHtmlToPlainText(parts[1]);
            var mail = BbsTextCodec.ConvertHtmlToPlainText(parts[2]);
            var rawDate = BbsTextCodec.ConvertHtmlToPlainText(parts[3]);
            var body = BbsTextCodec.ConvertHtmlToPlainText(parts[4]);
            var title = parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5])
                ? BbsTextCodec.ConvertHtmlToPlainText(parts[5])
                : null;
            var explicitId = parts.Length > 6 ? BbsTextCodec.ConvertHtmlToPlainText(parts[6]) : string.Empty;
            var (timestamp, embeddedId) = SplitTimestampAndId(rawDate);
            var id = !string.IsNullOrEmpty(explicitId) ? explicitId : embeddedId;

            posts.Add(new BbsPost(number, name, mail, timestamp, id, body, title));
        }

        return posts;
    }

    public static IReadOnlyList<BbsPost> ParseJpnknDat(string text, int startNumber = 1)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var posts = new List<BbsPost>();
        var number = Math.Max(1, startNumber);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Format: name<>mail<>date ID:xxx<>body<>title
            var parts = line.Split("<>");
            if (parts.Length < 4)
            {
                continue;
            }

            var name = BbsTextCodec.ConvertHtmlToPlainText(parts[0]);
            var mail = BbsTextCodec.ConvertHtmlToPlainText(parts[1]);
            var rawDate = BbsTextCodec.ConvertHtmlToPlainText(parts[2]);
            var body = BbsTextCodec.ConvertHtmlToPlainText(parts[3]);
            var title = parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4])
                ? BbsTextCodec.ConvertHtmlToPlainText(parts[4])
                : null;
            var (timestamp, id) = SplitTimestampAndId(rawDate);

            posts.Add(new BbsPost(number++, name, mail, timestamp, id, body, title));
        }

        return posts;
    }

    private static bool TryParseSubjectLine(string line, BbsBoardKind kind, out string key, out string titleAndCount)
    {
        key = string.Empty;
        titleAndCount = string.Empty;

        if (kind == BbsBoardKind.Shitaraba)
        {
            var commaIndex = line.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex <= 0)
            {
                return false;
            }

            var rawKey = line[..commaIndex].Trim();
            key = rawKey.EndsWith(".cgi", StringComparison.OrdinalIgnoreCase)
                ? rawKey[..^4]
                : rawKey;
            titleAndCount = line[(commaIndex + 1)..].Trim();
            return key.Length > 0 && key.All(char.IsAsciiDigit);
        }

        var sepIndex = line.IndexOf("<>", StringComparison.Ordinal);
        var sepLength = 2;
        if (sepIndex <= 0)
        {
            sepIndex = line.IndexOf(',', StringComparison.Ordinal);
            sepLength = 1;
        }

        if (sepIndex <= 0)
        {
            return false;
        }

        var jpnknKey = line[..sepIndex].Trim();
        key = jpnknKey.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            ? jpnknKey[..^4]
            : jpnknKey;
        titleAndCount = line[(sepIndex + sepLength)..].Trim();
        return key.Length > 0 && key.All(char.IsAsciiDigit);
    }

    private static (string Title, int Count) SplitTitleAndCount(string titleAndCount)
    {
        var match = TrailingCountRegex().Match(titleAndCount);
        if (match.Success &&
            int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return (titleAndCount[..match.Index].TrimEnd(), count);
        }

        return (titleAndCount.Trim(), 0);
    }

    private static (string Timestamp, string Id) SplitTimestampAndId(string rawDate)
    {
        var match = IdTokenRegex().Match(rawDate);
        if (!match.Success)
        {
            return (rawDate.Trim(), string.Empty);
        }

        var id = match.Groups["id"].Value.Trim();
        var timestamp = rawDate[..match.Index].Trim();
        return (timestamp, id);
    }

    [GeneratedRegex(@"\s*\((?<count>\d+)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCountRegex();

    [GeneratedRegex(@"\s+ID:(?<id>[^\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex IdTokenRegex();
}

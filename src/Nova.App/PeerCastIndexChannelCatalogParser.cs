using System.Globalization;
using System.Net;

namespace PeerCast.Nova.App;

internal static class PeerCastIndexChannelCatalogParser
{
    private const int ChannelNameField = 0;
    private const int ChannelIdField = 1;
    private const int TrackerField = 2;
    private const int GenreField = 4;
    private const int DetailField = 5;
    private const int ListenerField = 6;
    private const int RelayField = 7;
    private const int BitrateField = 8;
    private const int TypeField = 9;
    private const int TrackArtistField = 10;
    private const int TrackAlbumField = 11;
    private const int TrackTitleField = 12;
    private const int BroadcastTimeField = 15;
    private const int CommentField = 17;
    private const int MinimumFieldCount = 18;
    private const int ChannelIdCharacters = 32;

    public static GenesisChannelCatalogSnapshot Parse(
        string indexText,
        Uri? fallbackTrackerUri = null)
    {
        ArgumentNullException.ThrowIfNull(indexText);
        ValidateFallbackTrackerUri(fallbackTrackerUri);

        var channels = new List<GenesisChannelItem>();
        foreach (var rawLine in indexText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split("<>", StringSplitOptions.None);
            if (fields.Length < MinimumFieldCount)
            {
                continue;
            }

            var channelId = NormalizeChannelId(fields[ChannelIdField]);
            if (channelId is null)
            {
                continue;
            }

            var source = BuildSource(
                Decode(fields[TrackerField]),
                channelId,
                fallbackTrackerUri);
            if (source is null)
            {
                continue;
            }

            var name = Decode(fields[ChannelNameField]).Trim();
            if (name.Length == 0)
            {
                continue;
            }

            channels.Add(new GenesisChannelItem(
                name,
                source,
                BuildDescription(fields),
                ParseNonNegativeInteger(fields[ListenerField]),
                ParseNonNegativeInteger(fields[RelayField]),
                ParseNonNegativeInteger(fields[BitrateField]),
                ParseBroadcastTime(fields[BroadcastTimeField]),
                NormalizeOptionalText(fields[TypeField]),
                channelId));
        }

        return new(Array.AsReadOnly(channels.ToArray()));
    }

    private static string? BuildSource(
        string tracker,
        string channelId,
        Uri? fallbackTrackerUri)
    {
        var normalizedTracker = tracker.Trim();
        if (normalizedTracker.Length > 0 &&
            TryBuildChannelUri(normalizedTracker, channelId, out var source))
        {
            return source;
        }

        if (fallbackTrackerUri is not null &&
            TryBuildChannelUri(fallbackTrackerUri.Authority, channelId, out source))
        {
            return source;
        }

        return null;
    }

    private static bool TryBuildChannelUri(
        string tracker,
        string channelId,
        out string? source)
    {
        if (tracker.Contains('/') ||
            tracker.Contains('\\') ||
            tracker.Contains('?') ||
            tracker.Contains('#') ||
            tracker.Contains('@'))
        {
            source = null;
            return false;
        }

        if (Uri.TryCreate(
                $"pcp://{tracker}/channel/{channelId}",
                UriKind.Absolute,
                out var uri) &&
            string.Equals(uri.Scheme, "pcp", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            source = uri.AbsoluteUri;
            return true;
        }

        source = null;
        return false;
    }

    private static string? NormalizeChannelId(string value)
    {
        var channelId = value.Trim();
        if (channelId.Length != ChannelIdCharacters ||
            channelId.All(character => character == '0'))
        {
            return null;
        }

        foreach (var character in channelId)
        {
            if (!Uri.IsHexDigit(character))
            {
                return null;
            }
        }

        return channelId.ToUpperInvariant();
    }

    private static string? BuildDescription(string[] fields)
    {
        var genre = NormalizeOptionalText(fields[GenreField]);
        var detail = NormalizeOptionalText(fields[DetailField]);
        var comment = NormalizeOptionalText(fields[CommentField]);
        var artist = NormalizeOptionalText(fields[TrackArtistField]);
        var title = NormalizeOptionalText(fields[TrackTitleField]);
        var album = NormalizeOptionalText(fields[TrackAlbumField]);

        var sections = new List<string>(3);
        var genreDetail = (genre, detail) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"[{genre} - {detail}]",
            ({ Length: > 0 }, _) => $"[{genre}]",
            (_, { Length: > 0 }) => $"[{detail}]",
            _ => null,
        };
        if (genreDetail is not null)
        {
            sections.Add(genreDetail);
        }

        if (comment is not null)
        {
            sections.Add(comment);
        }

        var trackParts = new[] { artist, title, album }
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        if (trackParts.Length > 0)
        {
            sections.Add(string.Join('/', trackParts));
        }

        return sections.Count == 0 ? null : string.Join(' ', sections);
    }

    private static int? ParseNonNegativeInteger(string value) =>
        int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed) &&
        parsed >= 0
            ? parsed
            : null;

    private static TimeSpan? ParseBroadcastTime(string value)
    {
        var components = value.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (components.Length is < 2 or > 3 ||
            !long.TryParse(
                components[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours) ||
            !int.TryParse(
                components[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minutes) ||
            hours < 0 ||
            minutes is < 0 or > 59)
        {
            return null;
        }

        var seconds = 0;
        if (components.Length == 3 &&
            (!int.TryParse(
                    components[2],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out seconds) ||
                seconds is < 0 or > 59))
        {
            return null;
        }

        try
        {
            return TimeSpan.FromSeconds(
                checked((hours * 3_600L) + (minutes * 60L) + seconds));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string Decode(string value) =>
        WebUtility.HtmlDecode(value) ?? string.Empty;

    private static string? NormalizeOptionalText(string value)
    {
        var normalized = Decode(value).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static void ValidateFallbackTrackerUri(Uri? fallbackTrackerUri)
    {
        if (fallbackTrackerUri is null)
        {
            return;
        }

        if (!fallbackTrackerUri.IsAbsoluteUri ||
            !string.Equals(
                fallbackTrackerUri.Scheme,
                "pcp",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(fallbackTrackerUri.Host))
        {
            throw new ArgumentException(
                "The fallback tracker URI must be an absolute pcp:// URI.",
                nameof(fallbackTrackerUri));
        }
    }
}

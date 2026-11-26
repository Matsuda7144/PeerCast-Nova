using System.Globalization;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal static class GenesisRecordingLibraryFilenameParser
{
    internal static bool TryParse(string filename, out GenesisRecordingLibraryFilenameClaims? claims)
    {
        claims = null;
        var extension = Path.GetExtension(filename);
        if (extension.Length is < 2 or > DefaultRecordingFilePathProvider.MaximumExtensionLength + 1 ||
            extension.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c))) return false;
        var stem = filename[..^extension.Length];
        // 15-character UTC timestamp, separator, nonempty name, and two
        // trailing separator + 32-hex tokens. Name underscores are not split.
        if (stem.Length < 83 || stem[15] != '_' || stem[^33] != '_' || stem[^66] != '_' ||
            !DateTimeOffset.TryParseExact(stem.AsSpan(0, 15), "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ||
            !Guid.TryParseExact(stem.AsSpan(stem.Length - 65, 32), "N", out var channelId) || channelId == Guid.Empty ||
            !Guid.TryParseExact(stem.AsSpan(stem.Length - 32), "N", out var sessionId) || sessionId == Guid.Empty) return false;
        var name = stem[16..^66];
        if (string.IsNullOrWhiteSpace(name) || name.Length > DefaultRecordingFilePathProvider.MaximumNameComponentLength + 1 ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))) return false;
        claims = new(time, name, channelId, sessionId);
        return true;
    }
}

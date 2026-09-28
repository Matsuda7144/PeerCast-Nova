using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Conservative cross-platform recording path policy.
/// </summary>
public sealed class DefaultRecordingFilePathProvider : IRecordingFilePathProvider
{
    public const int MaximumNameComponentLength = 80;
    public const int MaximumExtensionLength = 10;

    private static readonly char[] UnsafeFileNameCharacters =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*',
    ];

    private static readonly HashSet<string> ReservedWindowsNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

    private readonly string baseDirectory;
    private readonly Func<DateTimeOffset> nowProvider;

    public DefaultRecordingFilePathProvider(
        string baseDirectory,
        Func<DateTimeOffset>? nowProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        this.baseDirectory = Path.GetFullPath(baseDirectory);
        this.nowProvider = nowProvider ?? GetUtcNow;
    }

    public string BaseDirectory => baseDirectory;

    public string GetPath(
        RecordingSinkStartRequest request,
        RecordingSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sessionId);

        var timestamp = nowProvider()
            .ToUniversalTime()
            .ToString(
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture);

        var channelName = SanitizeNameComponent(
            request.Candidate.Metadata.Name);

        var channelId = request.Candidate.ChannelId.ToString();

        var extension = NormalizeExtension(
            request.Candidate.Metadata.StreamExtension);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp}_{channelName}_{channelId}_{sessionId}{extension}");

        var combined = Path.Combine(
            baseDirectory,
            fileName);

        var fullPath = Path.GetFullPath(combined);

        EnsureContainedByBaseDirectory(fullPath);

        return fullPath;
    }

    private static string SanitizeNameComponent(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "channel"
            : value
                .Normalize(NormalizationForm.FormKC)
                .Trim();

        var builder = new StringBuilder(
            Math.Min(
                normalized.Length,
                MaximumNameComponentLength));

        var previousUnderscore = false;

        foreach (var character in normalized)
        {
            var replacement =
                char.IsControl(character) ||
                UnsafeFileNameCharacters.Contains(character)
                    ? '_'
                    : character;

            if (replacement == '_')
            {
                if (previousUnderscore)
                {
                    continue;
                }

                previousUnderscore = true;
            }
            else
            {
                previousUnderscore = false;
            }

            builder.Append(replacement);

            if (builder.Length >= MaximumNameComponentLength)
            {
                break;
            }
        }

        var sanitized = builder
            .ToString()
            .Trim(' ', '.', '_');

        if (sanitized.Length == 0)
        {
            sanitized = "channel";
        }

        if (ReservedWindowsNames.Contains(sanitized))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }

    private static string NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ".bin";
        }

        var candidate = value.Trim();

        if (candidate.StartsWith('.'))
        {
            candidate = candidate[1..];
        }

        if (candidate.Length == 0 ||
            candidate.Length > MaximumExtensionLength ||
            candidate.Any(
                static character =>
                    !char.IsAsciiLetterOrDigit(character)))
        {
            return ".bin";
        }

        return $".{candidate.ToLowerInvariant()}";
    }

    private static DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UtcNow;
    }

    private void EnsureContainedByBaseDirectory(string fullPath)
    {
        var relative = Path.GetRelativePath(
            baseDirectory,
            fullPath);

        if (relative.Equals(
                "..",
                StringComparison.Ordinal) ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                "The generated recording path escaped its configured base directory.");
        }
    }
}

using System.Globalization;

namespace PeerCast.Nova.Cli;

internal static class PlaybackControlledWindowsAudioRenderFailureRecordText
{
    internal static bool TryGetLines(
        string? text,
        int maximumCharacters,
        int expectedLineCount,
        out string normalized,
        out string[] lines)
    {
        normalized = string.Empty;
        lines = [];
        if (string.IsNullOrEmpty(text) || text.Length > maximumCharacters)
        {
            return false;
        }

        normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }
        lines = normalized.Split('\n');
        return lines.Length == expectedLineCount;
    }

    internal static bool TryReadExact(
        string line,
        string prefix,
        out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[prefix.Length..];
        return value.Length > 0;
    }

    internal static bool TryReadBoolean(
        string line,
        string prefix,
        out bool value)
    {
        value = false;
        if (!TryReadExact(line, prefix, out var text))
        {
            return false;
        }
        if (string.Equals(text, "True", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }
        return string.Equals(text, "False", StringComparison.Ordinal);
    }

    internal static bool TryReadInt32(
        string line,
        string prefix,
        out int value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            int.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
    }

    internal static bool TryReadInt64(
        string line,
        string prefix,
        out long value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadHexUInt32(
        string line,
        string prefix,
        out uint value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            text.Length == 8 &&
            uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadEnum<TEnum>(
        string line,
        string prefix,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        return TryReadExact(line, prefix, out var text) &&
            Enum.TryParse(text, ignoreCase: false, out value) &&
            Enum.IsDefined(value);
    }
}

using System.Globalization;
using System.Text;

namespace PeerCast.Nova.App;

internal static class PeerCastHttpIndexResponse
{
    private const int MaximumHeaderBytes = 64 * 1024;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    public static string Decode(byte[] responseBytes)
    {
        return Decode(responseBytes, out _, out _);
    }

    internal static string Decode(
        byte[] responseBytes,
        out int? statusCode,
        out int? responseBodyByteCount)
    {
        ArgumentNullException.ThrowIfNull(responseBytes);
        statusCode = null;
        responseBodyByteCount = null;
        if (responseBytes.Length == 0)
        {
            throw new InvalidDataException("The HTTP response is empty.");
        }

        var headerEnd = FindSequence(responseBytes, HeaderTerminator);
        if (headerEnd < 0 || headerEnd > MaximumHeaderBytes)
        {
            throw new InvalidDataException("The HTTP response header is invalid or too large.");
        }

        var headerText = Encoding.ASCII.GetString(responseBytes, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || !TryParseStatusCode(lines[0], out var parsedStatusCode))
        {
            throw new InvalidDataException("The HTTP response status is not successful.");
        }

        statusCode = parsedStatusCode;
        var bodyOffset = checked(headerEnd + HeaderTerminator.Length);
        responseBodyByteCount = Math.Max(0, responseBytes.Length - bodyOffset);
        if (parsedStatusCode != 200)
        {
            throw new InvalidDataException("The HTTP response status is not successful.");
        }

        var contentLength = ParseContentLength(lines);
        var charset = ParseContentCharset(lines);
        EnsureSupportedTransferEncoding(lines);
        EnsureSupportedContentEncoding(lines);

        var availableBodyLength = responseBytes.Length - bodyOffset;
        var bodyLength = contentLength ?? availableBodyLength;
        if (bodyLength < 0 || bodyLength > availableBodyLength)
        {
            throw new InvalidDataException("The HTTP response body is truncated.");
        }

        return PeerCastIndexTextEncoding.Decode(
            responseBytes.AsSpan(bodyOffset, bodyLength),
            charset);
    }

    private static bool TryParseStatusCode(string statusLine, out int statusCode)
    {
        statusCode = 0;
        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
            parts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out statusCode);
    }

    private static int? ParseContentLength(string[] lines)
    {
        int? contentLength = null;
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    "Content-Length",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(
                    line[(separator + 1)..].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedContentLength) ||
                parsedContentLength < 0)
            {
                throw new InvalidDataException("The HTTP Content-Length is invalid.");
            }

            if (contentLength is not null && contentLength.Value != parsedContentLength)
            {
                throw new InvalidDataException(
                    "The HTTP response declares conflicting Content-Length values.");
            }

            contentLength = parsedContentLength;
        }

        return contentLength;
    }

    private static string? ParseContentCharset(string[] lines)
    {
        string? charset = null;
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    "Content-Type",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameters = line[(separator + 1)..].Split(';');
            foreach (var parameter in parameters.Skip(1))
            {
                var assignment = parameter.Split('=', 2);
                if (assignment.Length != 2 ||
                    !string.Equals(
                        assignment[0].Trim(),
                        "charset",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = assignment[1].Trim().Trim('"');
                if (candidate.Length == 0)
                {
                    throw new InvalidDataException("The HTTP response charset is empty.");
                }

                if (charset is not null &&
                    !string.Equals(charset, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The HTTP response declares conflicting charsets.");
                }

                charset = candidate;
            }
        }

        return charset;
    }

    private static void EnsureSupportedTransferEncoding(string[] lines)
    {
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    "Transfer-Encoding",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (!string.Equals(value, "identity", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Chunked or transformed HTTP responses are not supported by the SP transport.");
            }
        }
    }

    private static void EnsureSupportedContentEncoding(string[] lines)
    {
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    "Content-Encoding",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (!string.Equals(value, "identity", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Encoded HTTP response bodies are not supported by the SP transport.");
            }
        }
    }

    private static int FindSequence(byte[] value, byte[] sequence)
    {
        var maximumStart = value.Length - sequence.Length;
        for (var index = 0; index <= maximumStart; index++)
        {
            var matches = true;
            for (var sequenceIndex = 0; sequenceIndex < sequence.Length; sequenceIndex++)
            {
                if (value[index + sequenceIndex] != sequence[sequenceIndex])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return index;
            }
        }

        return -1;
    }
}

using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace PeerCast.Nova.Network.Pcp;

internal static class PcpHttpHeaderBlockReader
{
    private static ReadOnlySpan<byte> Terminator => "\r\n\r\n"u8;

    public static async ValueTask<PcpHttpResponseHeader> ReadResponseAsync(
        PipeReader reader,
        int maxHeaderBytes,
        CancellationToken cancellationToken)
    {
        var lines = await ReadLinesAsync(
            reader,
            maxHeaderBytes,
            allowObsTextInFieldValues: true,
            cancellationToken).ConfigureAwait(false);

        if (lines.Length == 0)
        {
            throw new InvalidDataException("The HTTP response has no status line.");
        }

        var parts = lines[0].Split(
            ' ',
            3,
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2
            || !TryParseHttpVersion(parts[0], out var version)
            || !int.TryParse(parts[1], out var statusCode))
        {
            throw new InvalidDataException(
                "The HTTP response status line is invalid.");
        }

        var reasonPhrase = parts.Length == 3 ? parts[2] : string.Empty;

        return new PcpHttpResponseHeader(
            version,
            statusCode,
            reasonPhrase,
            ParseHeaders(lines.AsSpan(1)));
    }

    public static async ValueTask<PcpHttpRequestHeader> ReadRequestAsync(
        PipeReader reader,
        int maxHeaderBytes,
        CancellationToken cancellationToken)
    {
        var lines = await ReadLinesAsync(
            reader,
            maxHeaderBytes,
            allowObsTextInFieldValues: false,
            cancellationToken).ConfigureAwait(false);

        if (lines.Length == 0)
        {
            throw new InvalidDataException("The HTTP request has no request line.");
        }

        var parts = lines[0].Split(
            ' ',
            3,
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3
            || !TryParseHttpVersion(parts[2], out var version))
        {
            throw new InvalidDataException(
                "The HTTP request line is invalid.");
        }

        return new PcpHttpRequestHeader(
            parts[0],
            parts[1],
            version,
            ParseHeaders(lines.AsSpan(1)));
    }

    private static async ValueTask<string[]> ReadLinesAsync(
        PipeReader reader,
        int maxHeaderBytes,
        bool allowObsTextInFieldValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (maxHeaderBytes < 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxHeaderBytes),
                maxHeaderBytes,
                "The HTTP header limit is too small.");
        }

        while (true)
        {
            var result = await reader.ReadAsync(
                cancellationToken).ConfigureAwait(false);

            var buffer = result.Buffer;
            var advanced = false;

            try
            {
                if (result.IsCanceled)
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    advanced = true;
                    throw new OperationCanceledException(cancellationToken);
                }

                var candidateLength = checked((int)Math.Min(
                    buffer.Length,
                    maxHeaderBytes + Terminator.Length));

                var candidate = new byte[candidateLength];
                buffer.Slice(0, candidateLength).CopyTo(candidate);
                var terminatorIndex = candidate.AsSpan().IndexOf(Terminator);

                if (terminatorIndex >= 0)
                {
                    var headerLength = terminatorIndex + Terminator.Length;

                    if (headerLength > maxHeaderBytes)
                    {
                        throw new InvalidDataException(
                            $"The HTTP header exceeds {maxHeaderBytes} bytes.");
                    }

                    var consumed = buffer.GetPosition(headerLength);
                    reader.AdvanceTo(consumed, consumed);
                    advanced = true;

                    ValidateHeaderText(
                        candidate.AsSpan(0, terminatorIndex),
                        allowObsTextInFieldValues);

                    var text = Encoding.Latin1.GetString(
                        candidate,
                        0,
                        terminatorIndex);

                    return text.Split(
                        "\r\n",
                        StringSplitOptions.None);
                }

                if (buffer.Length > maxHeaderBytes)
                {
                    throw new InvalidDataException(
                        $"The HTTP header exceeds {maxHeaderBytes} bytes.");
                }

                if (result.IsCompleted)
                {
                    throw new EndOfStreamException(
                        "The connection ended before the HTTP header completed.");
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                advanced = true;
            }
            finally
            {
                if (!advanced)
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
    }

    private static Dictionary<string, string> ParseHeaders(
        ReadOnlySpan<string> lines)
    {
        var headers = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');

            if (separator <= 0)
            {
                throw new InvalidDataException(
                    "An HTTP header line has no valid name separator.");
            }

            var name = line[..separator];
            var value = line[(separator + 1)..].Trim();

            if (!IsHttpToken(name))
            {
                throw new InvalidDataException(
                    "An HTTP header name contains an invalid character.");
            }

            if (!headers.TryAdd(name, value))
            {
                headers[name] = $"{headers[name]}, {value}";
            }
        }

        return headers;
    }

    private static void ValidateHeaderText(
        ReadOnlySpan<byte> header,
        bool allowObsTextInFieldValues)
    {
        var isStartLine = true;
        var isFieldValue = false;

        for (var index = 0; index < header.Length; index++)
        {
            var value = header[index];

            if (value == (byte)'\r')
            {
                if (index + 1 >= header.Length ||
                    header[index + 1] != (byte)'\n')
                {
                    throw new InvalidDataException(
                        "An HTTP header contains a bare carriage return.");
                }

                continue;
            }

            if (value == (byte)'\n')
            {
                if (index == 0 || header[index - 1] != (byte)'\r')
                {
                    throw new InvalidDataException(
                        "An HTTP header contains a bare line feed.");
                }

                isStartLine = false;
                isFieldValue = false;
                continue;
            }

            if (!isStartLine &&
                !isFieldValue &&
                value == (byte)':')
            {
                isFieldValue = true;
            }

            if (value == (byte)'\t')
            {
                continue;
            }

            if (value < 0x20 || value == 0x7f)
            {
                throw new InvalidDataException(
                    "An HTTP header contains a disallowed control byte.");
            }

            if (value > 0x7e &&
                (!allowObsTextInFieldValues ||
                    isStartLine ||
                    !isFieldValue))
            {
                throw new InvalidDataException(
                    "An HTTP start line or header name is not ASCII text.");
            }
        }
    }

    private static bool IsHttpToken(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is
                '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or
                '-' or '.' or '^' or '_' or '`' or '|' or '~')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryParseHttpVersion(
        string value,
        out Version version)
    {
        version = default!;

        if (!value.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(value[5..], out var parsedVersion)
            || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }
}

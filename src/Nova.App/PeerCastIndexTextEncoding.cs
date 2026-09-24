using System.Text;

namespace PeerCast.Nova.App;

internal static class PeerCastIndexTextEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly Lazy<Encoding> ShiftJis = new(CreateShiftJis);

    public static string Decode(ReadOnlySpan<byte> bytes, string? charset)
    {
        var normalizedCharset = charset?.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(normalizedCharset))
        {
            if (IsUtf8(normalizedCharset))
            {
                return DecodeUtf8(bytes);
            }

            if (IsShiftJis(normalizedCharset))
            {
                return ShiftJis.Value.GetString(bytes);
            }

            throw new InvalidDataException(
                $"Unsupported HTTP response charset: {normalizedCharset}.");
        }

        try
        {
            return DecodeUtf8(bytes);
        }
        catch (DecoderFallbackException)
        {
            return ShiftJis.Value.GetString(bytes);
        }
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        var decoded = StrictUtf8.GetString(bytes);
        return decoded.Length > 0 && decoded[0] == '\uFEFF'
            ? decoded[1..]
            : decoded;
    }

    private static bool IsUtf8(string charset) =>
        string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(charset, "utf8", StringComparison.OrdinalIgnoreCase);

    private static bool IsShiftJis(string charset) =>
        string.Equals(charset, "shift_jis", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(charset, "shift-jis", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(charset, "windows-31j", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(charset, "cp932", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(charset, "ms932", StringComparison.OrdinalIgnoreCase);

    private static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            932,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }
}

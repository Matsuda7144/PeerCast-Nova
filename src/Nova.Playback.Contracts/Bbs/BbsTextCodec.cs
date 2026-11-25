using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PeerCast.Nova.Playback.Bbs;

public static partial class BbsTextCodec
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Lazy<Encoding> EucJpLazy = new(() =>
    {
        EnsureCodePagesRegistered();
        return Encoding.GetEncoding("EUC-JP");
    });

    private static readonly Lazy<Encoding> ShiftJisLazy = new(() =>
    {
        EnsureCodePagesRegistered();
        return Encoding.GetEncoding(932);
    });

    private static int _registered;

    public static Encoding EucJp => EucJpLazy.Value;

    public static Encoding ShiftJis => ShiftJisLazy.Value;

    public static void EnsureCodePagesRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }

    public static string Decode(byte[] bytes, BbsBoardKind kind, string? contentTypeCharset = null)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(contentTypeCharset))
        {
            var normalized = contentTypeCharset.Trim().Trim('"').ToUpperInvariant();
            if (normalized.Contains("UTF-8", StringComparison.Ordinal) || normalized.Contains("UTF8", StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetString(bytes);
            }

            if (normalized.Contains("EUC-JP", StringComparison.Ordinal) || normalized.Contains("EUCJP", StringComparison.Ordinal))
            {
                return EucJp.GetString(bytes);
            }

            if (normalized.Contains("SHIFT_JIS", StringComparison.Ordinal) ||
                normalized.Contains("SHIFT-JIS", StringComparison.Ordinal) ||
                normalized.Contains("SJIS", StringComparison.Ordinal) ||
                normalized.Contains("CP932", StringComparison.Ordinal) ||
                normalized.Contains("WINDOWS-31J", StringComparison.Ordinal))
            {
                return ShiftJis.GetString(bytes);
            }
        }

        if (kind == BbsBoardKind.Shitaraba)
        {
            return EucJp.GetString(bytes);
        }

        // bbs.jpnkn.com may serve Shift_JIS or UTF-8 depending on board/endpoint; prefer strict UTF-8 when valid.
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return ShiftJis.GetString(bytes);
        }
    }

    public static string EncodeFormBody(IEnumerable<KeyValuePair<string, string>> fields, Encoding encoding)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var (key, value) in fields)
        {
            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            AppendPercentEncoded(builder, key, encoding);
            builder.Append('=');
            var normalizedValue = value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
            AppendPercentEncoded(builder, normalizedValue, encoding);
        }

        return builder.ToString();
    }

    public static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var withNewlines = BreakTagRegex().Replace(html, "\n");
        var withoutTags = HtmlTagRegex().Replace(withNewlines, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return decoded.Trim();
    }

    private static void AppendPercentEncoded(StringBuilder builder, string value, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value);
        foreach (var b in bytes)
        {
            if (IsUnreservedAscii(b))
            {
                builder.Append((char)b);
            }
            else if (b == (byte)' ')
            {
                builder.Append('+');
            }
            else
            {
                builder.Append('%');
                builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool IsUnreservedAscii(byte b) =>
        (b >= (byte)'A' && b <= (byte)'Z') ||
        (b >= (byte)'a' && b <= (byte)'z') ||
        (b >= (byte)'0' && b <= (byte)'9') ||
        b is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'*';

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}

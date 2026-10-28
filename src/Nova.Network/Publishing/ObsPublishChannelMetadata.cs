using System.Text;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Publishing;

internal static class ObsPublishChannelMetadata
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static ChannelMetadataSnapshot Create(ObsPublishChannelOptions options)
    {
        Text(options.ChannelName, nameof(options.ChannelName), int.MaxValue, scalarLimit: false, wireLimit: false);
        var name = options.ChannelName.Trim();
        if (name.Length is < 1 or > 256) throw Invalid("チャンネル名は前後の空白を除いて1～256文字で入力してください。", nameof(options.ChannelName));
        Text(options.Genre, nameof(options.Genre), 128);
        Text(options.Description, nameof(options.Description), 1024);
        Text(options.Comment, nameof(options.Comment), 1024);
        Text(options.ContactUrl, nameof(options.ContactUrl), 2048, scalarLimit: false);
        if (options.ContactUrl.Length != 0 && (!Uri.TryCreate(options.ContactUrl, UriKind.Absolute, out var uri) ||
            options.ContactUrl != options.ContactUrl.Trim() || uri.Scheme is not ("http" or "https") ||
            !uri.IsWellFormedOriginalString() || uri.UserInfo.Length != 0))
            throw Invalid("空欄、またはユーザー情報を含まない絶対HTTP/HTTPS URLを入力してください。", nameof(options.ContactUrl));
        if (options.BitrateKbps is <= 0)
            throw Invalid("1～2147483647の整数kbps、または空欄で入力してください。", nameof(options.BitrateKbps));
        return new(name, options.Genre, options.Description, options.ContactUrl, "FLV", options.BitrateKbps,
            comment: options.Comment, streamExtension: "flv");
    }

    private static ArgumentException Invalid(string message, string field) => new(message, field);

    private static void Text(string text, string field, int maximum, bool scalarLimit = true, bool wireLimit = true)
    {
        if (text is null) throw new ArgumentException("文字列を入力してください。", field);
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException) { throw new ArgumentException("不正なUnicodeは使用できません。", field); }
        if (text.EnumerateRunes().Any(r => Rune.IsControl(r) || r.Value is 0x2028 or 0x2029))
            throw new ArgumentException("制御文字・改行は使用できません。", field);
        if ((scalarLimit ? text.EnumerateRunes().Count() : text.Length) > maximum)
            throw new ArgumentException($"{maximum}文字以内で入力してください。", field);
        if (wireLimit && bytes + 1 > 4096)
            throw new ArgumentException("UTF-8本文と終端NULの合計を4096 bytes以内にしてください。", field);
    }
}

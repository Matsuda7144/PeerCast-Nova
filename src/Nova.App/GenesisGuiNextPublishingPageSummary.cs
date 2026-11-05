namespace PeerCast.Nova.App;

// Read-only formatting only: no settings draft, persistence, connection, or new counters.
internal sealed record GenesisGuiNextPublishingPageSummary(string Basic, string Options, string Diagnostic)
{
    internal static GenesisGuiNextPublishingPageSummary Create(PublishingSettingsSnapshot? saved, PublishingSettingsSnapshot? applied,
        bool loadFailed, NovaObsPublishingReadOnlySnapshot? status, string commandFeedback, string connectionFeedback)
    {
        var basic = "配信設定を確認できません。";
        var options = basic;
        if (!loadFailed && saved is { } snapshot)
        {
            var v = snapshot.Values;
            static string V(string text) => string.IsNullOrWhiteSpace(text) ? "未設定" : text;
            basic = $"チャンネル名: {V(v.ChannelName)}\r\nジャンル: {V(v.Genre)}\r\n概要: {V(v.Description)}\r\nコメント: {V(v.Comment)}\r\nコンタクトURL: {SafeContact(v.ContactUrl)}";
            var scope = v.Wan.Enabled ? "WAN（到達性は別途確認）" : v.Lan.Enabled ? "LAN（到達性は別途確認）" : "このPCのみ";
            var bitrate = v.BitrateKbps is { } n ? $"{n} kbps" : "未指定";
            var used = applied is null ? "未適用（配信開始前）" : applied.Values == v ? "保存値と一致" : "保存値と異なります";
            options = $"ソース: OBS（ローカルRTMP）\r\n公開範囲の設定: {scope}\r\nビットレート: {bitrate}\r\nYP掲載: {(v.Announce.Enabled ? v.Announce.Name : "無効")}\r\n前回開始時の設定: {used}\r\n変更は「配信設定…」で保存し、次回の配信開始から使用します。";
        }
        else if (loadFailed) basic = options = PublishingSettingsController.LoadWarning;
        var diagnostic = status is null ? "配信状態を確認できません。" :
            $"ChannelId: {status.ChannelId?.ToString("N").ToUpperInvariant() ?? "未接続"} / SessionId: {status.SessionId?.ToString("N").ToUpperInvariant() ?? "未接続"}\r\nエラー分類 / 終了理由: {GenesisGuiNextPublishingPresentation.ErrorText(status)}";
        if (!string.IsNullOrEmpty(commandFeedback)) diagnostic += "\r\n" + commandFeedback;
        if (!string.IsNullOrEmpty(connectionFeedback)) diagnostic += "\r\n" + connectionFeedback;
        return new(basic, options, diagnostic);
    }

    public override string ToString() => "Publishing GUI summary [redacted]";

    private static string SafeContact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未設定";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "設定あり（配信設定で確認）";
        // This summary is routinely visible. Credentials, query tokens and fragments stay in the explicit editor.
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped) +
            (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 ? "（一部省略）" : "");
    }
}

namespace PeerCast.Nova.App;

/// <summary>
/// Truthful details shown for the currently selected native row. Values are
/// copied only from that row's display model; this projection never resolves a
/// name against the catalog or playback state.
/// </summary>
internal sealed record GenesisChannelDetailsPresentation(
    bool IsEmpty,
    string EmptyStateText,
    string Name,
    string Description,
    string Listeners,
    string Relays,
    string Bitrate,
    string Format,
    string Uptime,
    string YellowPage,
    string ChannelId,
    string Source)
{
    internal static GenesisChannelDetailsPresentation Empty { get; } = new(
        IsEmpty: true,
        EmptyStateText: "チャンネルを選択してください",
        Name: string.Empty,
        Description: string.Empty,
        Listeners: string.Empty,
        Relays: string.Empty,
        Bitrate: string.Empty,
        Format: string.Empty,
        Uptime: string.Empty,
        YellowPage: string.Empty,
        ChannelId: string.Empty,
        Source: string.Empty);

    public string MetadataText
    {
        get
        {
            var fields = new List<string>();
            if (!string.IsNullOrWhiteSpace(Listeners) ||
                !string.IsNullOrWhiteSpace(Relays))
            {
                fields.Add($"リスナー: {FormatUnknown(Listeners)}  リレー: {FormatUnknown(Relays)}");
            }

            AddField(fields, "ビットレート", Bitrate);
            AddField(fields, "形式", Format);
            AddField(fields, "配信時間", Uptime);
            AddField(fields, "YP", YellowPage);
            return string.Join("    ", fields);
        }
    }

    // Same projection values, arranged for the Modern Channels metadata column.
    public string MetadataLines => IsEmpty ? string.Empty :
        $"リスナー / リレー: {FormatUnknown(Listeners)} / {FormatUnknown(Relays)}\r\nビットレート: {FormatUnknown(Bitrate)}\r\n形式: {FormatUnknown(Format)}\r\n配信時間: {FormatUnknown(Uptime)}\r\nYP: {FormatUnknown(YellowPage)}";

    public string IdentityText
    {
        get
        {
            var fields = new List<string>();
            AddField(fields, "ChannelId", ChannelId);
            AddField(fields, "Source", Source);
            return string.Join("    ", fields);
        }
    }

    public string CopyText
    {
        get
        {
            if (IsEmpty)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            AddField(lines, "チャンネル名", Name);
            AddField(lines, "詳細", Description);
            AddField(lines, "リスナー", Listeners);
            AddField(lines, "リレー", Relays);
            AddField(lines, "ビットレート", Bitrate);
            AddField(lines, "形式", Format);
            AddField(lines, "配信時間", Uptime);
            AddField(lines, "YP", YellowPage);
            AddField(lines, "ChannelId", ChannelId);
            AddField(lines, "Source", Source);
            return string.Join(Environment.NewLine, lines);
        }
    }

    private static string FormatUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static void AddField(List<string> fields, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add($"{label}: {value}");
        }
    }
}

/// <summary>
/// One shared projection for all GUI Next native tabs. The selected display
/// row is authoritative even when it is intentionally non-interactive, as in
/// Playing and offline Favorites.
/// </summary>
internal static class GenesisGuiNextChannelDetailsProjection
{
    public static GenesisChannelDetailsPresentation FromSelection(
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows,
        int selectedRow)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return selectedRow < 0 || selectedRow >= rows.Count
            ? GenesisChannelDetailsPresentation.Empty
            : FromChannel(rows[selectedRow].DisplayChannel);
    }

    public static GenesisChannelDetailsPresentation FromChannel(GenesisChannelItem? channel)
    {
        if (channel is null)
        {
            return GenesisChannelDetailsPresentation.Empty;
        }

        return new(
            IsEmpty: false,
            EmptyStateText: string.Empty,
            Name: channel.Name ?? string.Empty,
            Description: channel.DetailsText,
            Listeners: channel.Listeners?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty,
            Relays: channel.Relays?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty,
            Bitrate: channel.BitrateKbps is { } bitrate
                ? $"{bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture)} kbps"
                : string.Empty,
            Format: channel.TypeText,
            Uptime: channel.UptimeText,
            YellowPage: channel.YellowPageText,
            ChannelId: channel.ChannelId ?? string.Empty,
            Source: channel.Source ?? string.Empty);
    }
}

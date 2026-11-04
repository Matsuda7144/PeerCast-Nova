using System.Globalization;

namespace PeerCast.Nova.App;

public enum GenesisChannelColumn
{
    ChannelName,
    Details,
    ListenerRelay,
    Bitrate,
    Uptime,
    Type,
    YellowPage,
}

public enum GenesisChannelColumnAlignment
{
    Left,
    Right,
}

public sealed record GenesisChannelListColumn(
    GenesisChannelColumn Column,
    string Header,
    int PreferredWidth,
    GenesisChannelColumnAlignment Alignment);

/// <summary>
/// Default Genesis channel-list presentation. The order intentionally follows
/// the compact PeCaRecorder-style layout familiar to existing PeerCast users.
/// </summary>
public static class GenesisChannelListPresentation
{
    public static IReadOnlyList<GenesisChannelListColumn> Columns { get; } =
        Array.AsReadOnly(
            new GenesisChannelListColumn[]
            {
                new(
                    GenesisChannelColumn.ChannelName,
                    "チャンネル名",
                    150,
                    GenesisChannelColumnAlignment.Left),
                new(
                    GenesisChannelColumn.Details,
                    "[ジャンル - 詳細] コメント アーティスト/タイトル/アルバム",
                    520,
                    GenesisChannelColumnAlignment.Left),
                new(
                    GenesisChannelColumn.ListenerRelay,
                    "リスナー/リレー",
                    105,
                    GenesisChannelColumnAlignment.Right),
                new(
                    GenesisChannelColumn.Bitrate,
                    "ビットレート",
                    85,
                    GenesisChannelColumnAlignment.Right),
                new(
                    GenesisChannelColumn.Uptime,
                    "配信時間",
                    75,
                    GenesisChannelColumnAlignment.Right),
                new(
                    GenesisChannelColumn.Type,
                    "タイプ",
                    60,
                    GenesisChannelColumnAlignment.Left),
                new(
                    GenesisChannelColumn.YellowPage,
                    "YP",
                    70,
                    GenesisChannelColumnAlignment.Left),
            });

    internal static string[] CreateRowValues(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return
        [
            channel.Name,
            channel.DetailsText,
            channel.ListenerRelayText,
            channel.BitrateText,
            channel.UptimeText,
            channel.TypeText,
            channel.YellowPageText,
        ];
    }
}

public sealed record GenesisChannelItem(
    string Name,
    string Source,
    string? Description = null,
    int? Listeners = null,
    int? Relays = null,
    int? BitrateKbps = null,
    TimeSpan? Uptime = null,
    string? Type = null,
    string? ChannelId = null)
{
    public string? YellowPage { get; init; }
    public string? ContactUrl { get; init; }

    public string DetailsText => Description ?? string.Empty;

    public string ListenerRelayText => Listeners is null && Relays is null
        ? string.Empty
        : $"{FormatCount(Listeners)} / {FormatCount(Relays)}";

    public string BitrateText => BitrateKbps?.ToString(CultureInfo.InvariantCulture) ??
        string.Empty;

    public string UptimeText
    {
        get
        {
            if (Uptime is not { } uptime)
            {
                return string.Empty;
            }

            var totalHours = (long)Math.Floor(uptime.TotalHours);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{totalHours:00}:{uptime.Minutes:00}");
        }
    }

    public string TypeText => Type ?? string.Empty;

    public string YellowPageText => YellowPage ?? string.Empty;

    private static string FormatCount(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}

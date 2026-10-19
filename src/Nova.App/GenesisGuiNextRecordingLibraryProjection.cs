using System.Globalization;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal sealed record GenesisRecordingLibraryDetails(
    string Title, string Description, string Metadata, string Identity,
    IReadOnlyList<RecordingSessionTerminalFact> HistoryFacts);

internal sealed record GenesisGuiNextRecordingLibraryRow(
    GenesisRecordingLibraryEntry Entry, string Channel, string Availability,
    string Container, string Size, string Modified, string Relation,
    GenesisRecordingLibraryDetails Details)
{
    public string Path => Entry.Path;
}

internal static class GenesisRecordingLibraryListPresentation
{
    public static IReadOnlyList<GenesisNativeListColumnPresentation> Columns { get; } =
        Array.AsReadOnly(new GenesisNativeListColumnPresentation[]
        {
            new("状態", 110, GenesisChannelColumnAlignment.Left),
            new("チャンネル", 190, GenesisChannelColumnAlignment.Left),
            new("ファイル", 270, GenesisChannelColumnAlignment.Left),
            new("種類", 130, GenesisChannelColumnAlignment.Left),
            new("サイズ", 120, GenesisChannelColumnAlignment.Right),
            new("更新日時", 180, GenesisChannelColumnAlignment.Left),
            new("関連", 84, GenesisChannelColumnAlignment.Left),
        });

    public static string[] CreateRowValues(GenesisGuiNextRecordingLibraryRow row) =>
        [row.Availability, row.Channel, row.Entry.Filename, row.Container, row.Size, row.Modified, row.Relation];
}

/// <summary>Pure presentation of current metadata and separately proven historical facts.</summary>
internal static class GenesisGuiNextRecordingLibraryProjection
{
    public const string NotObservedText = "録画ライブラリはまだ確認していません";
    public const string RefreshingText = "録画ライブラリを更新中…";
    public const string RefreshFailedText = "録画ライブラリを更新できませんでした";
    public const string LengthMismatchText = "現在のファイルサイズは録画時の記録と異なります";
    public const string MultipleHistoryText = "複数の録画履歴がこのパスを参照しています";

    public static IReadOnlyList<GenesisGuiNextRecordingLibraryRow> CreateRows(
        GenesisRecordingLibrarySnapshot snapshot) =>
        Array.AsReadOnly(snapshot.Entries.Select(CreateRow).ToArray());

    public static GenesisGuiNextRecordingLibraryRow CreateRow(GenesisRecordingLibraryEntry entry)
    {
        var facts = entry.HistoryFacts;
        var historicalNames = facts.Select(x => x.ChannelName).Distinct(StringComparer.Ordinal).ToArray();
        var channel = historicalNames.Length > 1 ? "複数の履歴"
            : historicalNames.Length == 1 && !string.IsNullOrWhiteSpace(historicalNames[0]) ? historicalNames[0]!
            : entry.FilenameClaims is { } claims ? claims.SanitizedChannelNameClaim + "（ファイル名）" : "不明";
        var availability = entry.Availability switch
        {
            GenesisRecordingLibraryAvailability.Available => "利用可能",
            GenesisRecordingLibraryAvailability.Missing => "見つかりません",
            _ => "確認できません",
        };
        var container = entry.Container switch
        {
            GenesisRecordingLibraryContainer.FlvCandidate => "FLV候補",
            GenesisRecordingLibraryContainer.UnknownContainerCandidate => "不明（.bin）",
            _ => "未対応形式",
        };
        var relation = facts.Count > 0 ? "履歴あり" : "履歴なし";
        var metadata = entry.Availability == GenesisRecordingLibraryAvailability.Available ? entry.FileMetadata : null;
        var size = metadata is null ? "—" : Bytes(metadata.Length);
        var modified = metadata is null ? "—" : metadata.LastWriteTimeUtc.ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var description = new List<string> { availability, container, relation };
        var identity = new List<string> { "パス: " + entry.Path };
        var detailsMetadata = new List<string> { "現在のサイズ: " + size, "更新日時: " + modified };
        if (facts.Count > 0)
        {
            if (facts.Count > 1) description.Add(MultipleHistoryText);
            AddShared(identity, "SessionId（履歴）", facts.Select(x => x.Session.SessionId.Value.ToString("N")));
            AddShared(identity, "ChannelId（履歴）", facts.Select(x => x.Session.ChannelId.Value.ToString("N")));
            AddShared(identity, "RuleId（履歴）", facts.Select(x => x.Session.AutomaticRuleId?.ToString("D")));
            AddShared(detailsMetadata, "由来（履歴）", facts.Select(x => x.Session.Origin switch
            {
                RecordingSessionOrigin.Manual => "手動",
                RecordingSessionOrigin.AutomaticRule => "自動",
                _ => "不明",
            }));
            AddShared(detailsMetadata, "terminal Kind（履歴）", facts.Select(x => x.Kind.ToString()));
            AddShared(detailsMetadata, "録画時の記録サイズ", facts.Select(x =>
                x.WriteResult is { } result ? Bytes(result.BytesWritten) : null));
            AddShared(detailsMetadata, "writer completion（履歴）", facts.Select(x => x.WriteResult?.CompletionKind.ToString()));
        }
        else if (entry.FilenameClaims is { } filenameClaims)
        {
            identity.Add("ChannelId（ファイル名の未確認情報）: " + filenameClaims.ChannelIdClaim.ToString("N"));
            identity.Add("SessionId（ファイル名の未確認情報）: " + filenameClaims.SessionIdClaim.ToString("N"));
        }
        if (entry.LengthMismatch) description.Add(LengthMismatchText);
        var title = channel != "不明" ? channel : entry.Filename;
        return new(entry, channel, availability, container, size, modified, relation,
            new(title, string.Join(" · ", description), string.Join(" · ", detailsMetadata),
                string.Join(" · ", identity), facts));
    }

    public static string StatusText(GenesisRecordingLibrarySnapshot snapshot, bool refreshing = false, bool failed = false)
    {
        var parts = new List<string>();
        if (refreshing) parts.Add(RefreshingText);
        else if (failed) parts.Add(RefreshFailedText);
        parts.Add(snapshot.State == GenesisRecordingLibraryObservationState.NotObservedYet ? NotObservedText
            : snapshot.Entries.Count == 0 ? "録画ファイルは見つかりませんでした"
            : $"録画ファイルを {snapshot.Entries.Count.ToString(CultureInfo.InvariantCulture)} 件表示しています");
        if (snapshot.State == GenesisRecordingLibraryObservationState.Observed)
        {
            if (snapshot.Roots.Any(x => x.Status != GenesisRecordingLibraryAvailability.Available || x.UnavailableFileCount > 0) ||
                snapshot.Entries.Any(x => x.Availability == GenesisRecordingLibraryAvailability.Unavailable))
                parts.Add("一部の保存先またはファイルを確認できません");
            if (snapshot.HasTruncatedRoots || snapshot.IsSnapshotTruncated) parts.Add("一部のみ表示しています");
            if (snapshot.ExcludedHistoryPathCount > 0) parts.Add("一部の履歴パスはライブラリ対象外です");
            if (snapshot.HistoryHasGap) parts.Add("録画履歴に欠落があります");
        }
        return string.Join(" · ", parts);
    }

    private static string Bytes(long count) => count.ToString("N0", CultureInfo.InvariantCulture) + " B";

    private static void AddShared(List<string> parts, string label, IEnumerable<string?> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 1 && !string.IsNullOrWhiteSpace(distinct[0]))
            parts.Add(label + ": " + distinct[0]);
    }
}

using System.Globalization;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisGuiNextRecordingPageMode
{
    ActiveRecordings,
    Library,
    Rules,
    History = 3,
}

internal static class GenesisGuiNextRecordingPageModePresentation
{
    public const string ActiveRecordingsLabel = "録画中";
    public const string LibraryLabel = "ライブラリ";
    public const string RulesLabel = "ルール";
    public const string HistoryLabel = "履歴";

    public static IReadOnlyList<(GenesisGuiNextRecordingPageMode Mode, string Label)> Items { get; } =
        Array.AsReadOnly(
            new[]
            {
                (GenesisGuiNextRecordingPageMode.ActiveRecordings, ActiveRecordingsLabel),
                (GenesisGuiNextRecordingPageMode.Library, LibraryLabel),
                (GenesisGuiNextRecordingPageMode.Rules, RulesLabel),
                (GenesisGuiNextRecordingPageMode.History, HistoryLabel),
            });
}

internal sealed record GenesisGuiNextRecordingRuleRow(
    Guid RuleId,
    int PersistedIndex,
    string Name,
    bool IsEnabled,
    RecordingRuleAction Action,
    int Priority,
    string CriteriaSummary);

internal sealed record GenesisNativeListColumnPresentation(
    string Header,
    int PreferredWidth,
    GenesisChannelColumnAlignment Alignment);

internal static class GenesisRecordingRulesListPresentation
{
    public static IReadOnlyList<GenesisNativeListColumnPresentation> Columns { get; } =
        Array.AsReadOnly(
            new GenesisNativeListColumnPresentation[]
            {
                new("状態", 64, GenesisChannelColumnAlignment.Left),
                new("ルール名", 190, GenesisChannelColumnAlignment.Left),
                new("動作", 72, GenesisChannelColumnAlignment.Left),
                new("優先度", 76, GenesisChannelColumnAlignment.Right),
                new("保存順", 72, GenesisChannelColumnAlignment.Right),
                new("条件（すべて一致）", 640, GenesisChannelColumnAlignment.Left),
            });

    public static string[] CreateRowValues(GenesisGuiNextRecordingRuleRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            row.IsEnabled ? "有効" : "無効",
            row.Name,
            GenesisGuiNextRecordingRuleProjection.ActionLabel(row.Action),
            row.Priority.ToString(CultureInfo.InvariantCulture),
            row.PersistedIndex.ToString(CultureInfo.InvariantCulture),
            row.CriteriaSummary,
        ];
    }
}

internal static class GenesisGuiNextRecordingRuleProjection
{
    public const string EmptyStateText = "録画ルールはありません";

    public static IReadOnlyList<GenesisGuiNextRecordingRuleRow> CreateRows(
        RecordingRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        return Array.AsReadOnly(
            ruleSet.Rules
                .Select(static (rule, index) => new GenesisGuiNextRecordingRuleRow(
                    rule.Id,
                    index + 1,
                    rule.Name,
                    rule.IsEnabled,
                    rule.Action,
                    rule.Priority,
                    FormatCriteria(rule.Criteria)))
                .ToArray());
    }

    public static string FormatCriteria(RecordingRuleCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.IsMatchAll)
        {
            return "すべてのチャンネルに一致";
        }

        var parts = new List<string>();
        if (criteria.ChannelId is { } channelId)
        {
            parts.Add($"ChannelId: {channelId}");
        }

        AddText(parts, "名前", criteria.Name);
        AddText(parts, "Genre", criteria.Genre);
        AddText(parts, "説明", criteria.Description);
        AddText(parts, "ContentType", criteria.ContentType);
        AddText(parts, "StreamType", criteria.StreamType);
        AddText(parts, "Comment", criteria.Comment);

        if (criteria.MinimumBitrateKbps is { } minimum &&
            criteria.MaximumBitrateKbps is { } maximum)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"ビットレート: {minimum}–{maximum} kbps"));
        }
        else if (criteria.MinimumBitrateKbps is { } minimumOnly)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"ビットレート: {minimumOnly} kbps以上"));
        }
        else if (criteria.MaximumBitrateKbps is { } maximumOnly)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"ビットレート: {maximumOnly} kbps以下"));
        }

        return parts.Count == 0
            ? "条件を表示できません"
            : $"すべての条件に一致: {string.Join(" / ", parts)}";
    }

    internal static string ActionLabel(RecordingRuleAction action) => action switch
    {
        RecordingRuleAction.Record => "録画",
        RecordingRuleAction.Ignore => "除外",
        _ => "不明",
    };

    internal static string StatusText(
        GenesisRecordingRuleSnapshotStatus status,
        GenesisRecordingRuleRepositoryFailureKind failureKind,
        int ruleCount) => status switch
        {
            GenesisRecordingRuleSnapshotStatus.Available when ruleCount == 0 => EmptyStateText,
            GenesisRecordingRuleSnapshotStatus.Available =>
                $"保存順に {ruleCount.ToString(CultureInfo.InvariantCulture)} 件表示しています。各条件はすべて一致が必要です。",
            GenesisRecordingRuleSnapshotStatus.MissingFile => EmptyStateText,
            GenesisRecordingRuleSnapshotStatus.Invalid =>
                "録画ルールを読み込めません（ファイル形式が不正です）",
            GenesisRecordingRuleSnapshotStatus.UnsupportedVersion =>
                "録画ルールを読み込めません（未対応の形式です）",
            GenesisRecordingRuleSnapshotStatus.Unavailable => failureKind switch
            {
                GenesisRecordingRuleRepositoryFailureKind.Permission =>
                    "録画ルールを読み込めません（アクセスが許可されていません）",
                GenesisRecordingRuleRepositoryFailureKind.NoPath =>
                    "録画ルールの保存先を利用できません",
                _ => "録画ルールを読み込めません（ファイルを確認できません）",
            },
            GenesisRecordingRuleSnapshotStatus.Cancelled =>
                "録画ルールの読み込みをキャンセルしました",
            _ => "録画ルールを確認できません",
        };

    private static void AddText(
        List<string> parts,
        string label,
        RecordingTextPattern? pattern)
    {
        if (pattern is null)
        {
            return;
        }

        var mode = pattern.Mode switch
        {
            RecordingTextMatchMode.Exact => "完全一致",
            RecordingTextMatchMode.Contains => "一部一致",
            RecordingTextMatchMode.Regex => "正規表現",
            _ => "不明な形式",
        };
        parts.Add($"{label}: {Quote(pattern.Value)}（{mode}）");
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";
}

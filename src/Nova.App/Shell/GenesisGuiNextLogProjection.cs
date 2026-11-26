using System.Globalization;

namespace PeerCast.Nova.App;

internal enum GenesisGuiNextLogKind { Information, Warning, Error }

internal sealed record GenesisGuiNextLogRow(DateTimeOffset Timestamp, GenesisGuiNextLogKind Kind, string Content)
{
    public string KindLabel => Kind switch
    {
        GenesisGuiNextLogKind.Error => "エラー",
        GenesisGuiNextLogKind.Warning => "警告",
        _ => "情報",
    };
}

/// <summary>
/// Read-only projection of existing refresh facts, not an event collector.
/// Only timestamps and typed outcomes cross this boundary; no URLs, channel
/// metadata, exception text, or arbitrary diagnostic strings are accepted.
/// </summary>
internal static class GenesisGuiNextLogProjection
{
    internal const int MaximumRows = 2;
    internal const string ScopeText = "YP更新の最新診断のみ · 読み取り専用 · 最大2件（履歴収集なし）";
    internal const string EmptyText = "表示できるログはありません。";
    internal static readonly IReadOnlyList<GenesisNativeListColumnPresentation> Columns = Array.AsReadOnly(
        new GenesisNativeListColumnPresentation[] { new("日時", 210, GenesisChannelColumnAlignment.Left), new("種類", 90, GenesisChannelColumnAlignment.Left), new("内容", 580, GenesisChannelColumnAlignment.Left) });

    public static IReadOnlyList<GenesisGuiNextLogRow> Create(GenesisChannelCatalogRefreshDiagnostics diagnostics, bool isRefreshing = false)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        List<GenesisGuiNextLogRow> rows = [];
        // LastResult is retained while LastAttemptedAt advances at refresh start.
        // Do not pair that old outcome with the new attempt's timestamp.
        if (!isRefreshing && diagnostics.LastAttemptedAt is { } attempted && diagnostics.LastResult is { } result)
        {
            var row = result switch
            {
                GenesisChannelCatalogRefreshResult.Completed => new GenesisGuiNextLogRow(
                    diagnostics.LastSuccessfulAt ?? attempted, GenesisGuiNextLogKind.Information, "YP一覧の更新が完了しました。"),
                GenesisChannelCatalogRefreshResult.Cancelled => new GenesisGuiNextLogRow(
                    attempted, GenesisGuiNextLogKind.Warning, "YP一覧の更新がキャンセルされました（試行開始日時）。"),
                GenesisChannelCatalogRefreshResult.Faulted => new GenesisGuiNextLogRow(
                    attempted, GenesisGuiNextLogKind.Error, "YP一覧の更新に失敗しました（試行開始日時）。"),
                _ => null,
            };
            if (row is not null) rows.Add(row);
        }
        if (diagnostics.LastSuccessfulAt is { } successful &&
            (isRefreshing || diagnostics.LastResult != GenesisChannelCatalogRefreshResult.Completed))
            rows.Add(new(successful, GenesisGuiNextLogKind.Information, "YP一覧の最終成功日時です。"));
        return Array.AsReadOnly(rows.OrderByDescending(row => row.Timestamp).Take(MaximumRows).ToArray());
    }

    internal static string[] Values(GenesisGuiNextLogRow row) =>
        [row.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), row.KindLabel, row.Content];

    // COLORREF. Severity is also spelled out for accessibility and monochrome displays.
    internal static int TextColor(GenesisGuiNextLogKind kind) => kind switch
    {
        GenesisGuiNextLogKind.Error => 0x008080FF,
        GenesisGuiNextLogKind.Warning => 0x0080C8FF,
        _ => 0x00F7F4F2,
    };
}

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private IReadOnlyList<GenesisGuiNextLogRow> _logRows = Array.AsReadOnly(Array.Empty<GenesisGuiNextLogRow>());
    public IReadOnlyList<GenesisGuiNextLogRow> LogRows => _logRows;

    internal void UpdateLogDiagnostics(GenesisChannelCatalogRefreshDiagnostics diagnostics, bool isRefreshing = false)
    {
        var rows = GenesisGuiNextLogProjection.Create(diagnostics, isRefreshing);
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || _logRows.SequenceEqual(rows)) return;
            _logRows = rows;
        }
        OnPropertyChanged(nameof(LogRows));
    }
}

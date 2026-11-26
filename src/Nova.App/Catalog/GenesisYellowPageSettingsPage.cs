namespace PeerCast.Nova.App;

// UI selection/draft only. Mutations and rollback belong to the existing model.
internal sealed class GenesisYellowPageSettingsPage(GenesisYellowPageSettingsModel model)
{
    private GenesisYellowPageDefinition? _baseline;
    internal bool IsOpen { get; private set; }
    internal GenesisYellowPageSettingsDraft Draft { get; private set; } = Empty();
    internal string? SelectedId => _baseline?.Id;
    internal bool Conflict { get; private set; }
    internal bool IsError { get; private set; }
    internal string Message { get; private set; } = string.Empty;
    internal IReadOnlyList<GenesisYellowPageDefinition> Entries => model.Entries;
    internal void Open() { IsOpen = true; Select(null); }
    internal void Close() { IsOpen = false; _baseline = null; Draft = Empty(); }
    internal void Edit(GenesisYellowPageSettingsDraft draft) { if (IsOpen) Draft = draft; }
    internal void Select(string? id)
    {
        if (!IsOpen) return;
        _baseline = Entries.FirstOrDefault(e => e.Id == id);
        Draft = _baseline is { } e ? new(e.Id, e.Name, e.IndexUri.OriginalString, e.FallbackTrackerUri?.OriginalString, e.IsEnabled, e.Namespace) : Empty();
        Conflict = IsError = false;
        Message = "操作ごとに保存します。未保存入力は行・カテゴリ移動で破棄します。";
    }
    internal void Refresh()
    {
        if (!IsOpen || _baseline is null || Entries.FirstOrDefault(e => e.Id == _baseline.Id) == _baseline) return;
        Conflict = IsError = true;
        Message = "従来の設定で対象YPが変わりました。キャンセルして再読込してください。";
    }
    internal bool Save() => Apply(() => _baseline is null ? model.Add(Draft) : model.Edit(_baseline.Id, Draft), "YPを保存しました。");
    internal bool Toggle() => _baseline is not null && Apply(() => model.SetEnabled(_baseline.Id, !_baseline.IsEnabled), "YPの有効状態を保存しました。");
    internal bool Remove(GenesisYellowPageDefinition confirmed) => _baseline == confirmed && Apply(() => model.Remove(confirmed.Id), "YPを削除しました。", removed: true);
    internal GenesisYellowPageDefinition? DeletionTarget { get { Refresh(); return IsOpen && !Conflict ? _baseline : null; } }
    internal void Cancel() => Select(SelectedId);
    private bool Apply(Func<GenesisYellowPageSettingsMutationResult> mutation, string success, bool removed = false)
    {
        Refresh();
        if (!IsOpen || Conflict) return false;
        var result = mutation();
        IsError = !result.Succeeded;
        Message = result.Succeeded ? success : result.ErrorMessage ?? "YP設定を保存できませんでした。";
        if (!result.Succeeded) return false;
        var id = removed ? null : _baseline?.Id ?? Draft.Id?.Trim();
        var message = Message;
        Select(id);
        Message = message;
        return true;
    }
    private static GenesisYellowPageSettingsDraft Empty() => new("", "", "", "", false, "");
}

namespace PeerCast.Nova.App;

// UI-owned draft. Application-owned settings retain persistence and shutdown ownership.
internal sealed class GenesisConnectionSettingsPage(GenesisGuiNextConnectionSettings settings)
{
    private GenesisGuiNextConnectionPreferences _baseline = settings.Saved;
    private Task<GenesisGuiNextConnectionSaveResult>? _save;
    internal bool IsOpen { get; private set; }
    internal bool IsSaving => _save is not null;
    internal bool Conflict { get; private set; }
    internal bool IsError { get; private set; }
    internal string Message { get; private set; } = string.Empty;
    internal GenesisGuiNextConnectionInput Input { get; private set; } = GenesisGuiNextConnectionInput.From(settings.Saved);
    internal bool IsDirty => Input != GenesisGuiNextConnectionInput.From(_baseline);
    internal GenesisGuiNextConnectionPreferences Startup => settings.Startup;
    internal GenesisGuiNextConnectionPreferences Saved => settings.Saved;

    internal void Open() { IsOpen = true; Reload(); }
    internal void Close() { IsOpen = false; }
    internal void Edit(GenesisGuiNextConnectionInput input) { if (IsOpen && !IsSaving) Input = input; }
    internal void Reload()
    {
        if (IsSaving) return;
        _baseline = settings.Saved;
        Input = GenesisGuiNextConnectionInput.From(_baseline);
        Conflict = false;
        IsError = settings.LoadFailed;
        Message = IsError ? GenesisGuiNextConnectionSettings.LoadFailureMessage : "未保存の変更は、移動またはキャンセルで破棄されます。";
    }
    internal void Refresh()
    {
        if (!IsOpen || IsSaving || settings.Saved == _baseline) return;
        if (!IsDirty) Reload();
        else { Conflict = IsError = true; Message = "従来の設定で保存値が変わりました。キャンセルして再読込してください。"; }
    }
    internal Task<GenesisGuiNextConnectionSaveResult>? BeginSave()
    {
        Refresh();
        if (!IsOpen || IsSaving || Conflict) return null;
        var input = Input;
        var baseline = _baseline;
        Message = "保存中…（別カテゴリへ移動しても保存は継続します）";
        IsError = false;
        // SaveAsync may complete its synchronous store work before the first await.
        return _save = Task.Run(async () => await settings.SaveAsync(input, expectedSaved: baseline).ConfigureAwait(false));
    }
    internal void CompleteSave(Task<GenesisGuiNextConnectionSaveResult> operation)
    {
        if (!ReferenceEquals(_save, operation) || !operation.IsCompleted) return;
        _save = null;
        if (!IsOpen) return;
        if (!operation.IsCompletedSuccessfully) { IsError = true; Message = "設定を保存できませんでした。保存値を確認してください。"; return; }
        var result = operation.Result;
        IsError = !result.Saved;
        Message = result.Message;
        if (result.Saved) { _baseline = settings.Saved; Input = GenesisGuiNextConnectionInput.From(_baseline); }
        else Refresh();
    }
}

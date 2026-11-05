namespace PeerCast.Nova.App;

internal enum GenesisIntegratedSettingsCategory { General, Playback, Recording, Notifications, Connection, YellowPages }

// A draft for the two existing path editors, not a settings store or a global save transaction.
internal sealed class GenesisPathSettingsPage(GenesisGuiNextPlaybackSettings? playback, GenesisRecordingOutputDirectorySettings? recording)
{
    private bool _released;
    private string _baseline = string.Empty;
    internal GenesisIntegratedSettingsCategory? Category { get; private set; }
    internal string Draft { get; private set; } = string.Empty;
    internal string Message { get; private set; } = string.Empty;
    internal bool IsError { get; private set; }
    internal bool Conflict { get; private set; }
    internal bool IsDirty => Draft != _baseline;
    internal string Saved => Category == GenesisIntegratedSettingsCategory.Recording ? recording?.Presentation.ConfiguredDirectory ?? string.Empty : playback?.PcrPlayerExecutablePath ?? string.Empty;
    internal string? RuntimeDirectory => recording?.CurrentRuntimeDirectory;
    internal string? EffectiveDirectory => recording?.Presentation.EffectiveDirectory;
    internal bool Available(GenesisIntegratedSettingsCategory category) => category switch
    {
        GenesisIntegratedSettingsCategory.Playback => playback is not null,
        GenesisIntegratedSettingsCategory.Recording => recording is not null,
        _ => false,
    };

    internal bool Open(GenesisIntegratedSettingsCategory category)
    {
        if (_released || !Available(category)) return false;
        Category = category;
        Reload();
        var error = category == GenesisIntegratedSettingsCategory.Recording ? recording?.Presentation.Error : null;
        IsError = error is not null;
        Message = error ?? "未保存の入力は、キャンセルやカテゴリ移動で破棄します。";
        return true;
    }

    private void Reload() { Draft = _baseline = Saved; Conflict = IsError = false; }

    internal void Refresh()
    {
        if (_released || Category is null || Saved == _baseline) return;
        if (IsDirty)
        {
            Conflict = IsError = true;
            Message = "従来の設定で保存値が変わりました。キャンセルして最新値を読み込んでください。";
        }
        else { Reload(); Message = "保存済みの値を読み直しました。"; }
    }

    internal void Edit(string value)
    {
        if (_released || Category is null) return;
        Refresh();
        Draft = value;
        if (!Conflict)
        {
            IsError = false;
            Message = IsDirty ? "未保存の変更があります。カテゴリ移動では保存されません。" : "保存済みの値を表示しています。";
        }
    }

    internal bool Save(bool reset = false)
    {
        if (_released || Category is null) return false;
        Refresh();
        if (Conflict) return false;
        bool saved;
        if (Category == GenesisIntegratedSettingsCategory.Playback)
        {
            var requested = reset ? null : Draft;
            var result = playback!.SetPcrPlayerExecutablePath(requested);
            saved = result is GenesisGuiNextPlaybackSettingsUpdateResult.Saved or GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged;
            Message = GenesisGuiNextPlaybackSettingsWindow.GetPcrPlayerSaveFeedback(result, requested);
        }
        else
        {
            var result = reset ? recording!.ResetToDefault() : recording!.Save(Draft);
            saved = result == GenesisRecordingOutputDirectorySaveResult.Saved;
            Message = saved ? "保存しました。" + GenesisRecordingOutputDirectorySettings.NextStartNotice : recording.Presentation.Error!;
        }
        if (saved) Reload();
        IsError = !saved;
        return saved;
    }

    internal void ReportInputError(string message) { if (!_released && Category is not null) { Message = message; IsError = true; } }
    internal void Cancel() { if (!_released && Category is not null) { Reload(); Message = "未保存の入力を破棄しました。"; } }
    internal void Close() { Category = null; Draft = _baseline = string.Empty; Conflict = IsError = false; }
    internal void Release() { Close(); _released = true; }
}

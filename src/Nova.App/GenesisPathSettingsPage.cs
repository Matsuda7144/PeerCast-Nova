namespace PeerCast.Nova.App;

internal enum GenesisIntegratedSettingsCategory { General, Playback, Recording, Notifications, Connection, YellowPages }

// Draft state for the integrated playback and recording settings.
internal sealed class GenesisPathSettingsPage(GenesisGuiNextPlaybackSettings? playback, GenesisRecordingOutputDirectorySettings? recording)
{
    private bool _released;
    private string _recordingBaseline = string.Empty;
    private string _recordingDraft = string.Empty;
    private GenesisGuiNextPlaybackPreferences _playbackBaseline = GenesisGuiNextPlaybackPreferences.Default;
    private GenesisGuiNextPlaybackPreferences _playbackDraft = GenesisGuiNextPlaybackPreferences.Default;

    internal GenesisIntegratedSettingsCategory? Category { get; private set; }
    internal string Message { get; private set; } = string.Empty;
    internal bool IsError { get; private set; }
    internal bool Conflict { get; private set; }
    internal bool IsDirty => IsPlayback ? _playbackDraft != _playbackBaseline : _recordingDraft != _recordingBaseline;
    internal GenesisPlaybackPlayerMode PlayerMode => _playbackDraft.PlayerMode;
    internal bool LowLatencyInternalPlayback => _playbackDraft.LowLatencyInternalPlayback;
    internal bool CanEditLowLatency => IsPlayback && PlayerMode == GenesisPlaybackPlayerMode.Internal;
    internal string ExternalPlayerArguments => _playbackDraft.ExternalPlayerArguments ?? string.Empty;
    internal bool CanEditExternalPlayerArguments => IsPlayback && PlayerMode == GenesisPlaybackPlayerMode.External;
    internal bool CanSetupLibMpv => IsPlayback && PlayerMode == GenesisPlaybackPlayerMode.Internal;
    internal GenesisPlaybackPlayerMode SavedPlayerMode => playback?.Preferences.PlayerMode ?? GenesisPlaybackPlayerMode.External;
    internal string Draft => IsPlayback ? PathForMode(_playbackDraft) : _recordingDraft;
    internal string Saved => IsPlayback ? PathForMode(playback!.Preferences) : recording?.Presentation.ConfiguredDirectory ?? string.Empty;
    internal bool CanReset => Category == GenesisIntegratedSettingsCategory.Recording ||
        IsPlayback && PlayerMode == GenesisPlaybackPlayerMode.External;
    internal string? RuntimeDirectory => recording?.CurrentRuntimeDirectory;
    internal string? EffectiveDirectory => recording?.Presentation.EffectiveDirectory;

    private bool IsPlayback => Category == GenesisIntegratedSettingsCategory.Playback;

    internal static string PlayerLabel(GenesisPlaybackPlayerMode mode) =>
        mode == GenesisPlaybackPlayerMode.Internal ? "内蔵プレイヤー" : "外部プレイヤー";

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

    private string PathForMode(GenesisGuiNextPlaybackPreferences preferences) =>
        (PlayerMode == GenesisPlaybackPlayerMode.Internal
            ? preferences.LibMpvLibraryPath : preferences.PcrPlayerExecutablePath) ?? string.Empty;

    private void Reload()
    {
        if (IsPlayback) _playbackDraft = _playbackBaseline = playback!.Preferences;
        else _recordingDraft = _recordingBaseline = Saved;
        Conflict = IsError = false;
    }

    internal void Refresh()
    {
        if (_released || Category is null ||
            (IsPlayback ? playback!.Preferences == _playbackBaseline : Saved == _recordingBaseline)) return;
        if (IsDirty)
        {
            ReportConflict();
        }
        else { Reload(); Message = "保存済みの値を読み直しました。"; }
    }

    internal void SelectPlayer(GenesisPlaybackPlayerMode mode)
    {
        if (_released || !IsPlayback) return;
        Refresh();
        if (PlayerMode == mode) return;
        _playbackDraft = _playbackDraft with { PlayerMode = mode };
        UpdateEditMessage();
    }

    internal void SetLowLatencyInternalPlayback(bool enabled)
    {
        if (_released || !CanEditLowLatency) return;
        Refresh();
        if (!CanEditLowLatency || LowLatencyInternalPlayback == enabled) return;
        _playbackDraft = _playbackDraft with { LowLatencyInternalPlayback = enabled };
        UpdateEditMessage();
    }

    internal void EditExternalPlayerArguments(string value)
    {
        if (_released || !CanEditExternalPlayerArguments) return;
        Refresh();
        if (!CanEditExternalPlayerArguments) return;
        var args = value.Length == 0 ? null : value;
        if (_playbackDraft.ExternalPlayerArguments == args) return;
        _playbackDraft = _playbackDraft with { ExternalPlayerArguments = args };
        UpdateEditMessage();
    }

    internal void Edit(string value)
    {
        if (_released || Category is null) return;
        Refresh();
        if (IsPlayback)
        {
            var path = value.Length == 0 ? null : value;
            _playbackDraft = PlayerMode == GenesisPlaybackPlayerMode.Internal
                ? _playbackDraft with { LibMpvLibraryPath = path }
                : _playbackDraft with { PcrPlayerExecutablePath = path };
        }
        else _recordingDraft = value;
        UpdateEditMessage();
    }

    private void UpdateEditMessage()
    {
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
        if (Conflict || reset && !CanReset) return false;
        if (IsPlayback) return SavePlayback(reset);

        var result = reset ? recording!.ResetToDefault() : recording!.Save(Draft);
        var saved = result == GenesisRecordingOutputDirectorySaveResult.Saved;
        Message = saved ? "保存しました。" + GenesisRecordingOutputDirectorySettings.NextStartNotice : recording.Presentation.Error!;
        if (saved) Reload();
        IsError = !saved;
        return saved;
    }

    private bool SavePlayback(bool reset)
    {
        var (result, invalidPathMode) = reset
            ? (playback!.SetPcrPlayerExecutablePath(null, _playbackBaseline), (GenesisPlaybackPlayerMode?)null)
            : playback!.Save(_playbackBaseline, _playbackDraft);
        if (result == GenesisGuiNextPlaybackSettingsUpdateResult.Conflict)
        {
            ReportConflict();
            return false;
        }

        var saved = result is GenesisGuiNextPlaybackSettingsUpdateResult.Saved or GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged;
        if (saved)
        {
            Reload();
            Message = reset ? "外部プレイヤーの実行ファイルを未設定に戻しました。" :
                "再生設定を保存しました。次の再生から使用します。";
        }
        else
        {
            Message = GenesisGuiNextPlaybackSettingsWindow.GetPlayerPathSaveFeedback(
                result, Draft, library: invalidPathMode == GenesisPlaybackPlayerMode.Internal);
            if (invalidPathMode is { } mode && mode != PlayerMode)
            {
                Message = $"{PlayerLabel(mode)}の設定を確認してください。{Message}";
            }
        }
        IsError = !saved;
        return saved;
    }

    private void ReportConflict()
    {
        Conflict = IsError = true;
        Message = "保存値が変わりました。キャンセルして最新値を読み込んでください。";
    }

    internal void ReportInputError(string message) { if (!_released && Category is not null) { Message = message; IsError = true; } }
    internal void ReportStatus(string message, bool isError) { if (!_released && Category is not null) { Message = message; IsError = isError; } }
    internal void Cancel() { if (!_released && Category is not null) { Reload(); Message = "未保存の入力を破棄しました。"; } }
    internal void Close()
    {
        Category = null;
        _recordingDraft = _recordingBaseline = string.Empty;
        _playbackDraft = _playbackBaseline = GenesisGuiNextPlaybackPreferences.Default;
        Conflict = IsError = false;
    }
    internal void Release() { Close(); _released = true; }
}

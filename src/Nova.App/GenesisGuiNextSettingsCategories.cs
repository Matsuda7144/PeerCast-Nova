using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const int PlaybackCategoryId = 1190, RecordingCategoryId = 1191, NotificationsCategoryId = 1192,
        NotifyNewId = 1193, NotifyFavoriteId = 1194, NotifySoundId = 1195, TestSoundId = 1196,
        SettingsPathId = 1197, SettingsBrowseId = 1198, SettingsResetId = 1199;

    private sealed partial class AppShellState
    {
        private GenesisIntegratedSettingsCategory? _settingsCategory;
        private long _settingsCategoryRevision;
        private GenesisPathSettingsPage? _pathSettings;
        private nint _playbackCategory, _recordingCategory, _notificationsCategory, _settingsPath, _settingsBrowse, _settingsReset;
        private nint _notifyNew, _notifyFavorite, _notifySound, _testSound;
        private bool _updatingSettingsPath, _notificationError;
        private string _notificationMessage = string.Empty;
        private nint[] AdditionalSettingsButtons => [_playbackCategory, _recordingCategory, _notificationsCategory, _settingsBrowse, _settingsReset, _testSound, .. NetworkSettingsButtons];
        private nint[] SettingsChecks => [_notifyNew, _notifyFavorite, _notifySound, _connectionIpv6, _connectionWan, _connectionAutoMapping, _ypEnabled];
        private bool PathCategory => _settingsCategory is GenesisIntegratedSettingsCategory.Playback or GenesisIntegratedSettingsCategory.Recording;
        private bool SettingsError => _settingsCategory == GenesisIntegratedSettingsCategory.General ? _generalSettings?.IsError == true :
            PathCategory ? _pathSettings?.IsError == true : NetworkCategory ? NetworkSettingsError : _notificationError;

        private void CreateSettingsCategories(nint instance)
        {
            nint Button(string text, int id) => CreateRecordingModeButton(text, _settingsPane, id, 0, instance);
            nint Check(string text, int id)
            {
                var control = CreateWindowExW(0, "BUTTON", text, WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 3,
                    0, 0, 1, 1, _settingsPane, (nint)id, instance, 0); // BS_AUTOCHECKBOX: native keyboard and accessibility.
                _ = SetWindowTheme(control, " ", " ");
                return control;
            }
            _playbackCategory = Button("再生", PlaybackCategoryId);
            _recordingCategory = Button("録画・保存先", RecordingCategoryId);
            _notificationsCategory = Button("通知", NotificationsCategoryId);
            _settingsBrowse = Button("参照…", SettingsBrowseId);
            _settingsReset = Button("未設定に戻す", SettingsResetId);
            _testSound = Button("通知音テスト", TestSoundId);
            _settingsPath = CreateWindowExW(0, "EDIT", string.Empty, WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 0x0080,
                0, 0, 1, 1, _settingsPane, (nint)SettingsPathId, instance, 0);
            _ = SendMessageW(_settingsPath, 0x00C5, GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity - 1, 0); // EM_SETLIMITTEXT
            _notifyNew = Check("新着チャンネル通知", NotifyNewId);
            _notifyFavorite = Check("お気に入りチャンネル通知", NotifyFavoriteId);
            _notifySound = Check("通知音を有効にする", NotifySoundId);
            _pathSettings = new(_viewModel.PlaybackSettings, _viewModel.RecordingOutputSettings);
            CreateNetworkSettingsCategories(instance);
        }

        private void SelectSettingsCategory(GenesisIntegratedSettingsCategory category)
        {
            if (_settingsCategory == category) return;
            if (category is GenesisIntegratedSettingsCategory.Playback or GenesisIntegratedSettingsCategory.Recording && _pathSettings?.Available(category) != true) return;
            if (category == GenesisIntegratedSettingsCategory.Notifications && _viewModel.NotificationSettings is null) return;
            if (category == GenesisIntegratedSettingsCategory.Connection && _connectionPage is null || category == GenesisIntegratedSettingsCategory.YellowPages && _ypPage is null) return;
            CloseNetworkSettingsPages();
            _generalSettings!.Close();
            _pathSettings!.Close();
            _settingsCategoryRevision++;
            _settingsCategory = category;
            if (category == GenesisIntegratedSettingsCategory.General) _ = _generalSettings.Open();
            else if (PathCategory) _ = _pathSettings.Open(category);
            else if (category == GenesisIntegratedSettingsCategory.Connection) _connectionPage!.Open();
            else if (category == GenesisIntegratedSettingsCategory.YellowPages) _ypPage!.Open();
            else { _notificationError = false; _notificationMessage = "変更は即時に保存・反映されます。保存・キャンセル操作はありません。"; }
            ShowSettingsCategory();
            RefreshIntegratedSettings();
            _ = SetFocus(NetworkCategory ? NetworkInitialFocus : PathCategory ? _settingsPath : category == GenesisIntegratedSettingsCategory.Notifications ? _notifyNew : _modernChoice);
        }

        private void ShowSettingsCategory()
        {
            var general = _settingsCategory == GenesisIntegratedSettingsCategory.General;
            var notification = _settingsCategory == GenesisIntegratedSettingsCategory.Notifications;
            void Show(nint control, bool visible) => _ = ShowWindow(control, visible ? ShowWindowShowNoActivate : ShowWindowHide);
            Show(_classicChoice, general); Show(_modernChoice, general);
            Show(_generalNotice, true); // YP hides this shared text; restore it for every other category.
            Show(_generalStatus, true);
            Show(_generalRuntime, !notification); Show(_generalConfigured, !notification);
            Show(_generalSave, !notification); Show(_generalCancel, !notification);
            Show(_settingsPath, PathCategory); Show(_settingsBrowse, PathCategory);
            Show(_settingsReset, PathCategory); Show(_testSound, notification);
            foreach (var control in new[] { _notifyNew, _notifyFavorite, _notifySound }) Show(control, notification);
            ShowNetworkSettingsCategory();
            _ = EnableWindow(_playbackCategory, _pathSettings?.Available(GenesisIntegratedSettingsCategory.Playback) == true);
            _ = EnableWindow(_recordingCategory, _pathSettings?.Available(GenesisIntegratedSettingsCategory.Recording) == true);
            _ = EnableWindow(_notificationsCategory, _viewModel.NotificationSettings is not null);
            if (GetClientRect(_settingsPane, out var bounds)) PlaceIntegratedSettings(LogicalWidth(bounds.Right), LogicalWidth(bounds.Bottom));
        }

        private bool ReadSettingsPath(out string text)
        {
            text = string.Empty;
            var length = GetSettingsTextLength(_settingsPath);
            if (length < 0 || length >= GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity)
            { _pathSettings!.ReportInputError("入力が長すぎます。保存値は変更していません。"); return false; }
            var buffer = new char[length + 1];
            var copied = GetWindowTextW(_settingsPath, buffer, buffer.Length);
            if (copied != length) { _pathSettings!.ReportInputError("入力を読み取れませんでした。保存値は変更していません。"); return false; }
            text = new string(buffer, 0, copied);
            return true;
        }

        private bool ProcessSettingsPathInput(int id, int code, nint control)
        {
            if (id != SettingsPathId || control != _settingsPath || code != 0x0300 || !SettingsPageOpen || !PathCategory) return false;
            if (!_updatingSettingsPath)
            {
                if (ReadSettingsPath(out var text)) _pathSettings!.Edit(text);
                RefreshIntegratedSettings();
            }
            return true;
        }

        private bool ProcessSettingsCategoryCommand(int id, nint control)
        {
            if (ProcessNetworkSettingsCommand(id, control)) return true;
            if (id == PlaybackCategoryId && control == _playbackCategory) SelectSettingsCategory(GenesisIntegratedSettingsCategory.Playback);
            else if (id == RecordingCategoryId && control == _recordingCategory) SelectSettingsCategory(GenesisIntegratedSettingsCategory.Recording);
            else if (id == NotificationsCategoryId && control == _notificationsCategory) SelectSettingsCategory(GenesisIntegratedSettingsCategory.Notifications);
            else if (id == SettingsBrowseId && control == _settingsBrowse && PathCategory)
            {
                if (!ReadSettingsPath(out var text)) { RefreshIntegratedSettings(); return true; }
                var category = _settingsCategory;
                var revision = _settingsCategoryRevision;
                // Browsing only modifies the current draft; persistence remains behind the Save button.
                var selectedPath = category == GenesisIntegratedSettingsCategory.Playback
                    ? GenesisGuiNextPlaybackSettingsWindow.BrowseForExecutable(_owner, text)
                    : default;
                var folder = category == GenesisIntegratedSettingsCategory.Recording
                    ? GenesisRecordingDirectoryPicker.Browse(_owner)
                    : default;
                if (SettingsPageOpen && category == _settingsCategory && revision == _settingsCategoryRevision)
                {
                    var path = category == GenesisIntegratedSettingsCategory.Playback ? selectedPath.SelectedPath : folder.SelectedPath;
                    var failed = category == GenesisIntegratedSettingsCategory.Playback ? selectedPath.Failed : folder.Failed;
                    if (path is not null) _pathSettings!.Edit(path);
                    else if (failed) _pathSettings!.ReportInputError("フォルダーまたはファイルを選択できませんでした。");
                    RefreshIntegratedSettings();
                }
            }
            else if (id == SettingsResetId && control == _settingsReset && PathCategory) { _ = _pathSettings!.Save(reset: true); RefreshIntegratedSettings(); }
            else if (_settingsCategory == GenesisIntegratedSettingsCategory.Notifications && _viewModel.NotificationSettings is { } notifications)
            {
                if (id == TestSoundId && control == _testSound)
                {
                    _ = RequestSettingsSoundAsync(notifications); // Existing processing and feedback own the result; no new sound engine or subscription.
                    _notificationMessage = "通知音テストを要求しました（自動通知音の設定は変更しません）。";
                    _notificationError = false;
                }
                else
                {
                    if (!(id == NotifyNewId && control == _notifyNew || id == NotifyFavoriteId && control == _notifyFavorite || id == NotifySoundId && control == _notifySound)) return false;
                    var value = SendMessageW(control, 0x00F0, 0, 0) == 1;
                    var result = id == NotifyNewId && control == _notifyNew ? notifications.SetNotifyNewChannels(value) :
                        id == NotifyFavoriteId && control == _notifyFavorite ? notifications.SetNotifyFavoriteChannels(value) :
                        id == NotifySoundId && control == _notifySound ? notifications.SetPlaySound(value) : (GenesisChannelNotificationSettingsMutationResult?)null;
                    if (result is null) return false;
                    _notificationError = result == GenesisChannelNotificationSettingsMutationResult.PersistenceFailed;
                    _notificationMessage = _notificationError ? "通知設定を保存できませんでした。既存値を保持しています。" : "通知設定を保存・反映しました。";
                }
                RefreshIntegratedSettings();
            }
            else return false;
            return true;
        }

        private void RefreshAdditionalSettings()
        {
            if (NetworkCategory) { RefreshNetworkSettings(); return; }
            if (PathCategory)
            {
                var page = _pathSettings!;
                page.Refresh();
                var recording = _settingsCategory == GenesisIntegratedSettingsCategory.Recording;
                _ = SetWindowTextW(_generalTitle, recording ? "録画・保存先設定" : "再生設定");
                _ = SetWindowTextW(_generalNotice, recording ? GenesisRecordingOutputDirectorySettings.NextStartNotice + " 既存writerの保存先は変わりません。\r\n「既定値に戻す」は即時保存します。フォルダーや録画ファイルは作成・移動しません。" :
                    "PCRPlayer64.exeのパスを設定します。保存した設定は次の再生から使用します。\r\n空欄・「未設定に戻す」では環境変数またはPATHを使用します。戻す操作は即時保存です。");
                _ = SetWindowTextW(_generalRuntime, recording ? "現在起動中: " + page.RuntimeDirectory : "保存値: " + (page.Saved.Length == 0 ? "未設定" : page.Saved));
                _ = SetWindowTextW(_generalConfigured, recording ? "次回起動用: " + page.EffectiveDirectory + (page.Saved.Length == 0 ? "（既定）" : string.Empty) : "PCRPlayer実行ファイルパス（未保存の入力はカテゴリ移動で破棄）");
                _updatingSettingsPath = true;
                try
                {
                    if (GetSettingsTextLength(_settingsPath) != page.Draft.Length || !ReadSettingsPath(out var existing) || existing != page.Draft)
                        _ = SetWindowTextW(_settingsPath, page.Draft);
                }
                finally { _updatingSettingsPath = false; }
                _ = SetWindowTextW(_settingsReset, recording ? "既定値に戻す" : "未設定に戻す");
                _ = SetWindowTextW(_generalStatus, page.Message);
                _ = EnableWindow(_generalSave, page.IsDirty && !page.Conflict);
                _ = EnableWindow(_generalCancel, page.IsDirty || page.Conflict || page.IsError);
                _ = EnableWindow(_settingsReset, !page.Conflict);
            }
            else if (_viewModel.NotificationSettings is { } notifications)
            {
                _ = SetWindowTextW(_generalTitle, "通知設定");
                _ = SetWindowTextW(_generalNotice, "変更は即時に保存・反映されます。このカテゴリに保存・キャンセル操作はありません。\r\n通知音テストは音の有効設定にかかわらず、明示的に要求した場合のみ実行します。");
                var current = notifications.Current;
                _ = SendMessageW(_notifyNew, 0x00F1, current.NotifyNewChannels ? 1u : 0u, 0);
                _ = SendMessageW(_notifyFavorite, 0x00F1, current.NotifyFavoriteChannels ? 1u : 0u, 0);
                _ = SendMessageW(_notifySound, 0x00F1, current.PlaySound ? 1u : 0u, 0);
                _ = SetWindowTextW(_generalStatus, _notificationMessage);
            }
        }

        private void PlaceSettingsCategory(GenesisGuiNextSettingsLayout layout)
        {
            void Place(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle b) => _ = MoveWindow(control, Scale(b.Left), Scale(b.Top), Scale(b.Width), Scale(b.Height), true);
            PlaceNetworkSettingsCategory(layout);
            Place(_playbackCategory, GenesisGuiNextSettingsLayout.PlaybackTab); Place(_recordingCategory, GenesisGuiNextSettingsLayout.RecordingTab);
            Place(_notificationsCategory, GenesisGuiNextSettingsLayout.NotificationsTab);
            Place(_settingsBrowse, layout.Browse); Place(_settingsReset, layout.Reset); Place(_testSound, layout.SoundTest);
            if (PathCategory)
            {
                Place(_generalNotice, layout.PathNotice); Place(_generalRuntime, layout.PathRuntime); Place(_generalConfigured, layout.PathSaved);
                Place(_settingsPath, layout.PathInput(true)); Place(_generalStatus, layout.PathStatus);
            }
            else if (_settingsCategory == GenesisIntegratedSettingsCategory.Notifications)
            {
                Place(_generalNotice, layout.PathNotice); Place(_generalStatus, layout.NotificationStatus);
            }
            for (var i = 0; i < 3; i++) Place(SettingsChecks[i], layout.NotificationCheck(i));
        }

        internal bool IsSettingsPane(nint window) => window == _settingsPane && _settingsPane != 0;
        internal void RefreshSettingsPathFocus(nint window)
        {
            if ((window == _settingsPath || NetworkSettingsInputs.Contains(window)) && SettingsPageOpen) _ = RedrawWindow(_settingsPane, 0, 0, 0x0001 | 0x0004);
        }
    }

    internal static async Task RequestSettingsSoundAsync(GenesisChannelNotificationSettings settings)
    {
        try { await settings.TestSoundAsync().ConfigureAwait(false); }
        catch { /* Existing notification feedback owns errors; no late callback to a closed settings page. */ }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetSettingsTextLength(nint window);
}

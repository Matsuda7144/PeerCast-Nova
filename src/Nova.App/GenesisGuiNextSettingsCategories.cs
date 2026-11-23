using System.Runtime.InteropServices;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const int PlaybackCategoryId = 1190, RecordingCategoryId = 1191, NotificationsCategoryId = 1192,
        NotifyNewId = 1193, NotifyFavoriteId = 1194, NotifySoundId = 1195, TestSoundId = 1196,
        SettingsPathId = 1197, SettingsBrowseId = 1198, SettingsResetId = 1199;
    private const int InternalPlayerChoiceId = 1235, ExternalPlayerChoiceId = 1236, LowLatencyPlaybackId = 1237, SetupLibMpvId = 1238;

    private sealed partial class AppShellState
    {
        private GenesisIntegratedSettingsCategory? _settingsCategory;
        private long _settingsCategoryRevision;
        private GenesisPathSettingsPage? _pathSettings;
        private nint _playbackCategory, _recordingCategory, _notificationsCategory, _settingsPath, _settingsBrowse, _settingsReset;
        private nint _internalPlayerChoice, _externalPlayerChoice, _lowLatencyPlayback, _setupLibMpv;
        private nint _notifyNew, _notifyFavorite, _notifySound, _testSound;
        private bool _updatingSettingsPath, _notificationError, _settingUpLibMpv;
        private string _notificationMessage = string.Empty;
        private nint[] AdditionalSettingsButtons => [_playbackCategory, _recordingCategory, _notificationsCategory,
            _internalPlayerChoice, _externalPlayerChoice, _settingsBrowse, _settingsReset, _setupLibMpv, _testSound, .. NetworkSettingsButtons];
        private nint[] SettingsChecks => [_notifyNew, _notifyFavorite, _notifySound, _connectionIpv6, _connectionWan, _connectionAutoMapping, _ypEnabled, _lowLatencyPlayback];
        private bool PlaybackCategory => _settingsCategory == GenesisIntegratedSettingsCategory.Playback;
        private bool PathCategory => PlaybackCategory || _settingsCategory == GenesisIntegratedSettingsCategory.Recording;
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
            _internalPlayerChoice = Button(GenesisPathSettingsPage.PlayerLabel(GenesisPlaybackPlayerMode.Internal), InternalPlayerChoiceId);
            _externalPlayerChoice = Button(GenesisPathSettingsPage.PlayerLabel(GenesisPlaybackPlayerMode.External), ExternalPlayerChoiceId);
            _settingsPath = CreateWindowExW(0, "EDIT", string.Empty, WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 0x0080,
                0, 0, 1, 1, _settingsPane, (nint)SettingsPathId, instance, 0);
            _ = SendMessageW(_settingsPath, 0x00C5, GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity - 1, 0); // EM_SETLIMITTEXT
            _settingsBrowse = Button("参照…", SettingsBrowseId);
            _lowLatencyPlayback = Check("低遅延再生", LowLatencyPlaybackId);
            _settingsReset = Button("未設定に戻す", SettingsResetId);
            _setupLibMpv = Button("公式 libmpv を自動導入", SetupLibMpvId);
            _testSound = Button("通知音テスト", TestSoundId);
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
            _ = SetFocus(category switch
            {
                GenesisIntegratedSettingsCategory.Playback =>
                    _pathSettings.PlayerMode == GenesisPlaybackPlayerMode.Internal ? _internalPlayerChoice : _externalPlayerChoice,
                GenesisIntegratedSettingsCategory.Recording => _settingsPath,
                GenesisIntegratedSettingsCategory.Notifications => _notifyNew,
                GenesisIntegratedSettingsCategory.Connection or GenesisIntegratedSettingsCategory.YellowPages => NetworkInitialFocus,
                _ => _modernChoice,
            });
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
            Show(_internalPlayerChoice, PlaybackCategory); Show(_externalPlayerChoice, PlaybackCategory);
            Show(_lowLatencyPlayback, PlaybackCategory);
            Show(_settingsPath, PathCategory); Show(_settingsBrowse, PathCategory);
            Show(_settingsReset, PathCategory && _pathSettings!.CanReset);
            Show(_setupLibMpv, PlaybackCategory && _pathSettings!.CanSetupLibMpv);
            Show(_testSound, notification);
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
            else if (PlaybackCategory &&
                (id == InternalPlayerChoiceId && control == _internalPlayerChoice || id == ExternalPlayerChoiceId && control == _externalPlayerChoice))
            {
                _settingsCategoryRevision++;
                _pathSettings!.SelectPlayer(id == InternalPlayerChoiceId ? GenesisPlaybackPlayerMode.Internal : GenesisPlaybackPlayerMode.External);
                RefreshIntegratedSettings();
            }
            else if (id == LowLatencyPlaybackId && control == _lowLatencyPlayback && PlaybackCategory)
            {
                _pathSettings!.SetLowLatencyInternalPlayback(SendMessageW(control, 0x00F0, 0, 0) == 1);
                RefreshIntegratedSettings();
            }
            else if (id == SettingsBrowseId && control == _settingsBrowse && PathCategory)
            {
                if (!ReadSettingsPath(out var text)) { RefreshIntegratedSettings(); return true; }
                var category = _settingsCategory;
                var revision = _settingsCategoryRevision;
                // Browsing only modifies the current draft; persistence remains behind the Save button.
                var selectedPath = PlaybackCategory
                    ? GenesisGuiNextPlaybackSettingsWindow.BrowseForPlayerFile(
                        _owner, text, library: _pathSettings!.PlayerMode == GenesisPlaybackPlayerMode.Internal)
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
            else if (id == SetupLibMpvId && control == _setupLibMpv && PlaybackCategory)
            {
                StartLibMpvAutoSetup();
            }
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
                if (PlaybackCategory)
                {
                    var internalSelected = page.PlayerMode == GenesisPlaybackPlayerMode.Internal;
                    var libMpvStatus = internalSelected
                        ? LibMpvRuntimeProvisioner.DescribeAvailability(page.Draft, page.Saved)
                        : null;
                    _ = SetWindowTextW(_generalTitle, "再生設定");
                    _ = SetWindowTextW(_generalNotice, internalSelected
                        ? "内蔵プレイヤーは libmpv DLL を使用し、保存した設定は次の再生から使用します。\r\n低遅延再生では通信状況により途切れやすくなります。"
                        : "保存したプレイヤーとファイルは次の再生から使用します。\r\n空欄では環境変数または PATH を使用します。「未設定に戻す」は即時保存します。");
                    var savedMode = page.SavedPlayerMode;
                    var savedPath = savedMode == page.PlayerMode ? " / " + (page.Saved.Length == 0 ? "パス未設定" : page.Saved) : string.Empty;
                    var savedBadge = internalSelected && savedMode == GenesisPlaybackPlayerMode.Internal && libMpvStatus is not null
                        ? $"（{libMpvStatus.SavedStateBadge}）"
                        : string.Empty;
                    _ = SetWindowTextW(_generalRuntime, "保存済み: " + GenesisPathSettingsPage.PlayerLabel(savedMode) + savedBadge + savedPath);
                    _ = SetWindowTextW(_generalConfigured, internalSelected ? libMpvStatus!.FieldLabel : "外部プレイヤーの実行ファイル（.exe）");
                    SetPlayerChoiceText(_internalPlayerChoice, GenesisPlaybackPlayerMode.Internal, page.PlayerMode);
                    SetPlayerChoiceText(_externalPlayerChoice, GenesisPlaybackPlayerMode.External, page.PlayerMode);
                    _ = SendMessageW(_lowLatencyPlayback, 0x00F1, page.LowLatencyInternalPlayback ? 1u : 0u, 0);
                    _ = EnableWindow(_lowLatencyPlayback, page.CanEditLowLatency);
                }
                else
                {
                    _ = SetWindowTextW(_generalTitle, "録画・保存先設定");
                    _ = SetWindowTextW(_generalNotice, GenesisRecordingOutputDirectorySettings.NextStartNotice +
                        " 既存writerの保存先は変わりません。\r\n「既定値に戻す」は即時保存します。フォルダーや録画ファイルは作成・移動しません。");
                    _ = SetWindowTextW(_generalRuntime, "現在起動中: " + page.RuntimeDirectory);
                    _ = SetWindowTextW(_generalConfigured, "次回起動用: " + page.EffectiveDirectory + (page.Saved.Length == 0 ? "（既定）" : string.Empty));
                }
                _updatingSettingsPath = true;
                try
                {
                    if (GetSettingsTextLength(_settingsPath) != page.Draft.Length || !ReadSettingsPath(out var existing) || existing != page.Draft)
                        _ = SetWindowTextW(_settingsPath, page.Draft);
                }
                finally { _updatingSettingsPath = false; }
                _ = SetWindowTextW(_settingsReset, PlaybackCategory ? "未設定に戻す" : "既定値に戻す");
                _ = SetWindowTextW(_generalStatus, page.Message);
                _ = EnableWindow(_generalSave, page.IsDirty && !page.Conflict);
                _ = EnableWindow(_generalCancel, page.IsDirty || page.Conflict || page.IsError);
                _ = ShowWindow(_settingsReset, page.CanReset ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = EnableWindow(_settingsReset, !page.Conflict);
                var setupButtonText = _settingUpLibMpv
                    ? "導入中..."
                    : PlaybackCategory && page.CanSetupLibMpv
                        ? LibMpvRuntimeProvisioner.DescribeAvailability(page.Draft, page.Saved).ButtonText
                        : "公式 libmpv を自動導入";
                _ = ShowWindow(_setupLibMpv, PlaybackCategory && page.CanSetupLibMpv ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = SetWindowTextW(_setupLibMpv, setupButtonText);
                _ = EnableWindow(_setupLibMpv, !_settingUpLibMpv && !page.Conflict);
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

        private static void SetPlayerChoiceText(nint control, GenesisPlaybackPlayerMode mode, GenesisPlaybackPlayerMode selected) =>
            _ = SetWindowTextW(control, GenesisPathSettingsPage.PlayerLabel(mode) + (mode == selected ? "（選択中）" : string.Empty));

        private void PlaceSettingsCategory(GenesisGuiNextSettingsLayout layout)
        {
            void Place(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle b) => _ = MoveWindow(control, Scale(b.Left), Scale(b.Top), Scale(b.Width), Scale(b.Height), true);
            PlaceNetworkSettingsCategory(layout);
            Place(_playbackCategory, GenesisGuiNextSettingsLayout.PlaybackTab); Place(_recordingCategory, GenesisGuiNextSettingsLayout.RecordingTab);
            Place(_notificationsCategory, GenesisGuiNextSettingsLayout.NotificationsTab);
            Place(_internalPlayerChoice, GenesisGuiNextSettingsLayout.InternalPlayerChoice);
            Place(_externalPlayerChoice, GenesisGuiNextSettingsLayout.ExternalPlayerChoice);
            Place(_lowLatencyPlayback, layout.LowLatencyPlayback);
            Place(_settingsReset, layout.Reset); Place(_setupLibMpv, layout.SetupLibMpv); Place(_testSound, layout.SoundTest);
            if (PathCategory)
            {
                Place(_generalNotice, layout.PathNotice(PlaybackCategory));
                Place(_generalRuntime, layout.PathRuntime(PlaybackCategory));
                Place(_generalConfigured, layout.PathSaved(PlaybackCategory));
                Place(_settingsPath, layout.PathInput(PlaybackCategory));
                Place(_settingsBrowse, layout.Browse(PlaybackCategory));
                Place(_generalStatus, layout.PathStatus(PlaybackCategory));
            }
            else if (_settingsCategory == GenesisIntegratedSettingsCategory.Notifications)
            {
                Place(_generalNotice, layout.PathNotice(playback: false)); Place(_generalStatus, layout.NotificationStatus);
            }
            for (var i = 0; i < 3; i++) Place(SettingsChecks[i], layout.NotificationCheck(i));
        }

        internal bool IsSettingsPane(nint window) => window == _settingsPane && _settingsPane != 0;
        internal void RefreshSettingsPathFocus(nint window)
        {
            if ((window == _settingsPath || NetworkSettingsInputs.Contains(window)) && SettingsPageOpen) _ = RedrawWindow(_settingsPane, 0, 0, 0x0001 | 0x0004);
        }

        private void StartLibMpvAutoSetup()
        {
            if (_settingUpLibMpv || _pathSettings is null || !_pathSettings.CanSetupLibMpv)
            {
                return;
            }

            _settingUpLibMpv = true;
            _pathSettings.ReportStatus($"公式 libmpv (LGPL {LibMpvRuntimeProvisioner.Version}) の導入を開始しています…", isError: false);
            RefreshIntegratedSettings();

            var provisioner = new LibMpvRuntimeProvisioner();
            var targetDir = LibMpvRuntimeProvisioner.ResolveDefaultRuntimeDirectory();
            var progress = new Progress<LibMpvSetupProgress>(report => PostPublishingUi(() =>
            {
                if (_settingUpLibMpv && _pathSettings is not null && PlaybackCategory)
                {
                    _pathSettings.ReportStatus(report.FormatStatus(), isError: false);
                    _ = SetWindowTextW(_generalStatus, _pathSettings.Message);
                }
            }));

            _ = Task.Run(async () =>
            {
                var result = await provisioner.ProvisionAsync(targetDir, progress).ConfigureAwait(false);
                PostPublishingUi(() =>
                {
                    _settingUpLibMpv = false;
                    if (_pathSettings is null)
                    {
                        return;
                    }

                    if (result.Succeeded && !string.IsNullOrWhiteSpace(result.LibraryPath))
                    {
                        if (PlaybackCategory)
                        {
                            _pathSettings.SelectPlayer(GenesisPlaybackPlayerMode.Internal);
                            _pathSettings.Edit(result.LibraryPath);
                            if (_pathSettings.Save())
                            {
                                _pathSettings.ReportStatus(
                                    result.AlreadyPresent
                                        ? $"導入済みの公式 libmpv ({LibMpvRuntimeProvisioner.Version} LGPL) を設定・保存しました。"
                                        : $"公式 libmpv ({LibMpvRuntimeProvisioner.Version} LGPL) を導入して保存しました。次の再生から使用します。",
                                    isError: false);
                            }
                        }
                    }
                    else
                    {
                        _pathSettings.ReportStatus(result.ErrorMessage ?? "libmpv の自動導入に失敗しました。", isError: true);
                    }

                    RefreshIntegratedSettings();
                });
            });
        }
    }

    internal static async Task RequestSettingsSoundAsync(GenesisChannelNotificationSettings settings)
    {
        try { await settings.TestSoundAsync().ConfigureAwait(false); }
        catch { /* Existing notification feedback owns errors; no late callback to a closed settings page. */ }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetSettingsTextLength(nint window);
}

using PeerCast.Nova.Windows;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisIntegratedSettingsController
{
    private GenesisIntegratedSettingsCategory? _settingsCategory;
    private long _settingsCategoryRevision;
    private GenesisPathSettingsPage? _pathSettings;
    private nint _playbackCategory;
    private nint _recordingCategory;
    private nint _notificationsCategory;
    private nint _settingsPath;
    private nint _settingsBrowse;
    private nint _settingsReset;
    private nint _internalPlayerChoice;
    private nint _externalPlayerChoice;
    private nint _lowLatencyPlayback;
    private nint _setupLibMpv;
    private nint _externalPlayerArgsLabel;
    private nint _externalPlayerArgs;
    private nint _notifyNew;
    private nint _notifyFavorite;
    private nint _notifySound;
    private nint _testSound;
    private bool _updatingSettingsPath;
    private bool _notificationError;
    private bool _settingUpLibMpv;
    private string _notificationMessage = string.Empty;

    private nint[] AdditionalSettingsButtons =>
    [
        _playbackCategory,
        _recordingCategory,
        _notificationsCategory,
        _internalPlayerChoice,
        _externalPlayerChoice,
        _settingsBrowse,
        _settingsReset,
        _setupLibMpv,
        _testSound,
        .. NetworkSettingsButtons,
    ];

    private nint[] SettingsChecks =>
    [
        _notifyNew,
        _notifyFavorite,
        _notifySound,
        _connectionIpv6,
        _connectionWan,
        _connectionAutoMapping,
        _ypEnabled,
        _lowLatencyPlayback,
    ];

    private bool PlaybackCategory => _settingsCategory == GenesisIntegratedSettingsCategory.Playback;
    private bool PathCategory => PlaybackCategory || _settingsCategory == GenesisIntegratedSettingsCategory.Recording;

    private bool SettingsError => _settingsCategory == GenesisIntegratedSettingsCategory.General
        ? _generalSettings?.IsError == true
        : PathCategory
            ? _pathSettings?.IsError == true
            : NetworkCategory ? NetworkSettingsError : _notificationError;

    private void CreateSettingsCategories(nint instance)
    {
        nint Button(string text, int id) => _host.CreateOwnerDrawButton(text, _settingsPane, id, 0, instance);
        nint Check(string text, int id)
        {
            var control = CreateWindowExW(
                0,
                "BUTTON",
                text,
                WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 3,
                0,
                0,
                1,
                1,
                _settingsPane,
                (nint)id,
                instance,
                0); // BS_AUTOCHECKBOX: キーボード操作およびアクセシビリティ対応
            _ = SetWindowTheme(control, " ", " ");
            return control;
        }

        _playbackCategory = Button("再生", PlaybackCategoryId);
        _recordingCategory = Button("録画・保存先", RecordingCategoryId);
        _notificationsCategory = Button("通知", NotificationsCategoryId);
        _internalPlayerChoice = Button(GenesisPathSettingsPage.PlayerLabel(GenesisPlaybackPlayerMode.Internal), InternalPlayerChoiceId);
        _externalPlayerChoice = Button(GenesisPathSettingsPage.PlayerLabel(GenesisPlaybackPlayerMode.External), ExternalPlayerChoiceId);
        _settingsPath = CreateWindowExW(
            0,
            "EDIT",
            string.Empty,
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 0x0080,
            0,
            0,
            1,
            1,
            _settingsPane,
            (nint)SettingsPathId,
            instance,
            0);
        _ = SendMessageW(_settingsPath, 0x00C5, GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity - 1, 0); // EM_SETLIMITTEXT
        _settingsBrowse = Button("参照…", SettingsBrowseId);
        _lowLatencyPlayback = Check("低遅延再生", LowLatencyPlaybackId);
        _externalPlayerArgsLabel = CreateWindowExW(0, "STATIC", "起動引数", WindowStyleChild | WindowStyleVisible, 0, 0, 1, 1, _settingsPane, 0, instance, 0);
        _externalPlayerArgs = CreateWindowExW(
            0,
            "EDIT",
            string.Empty,
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 0x0080,
            0,
            0,
            1,
            1,
            _settingsPane,
            (nint)ExternalPlayerArgsId,
            instance,
            0);
        _ = Win32AccessibilityAnnotation.SetName(_externalPlayerArgs, "外部プレイヤー起動引数");
        _ = Win32AccessibilityAnnotation.SetName(_settingsPath, "プレイヤーまたは libmpv のパス");
        _ = SendMessageW(_externalPlayerArgs, 0x00C5, 1024, 0); // EM_SETLIMITTEXT
        _ = SendMessageTextW(_externalPlayerArgs, 0x1501, 1, "{url} {title} {contact}（空欄で既定）");
        _settingsReset = Button("未設定に戻す", SettingsResetId);
        _setupLibMpv = Button("公式 libmpv を自動導入", SetupLibMpvId);
        _testSound = Button("通知音テスト", TestSoundId);
        _notifyNew = Check("新着チャンネル通知", NotifyNewId);
        _notifyFavorite = Check("お気に入りチャンネル通知", NotifyFavoriteId);
        _notifySound = Check("通知音を有効にする", NotifySoundId);
        _pathSettings = new(_host.ViewModel.PlaybackSettings, _host.ViewModel.RecordingOutputSettings);
        CreateNetworkSettingsCategories(instance);
    }

    private void SelectSettingsCategory(GenesisIntegratedSettingsCategory category)
    {
        if (_settingsCategory == category)
        {
            return;
        }

        if (category is GenesisIntegratedSettingsCategory.Playback or GenesisIntegratedSettingsCategory.Recording && _pathSettings?.Available(category) != true)
        {
            return;
        }

        if (category == GenesisIntegratedSettingsCategory.Notifications && _host.ViewModel.NotificationSettings is null)
        {
            return;
        }

        if ((category == GenesisIntegratedSettingsCategory.Connection && _connectionPage is null) ||
            (category == GenesisIntegratedSettingsCategory.YellowPages && _ypPage is null))
        {
            return;
        }

        CloseNetworkSettingsPages();
        _generalSettings!.Close();
        _pathSettings!.Close();
        _settingsCategoryRevision++;
        _settingsCategory = category;
        if (category == GenesisIntegratedSettingsCategory.General)
        {
            _ = _generalSettings.Open();
        }
        else if (PathCategory)
        {
            _ = _pathSettings.Open(category);
        }
        else if (category == GenesisIntegratedSettingsCategory.Connection)
        {
            _connectionPage!.Open();
        }
        else if (category == GenesisIntegratedSettingsCategory.YellowPages)
        {
            _ypPage!.Open();
        }
        else
        {
            _notificationError = false;
            _notificationMessage = "変更は即時に保存・反映されます。保存・キャンセル操作はありません。";
        }

        ShowSettingsCategory();
        Refresh();
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
        Show(_classicChoice, general);
        Show(_modernChoice, general);
        Show(_updateTitle, general);
        Show(_updateStatus, general);
        Show(_checkUpdates, general);
        Show(_openReleasePage, general);
        Show(_generalNotice, true); // YP タブで非表示にする共通テキストを他カテゴリでは復元する。
        Show(_generalStatus, true);
        Show(_generalRuntime, !notification);
        Show(_generalConfigured, !notification);
        Show(_generalSave, !notification);
        Show(_generalCancel, !notification);
        var internalPlayback = PlaybackCategory && _pathSettings!.PlayerMode == GenesisPlaybackPlayerMode.Internal;
        var externalPlayback = PlaybackCategory && _pathSettings!.PlayerMode == GenesisPlaybackPlayerMode.External;
        Show(_internalPlayerChoice, PlaybackCategory);
        Show(_externalPlayerChoice, PlaybackCategory);
        Show(_lowLatencyPlayback, internalPlayback);
        Show(_externalPlayerArgsLabel, externalPlayback);
        Show(_externalPlayerArgs, externalPlayback);
        Show(_settingsPath, PathCategory);
        Show(_settingsBrowse, PathCategory);
        Show(_settingsReset, PathCategory && _pathSettings!.CanReset);
        Show(_setupLibMpv, PlaybackCategory && _pathSettings!.CanSetupLibMpv);
        Show(_testSound, notification);
        foreach (var control in new[] { _notifyNew, _notifyFavorite, _notifySound })
        {
            Show(control, notification);
        }

        ShowNetworkSettingsCategory();
        _ = EnableWindow(_playbackCategory, _pathSettings?.Available(GenesisIntegratedSettingsCategory.Playback) == true);
        _ = EnableWindow(_recordingCategory, _pathSettings?.Available(GenesisIntegratedSettingsCategory.Recording) == true);
        _ = EnableWindow(_notificationsCategory, _host.ViewModel.NotificationSettings is not null);
        if (GetClientRect(_settingsPane, out var bounds))
        {
            Place(_host.LogicalWidth(bounds.Right), _host.LogicalWidth(bounds.Bottom));
        }
    }

    private bool ReadSettingsTextBox(nint control, int maxLength, out string text)
    {
        text = string.Empty;
        var length = GetWindowTextLengthW(control);
        if (length < 0 || length >= maxLength)
        {
            _pathSettings!.ReportInputError("入力が長すぎます。保存値は変更していません。");
            return false;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(control, buffer, buffer.Length);
        if (copied != length)
        {
            _pathSettings!.ReportInputError("入力を読み取れませんでした。保存値は変更していません。");
            return false;
        }

        text = new string(buffer, 0, copied);
        return true;
    }

    private bool ReadSettingsPath(out string text) =>
        ReadSettingsTextBox(_settingsPath, GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity, out text);

    private bool ReadExternalPlayerArguments(out string text) =>
        ReadSettingsTextBox(_externalPlayerArgs, 1025, out text);

    private bool ProcessSettingsPathInput(int id, int code, nint control)
    {
        if (code != 0x0300 || !IsOpen || !PathCategory)
        {
            return false;
        }

        if (id == SettingsPathId && control == _settingsPath)
        {
            if (!_updatingSettingsPath)
            {
                if (ReadSettingsPath(out var text))
                {
                    _pathSettings!.Edit(text);
                }

                Refresh();
            }

            return true;
        }

        if (id == ExternalPlayerArgsId && control == _externalPlayerArgs && PlaybackCategory)
        {
            if (!_updatingSettingsPath)
            {
                if (ReadExternalPlayerArguments(out var text))
                {
                    _pathSettings!.EditExternalPlayerArguments(text);
                }

                Refresh();
            }

            return true;
        }

        return false;
    }

    private bool ProcessSettingsCategoryCommand(int id, nint control)
    {
        if (ProcessNetworkSettingsCommand(id, control))
        {
            return true;
        }

        if (id == PlaybackCategoryId && control == _playbackCategory)
        {
            SelectSettingsCategory(GenesisIntegratedSettingsCategory.Playback);
        }
        else if (id == RecordingCategoryId && control == _recordingCategory)
        {
            SelectSettingsCategory(GenesisIntegratedSettingsCategory.Recording);
        }
        else if (id == NotificationsCategoryId && control == _notificationsCategory)
        {
            SelectSettingsCategory(GenesisIntegratedSettingsCategory.Notifications);
        }
        else if (PlaybackCategory &&
            ((id == InternalPlayerChoiceId && control == _internalPlayerChoice) ||
             (id == ExternalPlayerChoiceId && control == _externalPlayerChoice)))
        {
            _settingsCategoryRevision++;
            _pathSettings!.SelectPlayer(id == InternalPlayerChoiceId ? GenesisPlaybackPlayerMode.Internal : GenesisPlaybackPlayerMode.External);
            Refresh();
        }
        else if (id == LowLatencyPlaybackId && control == _lowLatencyPlayback && PlaybackCategory)
        {
            _pathSettings!.SetLowLatencyInternalPlayback(SendMessageW(control, 0x00F0, 0, 0) == 1);
            Refresh();
        }
        else if (id == SettingsBrowseId && control == _settingsBrowse && PathCategory)
        {
            if (!ReadSettingsPath(out var text))
            {
                Refresh();
                return true;
            }

            var category = _settingsCategory;
            var revision = _settingsCategoryRevision;
            // 参照ダイアログによる変更は下書きのみを更新し、永続化は保存ボタン時に行う。
            var selectedPath = PlaybackCategory
                ? GenesisGuiNextPlaybackSettingsWindow.BrowseForPlayerFile(
                    _host.Owner, text, library: _pathSettings!.PlayerMode == GenesisPlaybackPlayerMode.Internal)
                : default;
            var folder = category == GenesisIntegratedSettingsCategory.Recording
                ? GenesisRecordingDirectoryPicker.Browse(_host.Owner)
                : default;
            if (IsOpen && category == _settingsCategory && revision == _settingsCategoryRevision)
            {
                var path = category == GenesisIntegratedSettingsCategory.Playback ? selectedPath.SelectedPath : folder.SelectedPath;
                var failed = category == GenesisIntegratedSettingsCategory.Playback ? selectedPath.Failed : folder.Failed;
                if (path is not null)
                {
                    _pathSettings!.Edit(path);
                }
                else if (failed)
                {
                    _pathSettings!.ReportInputError("フォルダーまたはファイルを選択できませんでした。");
                }

                Refresh();
            }
        }
        else if (id == SettingsResetId && control == _settingsReset && PathCategory)
        {
            _ = _pathSettings!.Save(reset: true);
            Refresh();
        }
        else if (id == SetupLibMpvId && control == _setupLibMpv && PlaybackCategory)
        {
            StartLibMpvAutoSetup();
        }
        else if (_settingsCategory == GenesisIntegratedSettingsCategory.Notifications && _host.ViewModel.NotificationSettings is { } notifications)
        {
            if (id == TestSoundId && control == _testSound)
            {
                _ = RequestSettingsSoundAsync(notifications);
                _notificationMessage = "通知音テストを要求しました（自動通知音の設定は変更しません）。";
                _notificationError = false;
            }
            else
            {
                if (!((id == NotifyNewId && control == _notifyNew) ||
                      (id == NotifyFavoriteId && control == _notifyFavorite) ||
                      (id == NotifySoundId && control == _notifySound)))
                {
                    return false;
                }

                var value = SendMessageW(control, 0x00F0, 0, 0) == 1;
                var result = id == NotifyNewId && control == _notifyNew
                    ? notifications.SetNotifyNewChannels(value)
                    : id == NotifyFavoriteId && control == _notifyFavorite
                        ? notifications.SetNotifyFavoriteChannels(value)
                        : id == NotifySoundId && control == _notifySound
                            ? notifications.SetPlaySound(value)
                            : (GenesisChannelNotificationSettingsMutationResult?)null;
                if (result is null)
                {
                    return false;
                }

                _notificationError = result == GenesisChannelNotificationSettingsMutationResult.PersistenceFailed;
                _notificationMessage = _notificationError
                    ? "通知設定を保存できませんでした。既存値を保持しています。"
                    : "通知設定を保存・反映しました。";
            }

            Refresh();
        }
        else
        {
            return false;
        }

        return true;
    }

    private void RefreshAdditionalSettings()
    {
        if (NetworkCategory)
        {
            RefreshNetworkSettings();
            return;
        }

        if (PathCategory)
        {
            var page = _pathSettings!;
            page.Refresh();
            if (PlaybackCategory)
            {
                _ = Win32AccessibilityAnnotation.SetName(_settingsPath, "プレイヤーまたは libmpv のパス");
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
                _ = ShowWindow(_lowLatencyPlayback, internalSelected ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = ShowWindow(_externalPlayerArgsLabel, !internalSelected ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = ShowWindow(_externalPlayerArgs, !internalSelected ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = SendMessageW(_lowLatencyPlayback, 0x00F1, page.LowLatencyInternalPlayback ? 1u : 0u, 0);
                _ = EnableWindow(_lowLatencyPlayback, page.CanEditLowLatency);
                _ = EnableWindow(_externalPlayerArgs, page.CanEditExternalPlayerArguments);
            }
            else
            {
                _ = Win32AccessibilityAnnotation.SetName(_settingsPath, "録画保存先フォルダーのパス");
                _ = SetWindowTextW(_generalTitle, "録画・保存先設定");
                _ = SetWindowTextW(_generalNotice, GenesisRecordingOutputDirectorySettings.NextStartNotice +
                    "\r\n「既定値に戻す」は即時保存します。フォルダーや録画ファイルは作成・移動しません。");
                _ = SetWindowTextW(_generalRuntime, "現在の保存先: " + page.RuntimeDirectory);
                _ = SetWindowTextW(_generalConfigured, "設定済み保存先: " + page.EffectiveDirectory + (page.Saved.Length == 0 ? "（既定）" : string.Empty));
            }

            _updatingSettingsPath = true;
            try
            {
                if (GetWindowTextLengthW(_settingsPath) != page.Draft.Length || !ReadSettingsPath(out var existing) || existing != page.Draft)
                {
                    _ = SetWindowTextW(_settingsPath, page.Draft);
                }

                if (PlaybackCategory &&
                    (GetWindowTextLengthW(_externalPlayerArgs) != page.ExternalPlayerArguments.Length ||
                     !ReadExternalPlayerArguments(out var existingArgs) ||
                     existingArgs != page.ExternalPlayerArguments))
                {
                    _ = SetWindowTextW(_externalPlayerArgs, page.ExternalPlayerArguments);
                }
            }
            finally
            {
                _updatingSettingsPath = false;
            }

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
        else if (_host.ViewModel.NotificationSettings is { } notifications)
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
        void MoveControl(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle b) =>
            _ = MoveWindow(control, _host.Scale(b.Left), _host.Scale(b.Top), _host.Scale(b.Width), _host.Scale(b.Height), true);
        PlaceNetworkSettingsCategory(layout);
        MoveControl(_playbackCategory, GenesisGuiNextSettingsLayout.PlaybackTab);
        MoveControl(_recordingCategory, GenesisGuiNextSettingsLayout.RecordingTab);
        MoveControl(_notificationsCategory, GenesisGuiNextSettingsLayout.NotificationsTab);
        MoveControl(_internalPlayerChoice, GenesisGuiNextSettingsLayout.InternalPlayerChoice);
        MoveControl(_externalPlayerChoice, GenesisGuiNextSettingsLayout.ExternalPlayerChoice);
        MoveControl(_lowLatencyPlayback, layout.LowLatencyPlayback);
        MoveControl(_externalPlayerArgsLabel, GenesisGuiNextSettingsLayout.ExternalPlayerArgsLabel);
        MoveControl(_externalPlayerArgs, layout.ExternalPlayerArgsInput);
        MoveControl(_settingsReset, layout.Reset);
        MoveControl(_setupLibMpv, layout.SetupLibMpv);
        MoveControl(_testSound, layout.SoundTest);
        if (PathCategory)
        {
            MoveControl(_generalNotice, layout.PathNotice(PlaybackCategory));
            MoveControl(_generalRuntime, layout.PathRuntime(PlaybackCategory));
            MoveControl(_generalConfigured, layout.PathSaved(PlaybackCategory));
            MoveControl(_settingsPath, layout.PathInput(PlaybackCategory));
            MoveControl(_settingsBrowse, layout.Browse(PlaybackCategory));
            MoveControl(_generalStatus, layout.PathStatus(PlaybackCategory));
        }
        else if (_settingsCategory == GenesisIntegratedSettingsCategory.Notifications)
        {
            MoveControl(_generalNotice, layout.PathNotice(playback: false));
            MoveControl(_generalStatus, layout.NotificationStatus);
        }

        for (var i = 0; i < 3; i++)
        {
            MoveControl(SettingsChecks[i], layout.NotificationCheck(i));
        }
    }

    internal bool IsPane(nint window) => window == _settingsPane && _settingsPane != 0;

    internal void RefreshPathFocus(nint window)
    {
        if ((window == _settingsPath || window == _externalPlayerArgs || NetworkSettingsInputs.Contains(window)) && IsOpen)
        {
            _ = RedrawWindow(_settingsPane, 0, 0, 0x0001 | 0x0004);
        }
    }

    private void StartLibMpvAutoSetup()
    {
        if (_settingUpLibMpv || _pathSettings is null || !_pathSettings.CanSetupLibMpv)
        {
            return;
        }

        _settingUpLibMpv = true;
        _pathSettings.ReportStatus($"公式 libmpv (LGPL {LibMpvRuntimeProvisioner.Version}) の導入を開始しています…", isError: false);
        Refresh();

        var provisioner = new LibMpvRuntimeProvisioner();
        var targetDir = LibMpvRuntimeProvisioner.ResolveDefaultRuntimeDirectory();
        var progress = new Progress<LibMpvSetupProgress>(report => _host.EnqueueUiAction(() =>
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
            _host.EnqueueUiAction(() =>
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

                Refresh();
            });
        });
    }

    private void StartUpdateCheck()
    {
        if (_checkingUpdates || _settingsPane == 0)
        {
            return;
        }

        _checkingUpdates = true;
        Refresh();

        _ = Task.Run(async () =>
        {
            using var checker = new NovaReleaseUpdateChecker();
            var result = await checker.CheckForUpdatesAsync().ConfigureAwait(false);
            _host.EnqueueUiAction(() =>
            {
                if (_settingsPane == 0)
                {
                    return;
                }

                _checkingUpdates = false;
                _lastUpdateResult = result;
                if (_settingsCategory == GenesisIntegratedSettingsCategory.General)
                {
                    Refresh();
                }
            });
        });
    }

    internal static async Task RequestSettingsSoundAsync(GenesisChannelNotificationSettings settings)
    {
        try
        {
            await settings.TestSoundAsync().ConfigureAwait(false);
        }
        catch
        {
            // 既存の通知フィードバックがエラー表示を担当するため、閉じた設定画面への遅延通知は行わない。
        }
    }
}

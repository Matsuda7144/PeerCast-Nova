using System.Runtime.InteropServices;
using PeerCast.Nova.Foundation.Configuration;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    private const int GeneralCategoryId = 1180, LegacySettingsId = 1181, ClassicChoiceId = 1182,
        ModernChoiceId = 1183, GeneralSaveId = 1184, GeneralCancelId = 1185, GeneralBackId = 1186,
        CheckUpdatesId = 1187, OpenReleasePageId = 1188;

    private sealed partial class AppShellState
    {
        private nint _settingsPane, _settingsTitle, _generalTitle, _generalRuntime, _generalConfigured, _generalNotice, _generalStatus;
        private nint _updateTitle, _updateStatus;
        private nint _generalCategory, _legacySettings, _classicChoice, _modernChoice, _generalSave, _generalCancel, _generalBack;
        private nint _checkUpdates, _openReleasePage;
        private GenesisGeneralSettingsPage? _generalSettings;
        private bool _checkingUpdates;
        private NovaUpdateCheckResult? _lastUpdateResult;
        private bool SettingsPageOpen => _settingsCategory is not null;
        private nint[] SettingsButtons => _settingsPane == 0 ? [] : [_generalCategory, _legacySettings, _classicChoice, _modernChoice, _checkUpdates, _openReleasePage, _generalSave, _generalCancel, _generalBack, .. AdditionalSettingsButtons];
        private nint[] SettingsTexts => _settingsPane == 0 ? [] : [_settingsTitle, _generalTitle, _generalRuntime, _generalConfigured, _generalNotice, _generalStatus, _updateTitle, _updateStatus, _externalPlayerArgsLabel];
        private nint[] SettingsVisualControls => _settingsPane == 0 ? [] : [.. SettingsButtons, .. SettingsTexts, _settingsPath, _externalPlayerArgs, .. SettingsChecks, .. NetworkSettingsInputs, .. NetworkSettingsTexts, _ypList];

        internal void CreateIntegratedSettings(string className, nint instance)
        {
            if (!IsModern || _viewModel.AppearanceSettings is not { } appearance) return;
            _settingsPane = CreateWindowExW(0x00010000, className, string.Empty, WindowStyleChild | 0x02000000,
                0, 0, 1, 1, GetParent(_detailsPane), 0, instance, 0); // WS_EX_CONTROLPARENT
            if (_settingsPane == 0) return;
            nint Text(string text) => CreateWindowExW(0, "STATIC", text, WindowStyleChild | WindowStyleVisible,
                0, 0, 1, 1, _settingsPane, 0, instance, 0);
            nint Button(string text, int id) => CreateRecordingModeButton(text, _settingsPane, id, 0, instance);
            _settingsTitle = Text("設定");
            _generalTitle = Text("外観設定");
            _generalRuntime = Text(string.Empty);
            _generalConfigured = Text(string.Empty);
            _generalNotice = Text(GenesisAppearanceSettings.NextStartNotice + "\r\nUPnP/NATなどの詳細設定は「従来の設定を開く…」から変更できます。");
            _generalStatus = Text(string.Empty);
            _updateTitle = Text($"アプリの更新（現在: {NovaReleaseUpdateChecker.GetCurrentVersion().ToTagString()}）");
            _updateStatus = Text("「更新を確認」を押すと GitHub Releases の最新バージョンを確認します。");
            _generalCategory = Button("一般", GeneralCategoryId);
            _legacySettings = Button("従来の設定を開く…", LegacySettingsId);
            _classicChoice = Button("Classic", ClassicChoiceId);
            _modernChoice = Button("Modern", ModernChoiceId);
            _checkUpdates = Button("更新を確認", CheckUpdatesId);
            _openReleasePage = Button("配布ページを開く", OpenReleasePageId);
            _generalBack = Button("戻る", GeneralBackId);
            _generalCancel = Button("キャンセル", GeneralCancelId);
            _generalSave = Button("保存", GeneralSaveId);
            CreateSettingsCategories(instance);
            if (SettingsVisualControls.Any(control => control == 0) || !StatesByContainer.TryAdd(_settingsPane, this))
            {
                DestroyIntegratedSettings(); // The ordinary settings menu remains the fallback.
                return;
            }
            foreach (var control in SettingsVisualControls) ApplyDefaultFont(control);
            _generalSettings = new(appearance);
        }

        internal void DestroyIntegratedSettings()
        {
            CloseNatSettingsPage();
            CloseNetworkSettingsPages();
            _generalSettings?.Release();
            _generalSettings = null;
            _pathSettings?.Release();
            _pathSettings = null;
            _settingsCategory = null;
            _ = StatesByContainer.TryRemove(_settingsPane, out _);
            DestroyWindowIfPresent(_settingsPane);
            _settingsPane = 0;
        }

        private bool OpenIntegratedSettings()
        {
            if (!GenesisGeneralSettingsPage.UseIntegrated(IsModern, _settingsPane != 0) || _generalSettings is null) return false;
            if (!SettingsPageOpen)
            {
                if (!_generalSettings.Open()) return false;
                _settingsCategoryRevision++;
                _settingsCategory = GenesisIntegratedSettingsCategory.General;
                ShowSettingsCategory();
            }
            _ = ShowWindow(_listView, ShowWindowHide);
            _ = ShowWindow(_detailsPane, ShowWindowHide);
            _ = ShowWindow(_settingsPane, ShowWindowShowNoActivate);
            UpdatePaneSplitterVisibility();
            RefreshIntegratedSettings();
            _favoriteUpdates.Reset();
            SynchronizeNativeSelection();
            _ = SetFocus(NetworkCategory ? NetworkInitialFocus : PathCategory ? _settingsPath : _settingsCategory == GenesisIntegratedSettingsCategory.Notifications ? _notifyNew :
                _generalSettings.Draft == NovaGuiMode.Modern ? _modernChoice : _classicChoice);
            return true;
        }

        private void CloseIntegratedSettings()
        {
            if (!SettingsPageOpen) return;
            CloseNetworkSettingsPages();
            _generalSettings!.Close();
            _pathSettings!.Close();
            _settingsCategoryRevision++;
            _settingsCategory = null; // Leaving discards drafts only; immediate notification changes remain saved.
            _ = ShowWindow(_settingsPane, ShowWindowHide);
            _ = ShowWindow(_listView, ShowWindowShowNoActivate);
            _ = ShowWindow(_detailsPane, ShowWindowShowNoActivate);
            UpdatePaneSplitterVisibility();
            _favoriteUpdates.Reset();
            SynchronizeNativeSelection();
        }

        private bool ProcessIntegratedSettingsCommand(int id, nint control)
        {
            if (!SettingsPageOpen) return false;
            if (id == GeneralCategoryId && control == _generalCategory) { SelectSettingsCategory(GenesisIntegratedSettingsCategory.General); return true; }
            if (ProcessSettingsCategoryCommand(id, control)) return true;
            if (id == LegacySettingsId && control == _legacySettings)
            {
                _ = GenesisChannelNotificationSettingsButtonBridge.TryOpenForOwner(_owner, _legacySettings);
                RefreshIntegratedSettings();
                return true;
            }
            if (id == GeneralBackId && control == _generalBack)
            {
                CloseIntegratedSettings();
                _utilityNavigationSelection.SelectView(_tabs, _tabs.SelectedTab);
                _ = InvalidateRect(_sidebar, 0, true);
                _ = SetFocus(_listView);
                return true;
            }
            if (_settingsCategory == GenesisIntegratedSettingsCategory.General)
            {
                if (id == ClassicChoiceId && control == _classicChoice) _generalSettings!.Select(NovaGuiMode.Classic);
                else if (id == ModernChoiceId && control == _modernChoice) _generalSettings!.Select(NovaGuiMode.Modern);
                else if (id == CheckUpdatesId && control == _checkUpdates) { StartUpdateCheck(); return true; }
                else if (id == OpenReleasePageId && control == _openReleasePage)
                {
                    if (!NovaReleaseUpdateChecker.TryOpenApprovedReleaseUrl(_lastUpdateResult?.ReleasePageUrl))
                    {
                        _ = SetWindowTextW(_updateStatus, "配布ページを開けませんでした。URL を確認してください。");
                    }
                    return true;
                }
                else if (id == GeneralSaveId && control == _generalSave) _ = _generalSettings!.Save();
                else if (id == GeneralCancelId && control == _generalCancel) _generalSettings!.Cancel();
                else return false;
            }
            else if (PathCategory)
            {
                if (id == GeneralSaveId && control == _generalSave)
                {
                    if (ReadSettingsPath(out var path))
                    {
                        if (path != _pathSettings!.Draft) _pathSettings.Edit(path);
                        if (PlaybackCategory && _pathSettings.CanEditExternalPlayerArguments && ReadExternalPlayerArguments(out var args))
                            _pathSettings.EditExternalPlayerArguments(args);
                        _ = _pathSettings.Save();
                    }
                }
                else if (id == GeneralCancelId && control == _generalCancel) _pathSettings!.Cancel();
                else return false;
            }
            else return false;
            RefreshIntegratedSettings();
            if ((control == _generalSave || control == _generalCancel) && !(PathCategory ? _pathSettings!.IsDirty : _generalSettings!.IsDirty))
                _ = SetFocus(_generalBack);
            return true;
        }

        internal void RefreshSettingsOnActivation(nint window, uint message, nuint value)
        {
            if (SettingsPageOpen && window == _owner && message == 0x0006 && (value & 0xffff) != 0)
                RefreshIntegratedSettings();
        }

        private void RefreshIntegratedSettings()
        {
            if (!SettingsPageOpen) return;
            if (_settingsCategory == GenesisIntegratedSettingsCategory.General)
            {
                var page = _generalSettings!;
                page.Refresh();
                _ = SetWindowTextW(_generalTitle, "外観設定");
                _ = SetWindowTextW(_generalNotice, GenesisAppearanceSettings.NextStartNotice + "\r\nUPnP/NATなどの詳細設定は「従来の設定を開く…」から変更できます。");
                _ = SetWindowTextW(_generalRuntime, "現在の表示: " + GenesisGeneralSettingsPage.Label(page.RuntimeMode));
                _ = SetWindowTextW(_generalConfigured, "保存済み: " + GenesisGeneralSettingsPage.Label(page.ConfiguredMode));
                _ = SetWindowTextW(_classicChoice, page.Draft == NovaGuiMode.Classic ? "Classic（選択中）" : "Classic");
                _ = SetWindowTextW(_modernChoice, page.Draft == NovaGuiMode.Modern ? "Modern（選択中）" : "Modern");
                _ = SetWindowTextW(_generalStatus, page.Message);
                _ = EnableWindow(_generalSave, page.IsDirty);
                _ = EnableWindow(_generalCancel, page.IsDirty);
                var currentVersion = NovaReleaseUpdateChecker.GetCurrentVersion().ToTagString();
                _ = SetWindowTextW(_updateTitle, $"アプリの更新（現在: {currentVersion}）");
                var updateStatusText = _checkingUpdates
                    ? "GitHub Releases で最新バージョンを確認しています…"
                    : _lastUpdateResult?.Message ?? "「更新を確認」を押すと GitHub Releases の最新バージョンを確認します。";
                _ = SetWindowTextW(_updateStatus, updateStatusText);
                _ = SetWindowTextW(_checkUpdates, _checkingUpdates ? "確認中…" : "更新を確認");
                _ = EnableWindow(_checkUpdates, !_checkingUpdates);
                var canOpenRelease = !_checkingUpdates &&
                    _lastUpdateResult?.Status == NovaUpdateCheckStatus.UpdateAvailable &&
                    NovaReleaseUpdateChecker.IsApprovedReleasePageUrl(_lastUpdateResult.ReleasePageUrl);
                _ = EnableWindow(_openReleasePage, canOpenRelease);
            }
            else RefreshAdditionalSettings();
            _ = RedrawWindow(_settingsPane, 0, 0, 0x0001 | 0x0004 | 0x0080);
        }

        private void PlaceIntegratedSettings(int width, int height)
        {
            if (_settingsPane == 0) return;
            var layout = new GenesisGuiNextSettingsLayout(width, height);
            void Place(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle b) =>
                _ = MoveWindow(control, Scale(b.Left), Scale(b.Top), Scale(b.Width), Scale(b.Height), true);
            Place(_settingsTitle, layout.Title); Place(_generalTitle, layout.CardTitle);
            Place(_generalRuntime, layout.Runtime); Place(_generalConfigured, layout.Configured);
            Place(_generalNotice, layout.Notice); Place(_generalStatus, layout.Status);
            Place(_updateTitle, layout.UpdateTitle); Place(_updateStatus, layout.UpdateStatus);
            Place(_generalCategory, GenesisGuiNextSettingsLayout.General); Place(_legacySettings, layout.Legacy);
            Place(_classicChoice, GenesisGuiNextSettingsLayout.Classic); Place(_modernChoice, GenesisGuiNextSettingsLayout.Modern);
            Place(_checkUpdates, GenesisGuiNextSettingsLayout.CheckUpdates); Place(_openReleasePage, GenesisGuiNextSettingsLayout.OpenReleasePage);
            Place(_generalSave, layout.Save); Place(_generalCancel, layout.Cancel); Place(_generalBack, layout.Back);
            PlaceSettingsCategory(layout);
        }

        private bool PaintIntegratedSettings(nint pane, nint dc)
        {
            if (!IsModern || pane != _settingsPane || !GetClientRect(pane, out var client)) return false;
            _ = FillRect(dc, ref client, _mainBrush);
            var card = new GenesisGuiNextSettingsLayout(LogicalWidth(client.Right), LogicalWidth(client.Bottom)).Card;
            var box = new Rectangle { Left = Scale(card.Left), Top = Scale(card.Top), Right = Scale(card.Right), Bottom = Scale(card.Bottom) };
            _ = FillRect(dc, ref box, _topBrush);
            using var pen = new VisualPen(dc, Scale(1), GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
            PaintNetworkSettingsFrames(dc);
            if (PathCategory)
            {
                var layout = new GenesisGuiNextSettingsLayout(LogicalWidth(client.Right), LogicalWidth(client.Bottom));
                var input = layout.PathInput(PlaybackCategory);
                using var focus = new VisualPen(dc, Scale(1), GetFocus() == _settingsPath ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, Scale(input.Left) - 1, Scale(input.Top) - 1, Scale(input.Right) + 1, Scale(input.Bottom) + 1);
                if (PlaybackCategory && _pathSettings?.PlayerMode == GenesisPlaybackPlayerMode.External)
                {
                    var argsInput = layout.ExternalPlayerArgsInput;
                    using var argsFocus = new VisualPen(dc, Scale(1), GetFocus() == _externalPlayerArgs ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                    _ = GdiRectangle(dc, Scale(argsInput.Left) - 1, Scale(argsInput.Top) - 1, Scale(argsInput.Right) + 1, Scale(argsInput.Bottom) + 1);
                }
            }
            return true;
        }

        internal bool PaintSettingsText(nint control, uint message)
        {
            if (!IsModern || !(SettingsTexts.Contains(control) || NetworkSettingsTexts.Contains(control))) return false;
            if (message == 0x0014) return true;
            if (message != WindowMessagePaint) return false;
            var dc = BeginCardPaint(control, out var paint);
            try
            {
                if (dc == 0 || !GetClientRect(control, out var box)) return true;
                _ = FillRect(dc, ref box, control == _settingsTitle ? _mainBrush : _topBrush);
                var title = control == _settingsTitle || control == _generalTitle || control == _updateTitle;
                var color = ((control == _generalStatus || control == _connectionStatus) && SettingsError ||
                    control == _natSettingsStatus && _natSettingsError ||
                    control == _updateStatus && !_checkingUpdates && _lastUpdateResult?.Status == NovaUpdateCheckStatus.Failed) ? GenesisGuiNextDesign.Warning :
                    title ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText;
                DrawCardLabel(dc, box, ReadCardControlText(control), color,
                    title ? _cardTitleFont?.Handle ?? 0 : _visualFont?.Handle ?? 0, control,
                    control == _generalNotice || control == _generalStatus || control == _updateStatus || control == _natSettingsStatus || ConnectionCategory && ConnectionBodyTexts.Contains(control) ? 0x0010u | 0x8000 : DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
                return true;
            }
            finally { _ = EndCardPaint(control, ref paint); }
        }

        private bool DrawSettingsButton(DrawItem draw)
        {
            if (!IsModern || !IsOwnerDrawButton(draw.ControlType) || !SettingsButtons.Contains(draw.ControlWindow)) return false;
            var selected = draw.ControlId == GeneralCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.General ||
                draw.ControlId == PlaybackCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Playback ||
                draw.ControlId == RecordingCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Recording ||
                draw.ControlId == ConnectionCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Connection ||
                draw.ControlId == YellowPagesCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.YellowPages ||
                draw.ControlId == NotificationsCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Notifications || draw.ControlId == ClassicChoiceId && _generalSettings?.Draft == NovaGuiMode.Classic ||
                draw.ControlId == ModernChoiceId && _generalSettings?.Draft == NovaGuiMode.Modern ||
                draw.ControlId == InternalPlayerChoiceId && PlaybackCategory && _pathSettings?.PlayerMode == GenesisPlaybackPlayerMode.Internal ||
                draw.ControlId == ExternalPlayerChoiceId && PlaybackCategory && _pathSettings?.PlayerMode == GenesisPlaybackPlayerMode.External;
            var disabled = (draw.ItemState & DrawItemDisabled) != 0;
            var hot = _hoverControl == draw.ControlWindow;
            var pressed = (draw.ItemState & 1) != 0;
            var focused = (draw.ItemState & DrawItemFocused) != 0;
            var colors = selected ? GenesisGuiNextDesign.RecordingButtonColors(true, disabled, hot, pressed, focused) :
                GenesisGuiNextDesign.TopButtonColors(draw.ControlId == GeneralSaveId, disabled, hot, pressed, focused);
            FillRectangle(draw.DeviceContext, draw.ItemRectangle, colors.Background);
            using var pen = new VisualPen(draw.DeviceContext, Scale(1), colors.Border);
            var box = draw.ItemRectangle;
            _ = GdiRectangle(draw.DeviceContext, box.Left, box.Top, box.Right, box.Bottom);
            box.Left += Scale(5); box.Right -= Scale(5);
            DrawCardLabel(draw.DeviceContext, box, ReadCardControlText(draw.ControlWindow), colors.Text, _visualFont?.Handle ?? 0,
                draw.ControlWindow, DrawTextSingleLine | DrawTextVerticalCenter | 1 | 0x8000);
            if (focused) { box.Top += Scale(3); box.Bottom -= Scale(3); _ = DrawFocusRect(draw.DeviceContext, ref box); }
            return true;
        }
    }
}

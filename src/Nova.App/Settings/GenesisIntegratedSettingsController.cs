using PeerCast.Nova.Foundation.Configuration;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// メインウィンドウ内の統合設定ページ（一般・再生・録画・通知・通信・YP）を管理するコントローラー。
/// </summary>
internal sealed partial class GenesisIntegratedSettingsController
{
    private readonly IGenesisGuiNextShellHost _host;
    private nint _settingsPane;
    private nint _settingsTitle;
    private nint _generalTitle;
    private nint _generalRuntime;
    private nint _generalConfigured;
    private nint _generalNotice;
    private nint _generalStatus;
    private nint _updateTitle;
    private nint _updateStatus;
    private nint _generalCategory;
    private nint _legacySettings;
    private nint _classicChoice;
    private nint _modernChoice;
    private nint _generalSave;
    private nint _generalCancel;
    private nint _generalBack;
    private nint _checkUpdates;
    private nint _openReleasePage;
    private GenesisGeneralSettingsPage? _generalSettings;
    private bool _checkingUpdates;
    private NovaUpdateCheckResult? _lastUpdateResult;

    internal GenesisIntegratedSettingsController(IGenesisGuiNextShellHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    internal bool IsOpen => _settingsCategory is not null;
    internal nint PaneWindow => _settingsPane;

    private nint[] SettingsButtons => _settingsPane == 0 ? [] :
    [
        _generalCategory,
        _legacySettings,
        _classicChoice,
        _modernChoice,
        _checkUpdates,
        _openReleasePage,
        _generalSave,
        _generalCancel,
        _generalBack,
        .. AdditionalSettingsButtons,
    ];

    private nint[] SettingsTexts => _settingsPane == 0 ? [] :
    [
        _settingsTitle,
        _generalTitle,
        _generalRuntime,
        _generalConfigured,
        _generalNotice,
        _generalStatus,
        _updateTitle,
        _updateStatus,
        _externalPlayerArgsLabel,
    ];

    internal nint[] VisualControls => _settingsPane == 0 ? [] :
    [
        .. SettingsButtons,
        .. SettingsTexts,
        _settingsPath,
        _externalPlayerArgs,
        .. SettingsChecks,
        .. NetworkSettingsInputs,
        .. NetworkSettingsTexts,
        _ypList,
    ];

    internal void Create(string className, nint instance)
    {
        if (!_host.IsModern || _host.ViewModel.AppearanceSettings is not { } appearance)
        {
            return;
        }

        ControllersByOwner[_host.Owner] = this;
        _settingsPane = CreateWindowExW(
            0x00010000,
            className,
            string.Empty,
            WindowStyleChild | 0x02000000,
            0,
            0,
            1,
            1,
            GetParent(_host.DetailsPane),
            0,
            instance,
            0); // WS_EX_CONTROLPARENT
        if (_settingsPane == 0)
        {
            _ = ControllersByOwner.TryRemove(_host.Owner, out _);
            return;
        }

        nint Text(string text) => CreateWindowExW(
            0,
            "STATIC",
            text,
            WindowStyleChild | WindowStyleVisible,
            0,
            0,
            1,
            1,
            _settingsPane,
            0,
            instance,
            0);

        nint Button(string text, int id) => _host.CreateOwnerDrawButton(text, _settingsPane, id, 0, instance);
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
        if (VisualControls.Any(control => control == 0) || !_host.RegisterContainerWindow(_settingsPane))
        {
            Destroy(); // 作成失敗時は従来の設定メニューをフォールバックとして残す。
            return;
        }

        foreach (var control in VisualControls)
        {
            _host.ApplyDefaultFont(control);
        }

        _generalSettings = new(appearance);
    }

    internal void Detach()
    {
        _ = ControllersByOwner.TryRemove(_host.Owner, out _);
        _generalSettings?.Release();
        _pathSettings?.Release();
        CloseNatSettingsPage();
        CloseNetworkSettingsPages();
        _settingsCategory = null;
    }

    internal void Destroy()
    {
        _ = ControllersByOwner.TryRemove(_host.Owner, out _);
        CloseNatSettingsPage();
        CloseNetworkSettingsPages();
        _generalSettings?.Release();
        _generalSettings = null;
        _pathSettings?.Release();
        _pathSettings = null;
        _settingsCategory = null;
        _host.UnregisterContainerWindow(_settingsPane);
        _host.DestroyWindowIfPresent(_settingsPane);
        _settingsPane = 0;
    }

    internal bool Open()
    {
        if (!GenesisGeneralSettingsPage.UseIntegrated(_host.IsModern, _settingsPane != 0) || _generalSettings is null)
        {
            return false;
        }

        if (!IsOpen)
        {
            if (!_generalSettings.Open())
            {
                return false;
            }

            _settingsCategoryRevision++;
            _settingsCategory = GenesisIntegratedSettingsCategory.General;
            ShowSettingsCategory();
        }

        _ = ShowWindow(_settingsPane, ShowWindowShowNoActivate);
        _host.SynchronizeMainPaneVisibility();
        Refresh();
        _host.ResetFavoriteUpdates();
        _host.SynchronizeNativeSelection();
        _ = SetFocus(NetworkCategory
            ? NetworkInitialFocus
            : PathCategory
                ? _settingsPath
                : _settingsCategory == GenesisIntegratedSettingsCategory.Notifications
                    ? _notifyNew
                    : _generalSettings.Draft == NovaGuiMode.Modern ? _modernChoice : _classicChoice);
        return true;
    }

    internal void Close(bool synchronizeVisibility = true)
    {
        if (!IsOpen)
        {
            return;
        }

        CloseNetworkSettingsPages();
        _generalSettings!.Close();
        _pathSettings!.Close();
        _settingsCategoryRevision++;
        _settingsCategory = null; // 画面を閉じると未保存下書きのみ破棄し、即時反映済みの通知設定は保持する。
        _ = ShowWindow(_settingsPane, ShowWindowHide);
        if (synchronizeVisibility)
        {
            _host.SynchronizeMainPaneVisibility();
            _host.ResetFavoriteUpdates();
            _host.SynchronizeNativeSelection();
        }
    }

    internal bool ProcessCommand(int id, nint control)
    {
        if (!IsOpen)
        {
            return false;
        }

        if (id == GeneralCategoryId && control == _generalCategory)
        {
            SelectSettingsCategory(GenesisIntegratedSettingsCategory.General);
            return true;
        }

        if (ProcessSettingsCategoryCommand(id, control))
        {
            return true;
        }

        if (id == LegacySettingsId && control == _legacySettings)
        {
            _ = GenesisChannelNotificationSettingsButtonBridge.TryOpenForOwner(_host.Owner, _legacySettings);
            Refresh();
            return true;
        }

        if (id == GeneralBackId && control == _generalBack)
        {
            Close();
            _host.RestoreViewFocusFromSettings();
            return true;
        }

        if (_settingsCategory == GenesisIntegratedSettingsCategory.General)
        {
            if (id == ClassicChoiceId && control == _classicChoice)
            {
                _generalSettings!.Select(NovaGuiMode.Classic);
            }
            else if (id == ModernChoiceId && control == _modernChoice)
            {
                _generalSettings!.Select(NovaGuiMode.Modern);
            }
            else if (id == CheckUpdatesId && control == _checkUpdates)
            {
                StartUpdateCheck();
                return true;
            }
            else if (id == OpenReleasePageId && control == _openReleasePage)
            {
                if (!NovaReleaseUpdateChecker.TryOpenApprovedReleaseUrl(_lastUpdateResult?.ReleasePageUrl))
                {
                    _ = SetWindowTextW(_updateStatus, "配布ページを開けませんでした。URL を確認してください。");
                }

                return true;
            }
            else if (id == GeneralSaveId && control == _generalSave)
            {
                _ = _generalSettings!.Save();
            }
            else if (id == GeneralCancelId && control == _generalCancel)
            {
                _generalSettings!.Cancel();
            }
            else
            {
                return false;
            }
        }
        else if (PathCategory)
        {
            if (id == GeneralSaveId && control == _generalSave)
            {
                if (ReadSettingsPath(out var path))
                {
                    if (path != _pathSettings!.Draft)
                    {
                        _pathSettings.Edit(path);
                    }

                    if (PlaybackCategory && _pathSettings.CanEditExternalPlayerArguments && ReadExternalPlayerArguments(out var args))
                    {
                        _pathSettings.EditExternalPlayerArguments(args);
                    }

                    _ = _pathSettings.Save();
                }
            }
            else if (id == GeneralCancelId && control == _generalCancel)
            {
                _pathSettings!.Cancel();
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        Refresh();
        if ((control == _generalSave || control == _generalCancel) && !(PathCategory ? _pathSettings!.IsDirty : _generalSettings!.IsDirty))
        {
            _ = SetFocus(_generalBack);
        }

        return true;
    }

    internal bool ProcessInput(int id, int code, nint control) =>
        ProcessSettingsPathInput(id, code, control) || ProcessNetworkSettingsInput(id, code, control);

    internal void RefreshOnActivation(nint window, uint message, nuint value)
    {
        if (IsOpen && window == _host.Owner && message == 0x0006 && (value & 0xffff) != 0)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (!IsOpen)
        {
            return;
        }

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
        else
        {
            RefreshAdditionalSettings();
        }

        _ = RedrawWindow(_settingsPane, 0, 0, 0x0001 | 0x0004 | 0x0080);
    }

    internal void Place(int width, int height)
    {
        if (_settingsPane == 0)
        {
            return;
        }

        var layout = new GenesisGuiNextSettingsLayout(width, height);
        void MoveControl(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle b) =>
            _ = MoveWindow(control, _host.Scale(b.Left), _host.Scale(b.Top), _host.Scale(b.Width), _host.Scale(b.Height), true);
        MoveControl(_settingsTitle, layout.Title);
        MoveControl(_generalTitle, layout.CardTitle);
        MoveControl(_generalRuntime, layout.Runtime);
        MoveControl(_generalConfigured, layout.Configured);
        MoveControl(_generalNotice, layout.Notice);
        MoveControl(_generalStatus, layout.Status);
        MoveControl(_updateTitle, layout.UpdateTitle);
        MoveControl(_updateStatus, layout.UpdateStatus);
        MoveControl(_generalCategory, GenesisGuiNextSettingsLayout.General);
        MoveControl(_legacySettings, layout.Legacy);
        MoveControl(_classicChoice, GenesisGuiNextSettingsLayout.Classic);
        MoveControl(_modernChoice, GenesisGuiNextSettingsLayout.Modern);
        MoveControl(_checkUpdates, GenesisGuiNextSettingsLayout.CheckUpdates);
        MoveControl(_openReleasePage, GenesisGuiNextSettingsLayout.OpenReleasePage);
        MoveControl(_generalSave, layout.Save);
        MoveControl(_generalCancel, layout.Cancel);
        MoveControl(_generalBack, layout.Back);
        PlaceSettingsCategory(layout);
    }

    internal bool PaintBackground(nint pane, nint dc)
    {
        if (!_host.IsModern || pane != _settingsPane || !GetClientRect(pane, out var client))
        {
            return false;
        }

        _ = FillRect(dc, ref client, _host.MainBrush);
        var card = new GenesisGuiNextSettingsLayout(_host.LogicalWidth(client.Right), _host.LogicalWidth(client.Bottom)).Card;
        var box = new Win32Rect
        {
            Left = _host.Scale(card.Left),
            Top = _host.Scale(card.Top),
            Right = _host.Scale(card.Right),
            Bottom = _host.Scale(card.Bottom),
        };
        _ = FillRect(dc, ref box, _host.TopBrush);
        using var pen = new GenesisVisualPen(dc, _host.Scale(1), GenesisGuiNextDesign.Border);
        _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
        PaintNetworkSettingsFrames(dc);
        if (PathCategory)
        {
            var layout = new GenesisGuiNextSettingsLayout(_host.LogicalWidth(client.Right), _host.LogicalWidth(client.Bottom));
            var input = layout.PathInput(PlaybackCategory);
            using var focus = new GenesisVisualPen(dc, _host.Scale(1), GetFocus() == _settingsPath ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, _host.Scale(input.Left) - 1, _host.Scale(input.Top) - 1, _host.Scale(input.Right) + 1, _host.Scale(input.Bottom) + 1);
            if (PlaybackCategory && _pathSettings?.PlayerMode == GenesisPlaybackPlayerMode.External)
            {
                var argsInput = layout.ExternalPlayerArgsInput;
                using var argsFocus = new GenesisVisualPen(dc, _host.Scale(1), GetFocus() == _externalPlayerArgs ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, _host.Scale(argsInput.Left) - 1, _host.Scale(argsInput.Top) - 1, _host.Scale(argsInput.Right) + 1, _host.Scale(argsInput.Bottom) + 1);
            }
        }

        return true;
    }

    internal bool PaintText(nint control, uint message)
    {
        if (!_host.IsModern || !(SettingsTexts.Contains(control) || NetworkSettingsTexts.Contains(control)))
        {
            return false;
        }

        if (message == 0x0014)
        {
            return true;
        }

        if (message != WindowMessagePaint)
        {
            return false;
        }

        var dc = BeginPaint(control, out var paint);
        try
        {
            if (dc == 0 || !GetClientRect(control, out var box))
            {
                return true;
            }

            _ = FillRect(dc, ref box, control == _settingsTitle ? _host.MainBrush : _host.TopBrush);
            var title = control == _settingsTitle || control == _generalTitle || control == _updateTitle;
            var color = ((control == _generalStatus || control == _connectionStatus) && SettingsError ||
                control == _natSettingsStatus && _natSettingsError ||
                control == _updateStatus && !_checkingUpdates && _lastUpdateResult?.Status == NovaUpdateCheckStatus.Failed)
                ? GenesisGuiNextDesign.Warning
                : title ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText;
            _host.DrawCardLabel(
                dc,
                box,
                _host.ReadControlText(control),
                color,
                title ? _host.CardTitleFontHandle : _host.VisualFontHandle,
                control,
                control == _generalNotice || control == _generalStatus || control == _updateStatus || control == _natSettingsStatus || ConnectionCategory && ConnectionBodyTexts.Contains(control)
                    ? 0x0010u | 0x8000
                    : DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
            return true;
        }
        finally
        {
            _ = EndPaint(control, ref paint);
        }
    }

    internal bool DrawButton(Win32DrawItem draw)
    {
        if (!_host.IsModern || !IsOwnerDrawButton(draw.ControlType) || !SettingsButtons.Contains(draw.ControlWindow))
        {
            return false;
        }

        var selected = draw.ControlId == GeneralCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.General ||
            draw.ControlId == PlaybackCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Playback ||
            draw.ControlId == RecordingCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Recording ||
            draw.ControlId == ConnectionCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Connection ||
            draw.ControlId == YellowPagesCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.YellowPages ||
            draw.ControlId == NotificationsCategoryId && _settingsCategory == GenesisIntegratedSettingsCategory.Notifications ||
            draw.ControlId == ClassicChoiceId && _generalSettings?.Draft == NovaGuiMode.Classic ||
            draw.ControlId == ModernChoiceId && _generalSettings?.Draft == NovaGuiMode.Modern ||
            draw.ControlId == InternalPlayerChoiceId && PlaybackCategory && _pathSettings?.PlayerMode == GenesisPlaybackPlayerMode.Internal ||
            draw.ControlId == ExternalPlayerChoiceId && PlaybackCategory && _pathSettings?.PlayerMode == GenesisPlaybackPlayerMode.External;
        var disabled = (draw.ItemState & DrawItemDisabled) != 0;
        var hot = _host.HoverControl == draw.ControlWindow;
        var pressed = (draw.ItemState & 1) != 0;
        var focused = (draw.ItemState & DrawItemFocused) != 0;
        var colors = selected
            ? GenesisGuiNextDesign.RecordingButtonColors(true, disabled, hot, pressed, focused)
            : GenesisGuiNextDesign.TopButtonColors(draw.ControlId == GeneralSaveId, disabled, hot, pressed, focused);
        _host.FillRectangle(draw.DeviceContext, draw.ItemRectangle, colors.Background);
        using var pen = new GenesisVisualPen(draw.DeviceContext, _host.Scale(1), colors.Border);
        var box = draw.ItemRectangle;
        _ = GdiRectangle(draw.DeviceContext, box.Left, box.Top, box.Right, box.Bottom);
        box.Left += _host.Scale(5);
        box.Right -= _host.Scale(5);
        _host.DrawCardLabel(
            draw.DeviceContext,
            box,
            _host.ReadControlText(draw.ControlWindow),
            colors.Text,
            _host.VisualFontHandle,
            draw.ControlWindow,
            DrawTextSingleLine | DrawTextVerticalCenter | 1 | 0x8000);
        if (focused)
        {
            box.Top += _host.Scale(3);
            box.Bottom -= _host.Scale(3);
            _ = DrawFocusRect(draw.DeviceContext, ref box);
        }

        return true;
    }
}

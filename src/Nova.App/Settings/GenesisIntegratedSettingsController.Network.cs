using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisIntegratedSettingsController
{
    private static int _nextNetworkSettingsToken;
    private GenesisConnectionSettingsPage? _connectionPage;
    private GenesisYellowPageSettingsPage? _ypPage;
    private GuiNextNetworkSettingsViewModel? _natSettingsPage;
    private nint _connectionCategory;
    private nint _yellowPagesCategory;
    private nint _connectionIpv6;
    private nint _connectionWan;
    private nint _connectionAutoMapping;
    private nint _natSettingsStatus;
    private nint _ypEnabled;
    private nint _ypList;
    private nint _ypNew;
    private nint _ypRemove;
    private nint _ypToggle;
    private readonly nint[] _connectionInputs = new nint[6];
    private readonly nint[] _connectionLabels = new nint[6];
    private readonly nint[] _ypInputs = new nint[5];
    private readonly nint[] _ypLabels = new nint[5];
    private IReadOnlyList<GenesisYellowPageDefinition> _ypShown = [];
    private bool _updatingNetworkSettings;
    private bool _natSettingsError;
    private string? _networkInputError;
    private readonly nuint _networkSettingsToken = unchecked((nuint)Interlocked.Increment(ref _nextNetworkSettingsToken));
    private Task<GenesisGuiNextConnectionSaveResult>? _connectionSave;

    private nint[] NetworkSettingsInputs => [.. _connectionInputs, .. _ypInputs];
    private nint[] NetworkSettingsTexts => [.. _connectionLabels, .. _ypLabels, _natSettingsStatus, .. ConnectionBodyTexts];
    private nint[] NetworkSettingsButtons => [_connectionCategory, _yellowPagesCategory, _ypNew, _ypRemove, _ypToggle];
    private bool NetworkCategory => _settingsCategory is GenesisIntegratedSettingsCategory.Connection or GenesisIntegratedSettingsCategory.YellowPages;
    private bool ConnectionCategory => _settingsCategory == GenesisIntegratedSettingsCategory.Connection;
    private bool YellowPagesCategory => _settingsCategory == GenesisIntegratedSettingsCategory.YellowPages;
    private bool NetworkSettingsError => _networkInputError is not null || (ConnectionCategory ? _connectionPage?.IsError == true : _ypPage?.IsError == true);
    private nint NetworkInitialFocus => ConnectionCategory ? _connectionInputs[0] : _ypList;

    private void CreateNetworkSettingsCategories(nint instance)
    {
        nint Button(string text, int id) => _host.CreateOwnerDrawButton(text, _settingsPane, id, 0, instance);
        nint Control(string kind, string text, int id, uint style) => CreateWindowExW(
            0,
            kind,
            text,
            WindowStyleChild | WindowStyleVisible | style,
            0,
            0,
            1,
            1,
            _settingsPane,
            (nint)id,
            instance,
            0);
        nint Check(string text, int id)
        {
            var h = Control("BUTTON", text, id, WindowStyleTabStop | 3);
            _ = SetWindowTheme(h, " ", " ");
            return h;
        }

        _connectionCategory = Button("通信", ConnectionCategoryId);
        _yellowPagesCategory = Button("YP", YellowPagesCategoryId);
        CreateConnectionBody(instance);
        _connectionIpv6 = Check("IPv6を使う（未対応）", ConnectionIpv6Id);
        _connectionWan = Check("WAN Relayを公開", ConnectionWanId);
        _ = SetParent(_connectionWan, _connectionBody);
        _connectionAutoMapping = Check(GenesisNetworkSettingsWindow.NatCheckboxLabel, ConnectionAutoMappingId);
        _natSettingsStatus = Control("STATIC", string.Empty, 0, 0);
        _ = SetParent(_natSettingsStatus, _connectionBody);
        _ypEnabled = Check("有効（保存時に反映）", YpEnabledId);
        _ypNew = Button("新規", YpNewId);
        _ypRemove = Button("削除…", YpRemoveId);
        _ypToggle = Button("有効 / 無効", YpToggleId);
        _ypList = Control("LISTBOX", "登録YP", YpListId, WindowStyleTabStop | 0x00200000 | 0x0010 | 0x0040 | 1); // 非ソート・固定オーナードロー LISTBOX
        _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_ypList, "登録済み Yellow Page 一覧");
        _ = SendMessageW(_ypList, 0x01A0, 0, _host.Scale(24)); // LB_SETITEMHEIGHT
        var connectionLabels = new[] { "待受ポート", "IPv4上り (kbps)", "IPv6上り (kbps・未対応)", "合計リレー数", "配信毎リレー数", "視聴毎リレー数" };
        var ypLabels = new[] { "ID", "YP名", "Index URL", "Tracker URL", "Namespace" };
        for (var i = 0; i < _connectionInputs.Length; i++)
        {
            _connectionLabels[i] = Control("STATIC", connectionLabels[i], 0, 0);
            _connectionInputs[i] = Control("EDIT", "", ConnectionInputId + i, WindowStyleTabStop | 0x80);
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_connectionInputs[i], connectionLabels[i]);
            _ = SetParent(_connectionLabels[i], _connectionBody);
            _ = SetParent(_connectionInputs[i], _connectionBody);
            _ = SendMessageW(_connectionInputs[i], 0x00C5, 32, 0);
        }

        for (var i = 0; i < _ypInputs.Length; i++)
        {
            _ypLabels[i] = Control("STATIC", ypLabels[i], 0, 0);
            _ypInputs[i] = Control("EDIT", "", YpInputId + i, WindowStyleTabStop | 0x80);
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_ypInputs[i], ypLabels[i]);
            _ = SendMessageW(_ypInputs[i], 0x00C5, 32767, 0);
        }

        if (_host.ViewModel.ConnectionSettings is { } connection)
        {
            _connectionPage = new(connection);
        }

        if (_host.ViewModel.YellowPageSettings is { } yp)
        {
            _ypPage = new(yp);
        }

        // カテゴリ切替後も完了通知を受け取れるよう、設定ペイン単位でアダプターを保持する。
        if (_host.ViewModel.NetworkSettingsAdapter is { } adapter)
        {
            var pane = _settingsPane;
            _natSettingsPage = new(adapter, generation =>
            {
                if (!_host.IsDetached)
                {
                    _ = PostMessageW(pane, NatSettingsSaveCompleted, _networkSettingsToken, (nint)generation);
                }
            });
        }
    }

    private void CloseNetworkSettingsPages()
    {
        _connectionPage?.Close();
        _ypPage?.Close();
    }

    private void CloseNatSettingsPage()
    {
        _natSettingsPage?.Close();
        _natSettingsPage = null;
    }

    internal void CompleteNatSave(nuint token, long generation)
    {
        if (token == _networkSettingsToken && !_host.IsDetached &&
            ConnectionCategory && _natSettingsPage?.CanAcceptCallback(generation) == true)
        {
            Refresh();
        }
    }

    private void ShowNetworkSettingsCategory()
    {
        void Show(nint h, bool show) => _ = ShowWindow(h, show ? ShowWindowShowNoActivate : ShowWindowHide);
        foreach (var h in _connectionInputs.Concat(_connectionLabels))
        {
            Show(h, ConnectionCategory);
        }

        foreach (var h in _ypInputs.Concat(_ypLabels).Concat([_ypList, _ypNew, _ypRemove, _ypToggle, _ypEnabled]))
        {
            Show(h, YellowPagesCategory);
        }

        Show(_connectionIpv6, ConnectionCategory);
        Show(_connectionWan, ConnectionCategory);
        Show(_connectionAutoMapping, ConnectionCategory);
        Show(_natSettingsStatus, ConnectionCategory);
        Show(_connectionBody, ConnectionCategory);
        _connectionScroll = 0;
        if (NetworkCategory)
        {
            Show(_generalRuntime, false);
            Show(_generalConfigured, false);
            Show(_generalNotice, false);
            Show(_generalStatus, !ConnectionCategory);
            Show(_generalSave, true);
            Show(_generalCancel, true);
        }

        _ = EnableWindow(_connectionCategory, _connectionPage is not null);
        _ = EnableWindow(_yellowPagesCategory, _ypPage is not null);
        _networkInputError = null;
        _ypShown = [];
    }

    private string ReadNetworkInput(nint control)
    {
        var length = GetWindowTextLengthW(control);
        if (length is < 0 or > 32767)
        {
            throw new InvalidOperationException();
        }

        var buffer = new char[length + 1];
        if (GetWindowTextW(control, buffer, buffer.Length) != length)
        {
            throw new InvalidOperationException();
        }

        return new string(buffer, 0, length);
    }

    private bool ReadNetworkDraft()
    {
        try
        {
            if (ConnectionCategory)
            {
                var v = _connectionInputs.Select(ReadNetworkInput).ToArray();
                _connectionPage!.Edit(new(
                    v[0],
                    SendMessageW(_connectionIpv6, 0xF0, 0, 0) == 1,
                    v[1],
                    v[2],
                    v[3],
                    v[4],
                    v[5],
                    SendMessageW(_connectionWan, 0xF0, 0, 0) == 1));
            }
            else if (YellowPagesCategory)
            {
                var v = _ypInputs.Select(ReadNetworkInput).ToArray();
                _ypPage!.Edit(new(v[0], v[1], v[2], v[3], SendMessageW(_ypEnabled, 0xF0, 0, 0) == 1, v[4]));
            }

            _networkInputError = null;
            return true;
        }
        catch
        {
            _networkInputError = "入力を読み取れませんでした。保存値は変更していません。";
            return false;
        }
    }

    private bool ProcessNetworkSettingsInput(int id, int code, nint control)
    {
        if (!IsOpen || !NetworkCategory || _updatingNetworkSettings)
        {
            return false;
        }

        if (control == _ypList && YellowPagesCategory && id == YpListId && code == 1)
        {
            var index = SendMessageW(_ypList, 0x0188, 0, 0).ToInt32();
            _ypPage!.Select(index >= 0 && index < _ypShown.Count ? _ypShown[index].Id : null);
            Refresh();
            return true;
        }

        if (code != 0x0300 || !NetworkSettingsInputs.Contains(control))
        {
            return false;
        }

        ReadNetworkDraft();
        Refresh();
        return true;
    }

    private bool ProcessNetworkSettingsCommand(int id, nint control)
    {
        if (id == ConnectionCategoryId && control == _connectionCategory)
        {
            SelectSettingsCategory(GenesisIntegratedSettingsCategory.Connection);
            return true;
        }

        if (id == YellowPagesCategoryId && control == _yellowPagesCategory)
        {
            SelectSettingsCategory(GenesisIntegratedSettingsCategory.YellowPages);
            return true;
        }

        if (!NetworkCategory)
        {
            return false;
        }

        if (ConnectionCategory && id == ConnectionAutoMappingId && control == _connectionAutoMapping)
        {
            if (_natSettingsPage is { } nat && nat.Refresh().CanEdit)
            {
                _ = nat.SetEnabledAsync(SendMessageW(_connectionAutoMapping, 0xF0, 0, 0) == 1);
            }
        }
        else if (control == _connectionIpv6 || control == _connectionWan || control == _ypEnabled)
        {
            ReadNetworkDraft();
        }
        else if (id == GeneralCancelId && control == _generalCancel)
        {
            _networkInputError = null;
            if (ConnectionCategory)
            {
                _connectionPage!.Reload();
            }
            else
            {
                _ypPage!.Cancel();
            }
        }
        else if (id == GeneralSaveId && control == _generalSave)
        {
            if (!ReadNetworkDraft())
            {
                Refresh();
                return true;
            }

            if (ConnectionCategory)
            {
                if (_connectionPage!.BeginSave() is { } save)
                {
                    _connectionSave = save;
                    _ = PostNetworkSettingsCompletionAsync(save, _settingsPane);
                }
            }
            else
            {
                _ = _ypPage!.Save();
            }
        }
        else if (YellowPagesCategory && id == YpNewId && control == _ypNew)
        {
            _ypPage!.Select(null);
        }
        else if (YellowPagesCategory && id == YpToggleId && control == _ypToggle)
        {
            _ = _ypPage!.Toggle();
        }
        else if (YellowPagesCategory && id == YpRemoveId && control == _ypRemove)
        {
            if (_ypPage!.DeletionTarget is { } target &&
                MessageBoxW(_host.Owner, $"YP「{target.Name}」（ID: {target.Id}）を削除しますか？", "YP削除の確認", 0x24) == 6)
            {
                _ = _ypPage.Remove(target);
            }
        }
        else
        {
            return false;
        }

        Refresh();
        return true;
    }

    private async Task PostNetworkSettingsCompletionAsync(Task<GenesisGuiNextConnectionSaveResult> save, nint pane)
    {
        try
        {
            await save.ConfigureAwait(false);
        }
        catch
        {
            // UI 側で安全な完了状態を取得するためここでは握り潰す。
        }

        if (!_host.IsDetached)
        {
            _ = PostMessageW(pane, NetworkSettingsSaveCompleted, _networkSettingsToken, 0);
        }
    }

    internal void CompleteNetworkSave(nuint token)
    {
        if (token != _networkSettingsToken || _host.IsDetached || _connectionSave is not { } save)
        {
            return;
        }

        _connectionPage?.CompleteSave(save);
        if (save.IsCompleted)
        {
            _connectionSave = null;
        }

        if (ConnectionCategory)
        {
            Refresh();
        }
    }

    internal void CompleteLivePublicApply(nuint token)
    {
        if (token == _networkSettingsToken && !_host.IsDetached && ConnectionCategory)
        {
            Refresh();
        }
    }

    private void RefreshNetworkSettings()
    {
        void Text(nint h, string value) => _ = SetWindowTextW(h, value);
        void Input(nint h, string value)
        {
            if (GetWindowTextLengthW(h) != value.Length || ReadNetworkInput(h) != value)
            {
                Text(h, value);
            }
        }

        _updatingNetworkSettings = true;
        try
        {
            if (ConnectionCategory)
            {
                var page = _connectionPage!;
                page.Refresh();
                Text(_generalTitle, "接続・帯域・リレー設定");
                Text(_connectionNotice, ConnectionNotice);
                Text(_connectionRuntime, "現在適用中: " + ConnectionSummary(page.Active));
                Text(_connectionSaved, "保存済み: " + ConnectionSummary(page.Saved));
                Text(_connectionStatus, page.Message);
                var v = page.Input;
                var values = new[] { v.ListenPort, v.TotalUpstreamIpv4Kbps, v.TotalUpstreamIpv6Kbps, v.MaxRelaysTotal, v.MaxRelaysPerBroadcastChannel, v.MaxRelaysPerViewingChannel };
                for (var i = 0; i < values.Length; i++)
                {
                    Input(_connectionInputs[i], values[i]);
                    _ = EnableWindow(_connectionInputs[i], i != 2 && !page.IsSaving);
                }

                _ = SendMessageW(_connectionIpv6, 0xF1, v.UseIpv6 ? 1u : 0u, 0);
                _ = EnableWindow(_connectionIpv6, false);
                _ = SendMessageW(_connectionWan, 0xF1, v.WanRelayEnabled ? 1u : 0u, 0);
                _ = EnableWindow(_connectionWan, !page.IsSaving);
                var nat = _natSettingsPage?.Refresh();
                _natSettingsError = nat?.Failure == NovaApplicationNetworkFailure.PersistenceFailed;
                _ = EnableWindow(_connectionAutoMapping, nat?.CanEdit == true);
                _ = SendMessageW(_connectionAutoMapping, 0xF1, nat?.Checked == true ? 1u : 0u, 0);
                Text(_natSettingsStatus, nat?.IntegratedSettings ?? GuiNextNetworkSettingsPresentation.AutoMappingUnavailable);
                _ = EnableWindow(_generalSave, !page.IsSaving && !page.Conflict);
                _ = EnableWindow(_generalCancel, !page.IsSaving);
            }
            else
            {
                var page = _ypPage!;
                page.Refresh();
                Text(_generalTitle, "YP設定 — 操作ごとに保存");
                Text(_generalStatus, page.Message);
                var entries = page.Entries;
                if (!_ypShown.SequenceEqual(entries))
                {
                    _ypShown = entries;
                    _ = SendMessageW(_ypList, 0x0184, 0, 0); // LB_RESETCONTENT
                    foreach (var entry in entries)
                    {
                        _ = SendMessageTextW(_ypList, 0x0180, 0, YpListLabel(entry));
                    }
                }

                var index = entries.ToList().FindIndex(e => e.Id == page.SelectedId);
                _ = SendMessageW(_ypList, 0x0186, unchecked((nuint)index), 0);
                var v = page.Draft;
                var values = new[] { v.Id, v.Name, v.IndexUrl, v.FallbackTrackerUrl, v.Namespace };
                for (var i = 0; i < values.Length; i++)
                {
                    Input(_ypInputs[i], values[i] ?? "");
                }

                _ = EnableWindow(_ypInputs[0], page.SelectedId is null);
                _ = SendMessageW(_ypEnabled, 0xF1, v.IsEnabled ? 1u : 0u, 0);
                _ = EnableWindow(_generalSave, !page.Conflict);
                _ = EnableWindow(_generalCancel, true);
                _ = EnableWindow(_ypToggle, page.SelectedId is not null && !page.Conflict);
                _ = EnableWindow(_ypRemove, page.SelectedId is not null && !page.Conflict);
            }

            if (_networkInputError is { } error)
            {
                Text(ConnectionCategory ? _connectionStatus : _generalStatus, error);
                _ = EnableWindow(_generalSave, false);
            }

            if (ConnectionCategory)
            {
                PlaceConnectionBody();
            }
        }
        finally
        {
            _updatingNetworkSettings = false;
        }
    }

    private static string ConnectionSummary(GenesisGuiNextConnectionPreferences v) =>
        GenesisConnectionSettingsText.Summary(v);

    private static string YpListLabel(GenesisYellowPageDefinition e) =>
        $"{(e.IsEnabled ? "有効" : "無効")} · {e.Name} ({e.Id})";

    private void PlaceNetworkSettingsCategory(GenesisGuiNextSettingsLayout layout)
    {
        void MoveControl(nint h, GenesisGuiNextAppShellPlacement.WindowRectangle b) =>
            _ = MoveWindow(h, _host.Scale(b.Left), _host.Scale(b.Top), _host.Scale(b.Width), _host.Scale(b.Height), true);
        MoveControl(_connectionCategory, GenesisGuiNextSettingsLayout.ConnectionTab);
        MoveControl(_yellowPagesCategory, GenesisGuiNextSettingsLayout.YellowPagesTab);
        if (!NetworkCategory)
        {
            return;
        }

        var n = new GenesisGuiNextNetworkSettingsLayout(layout.Width);
        if (ConnectionCategory)
        {
            MoveControl(_generalTitle, n.ConnectionTitle);
            MoveControl(_connectionIpv6, n.Ipv6);
            MoveControl(_connectionAutoMapping, n.AutoMapping);
            MoveControl(_connectionBody, n.Body(layout));
            PlaceConnectionBody();
        }
        else
        {
            MoveControl(_generalStatus, n.YpStatus);
            MoveControl(_ypList, GenesisGuiNextNetworkSettingsLayout.YpList);
            MoveControl(_ypEnabled, GenesisGuiNextNetworkSettingsLayout.Enabled);
            MoveControl(_ypNew, GenesisGuiNextNetworkSettingsLayout.New(layout.Height));
            MoveControl(_ypRemove, GenesisGuiNextNetworkSettingsLayout.Remove(layout.Height));
            MoveControl(_ypToggle, GenesisGuiNextNetworkSettingsLayout.Toggle(layout.Height));
            for (var i = 0; i < 5; i++)
            {
                MoveControl(_ypInputs[i], n.YpInput(i));
                MoveControl(_ypLabels[i], GenesisGuiNextNetworkSettingsLayout.YpLabel(i));
            }

            _ = SendMessageW(_ypList, 0x01A0, 0, _host.Scale(24));
        }
    }

    private void PaintNetworkSettingsFrames(nint dc)
    {
        if (!YellowPagesCategory)
        {
            return;
        }

        foreach (var h in ConnectionCategory ? _connectionInputs : _ypInputs)
        {
            if (!GetWindowRect(h, out var b))
            {
                continue;
            }

            var point = new Win32Point { X = b.Left, Y = b.Top };
            _ = ScreenToClient(_settingsPane, ref point);
            using var pen = new GenesisVisualPen(dc, _host.Scale(1), GetFocus() == h ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, point.X - 1, point.Y - 1, point.X + b.Right - b.Left + 1, point.Y + b.Bottom - b.Top + 1);
        }
    }

    internal bool DrawYpList(Win32DrawItem draw)
    {
        if (!_host.IsModern || draw.ControlWindow != _ypList)
        {
            return false;
        }

        var selected = (draw.ItemState & 1) != 0;
        _host.FillRectangle(draw.DeviceContext, draw.ItemRectangle, selected ? GenesisGuiNextDesign.Selected : GenesisGuiNextDesign.Input);
        if (draw.ItemId < _ypShown.Count)
        {
            var b = draw.ItemRectangle;
            b.Left += _host.Scale(4);
            b.Right -= _host.Scale(4);
            _host.DrawCardLabel(
                draw.DeviceContext,
                b,
                YpListLabel(_ypShown[(int)draw.ItemId]),
                GenesisGuiNextDesign.Text,
                _host.VisualFontHandle,
                _ypList,
                DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
        }

        if ((draw.ItemState & DrawItemFocused) != 0)
        {
            var b = draw.ItemRectangle;
            _ = DrawFocusRect(draw.DeviceContext, ref b);
        }

        return true;
    }
}

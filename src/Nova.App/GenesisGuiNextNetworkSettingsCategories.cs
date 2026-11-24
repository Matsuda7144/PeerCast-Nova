using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const int ConnectionCategoryId = 1200, YellowPagesCategoryId = 1201, ConnectionIpv6Id = 1202,
        ConnectionInputId = 1210, ConnectionAutoMappingId = 1216, ConnectionWanId = 1217, YpInputId = 1220, YpListId = 1230, YpEnabledId = 1231,
        YpNewId = 1232, YpRemoveId = 1233, YpToggleId = 1234;
    private const uint NetworkSettingsSaveCompleted = 0x8730, NatSettingsSaveCompleted = 0x8731,
        LivePublicSettingsApplied = 0x8732;
    private static int _nextNetworkSettingsToken;

    private sealed partial class AppShellState
    {
        private GenesisConnectionSettingsPage? _connectionPage;
        private GenesisYellowPageSettingsPage? _ypPage;
        private GuiNextNetworkSettingsViewModel? _natSettingsPage;
        private nint _connectionCategory, _yellowPagesCategory, _connectionIpv6, _connectionWan, _connectionAutoMapping, _natSettingsStatus, _ypEnabled, _ypList, _ypNew, _ypRemove, _ypToggle;
        private readonly nint[] _connectionInputs = new nint[6], _connectionLabels = new nint[6], _ypInputs = new nint[5], _ypLabels = new nint[5];
        private IReadOnlyList<GenesisYellowPageDefinition> _ypShown = [];
        private bool _updatingNetworkSettings, _natSettingsError;
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
            nint Button(string text, int id) => CreateRecordingModeButton(text, _settingsPane, id, 0, instance);
            nint Control(string kind, string text, int id, uint style) => CreateWindowExW(0, kind, text,
                WindowStyleChild | WindowStyleVisible | style, 0, 0, 1, 1, _settingsPane, (nint)id, instance, 0);
            nint Check(string text, int id)
            {
                var h = Control("BUTTON", text, id, WindowStyleTabStop | 3);
                _ = SetWindowTheme(h, " ", " ");
                return h;
            }
            _connectionCategory = Button("通信", ConnectionCategoryId);
            _yellowPagesCategory = Button("YP", YellowPagesCategoryId);
            CreateConnectionBody(instance);
            _connectionIpv6 = Check("IPv6を使う（次回用）", ConnectionIpv6Id);
            _connectionWan = Check("WAN Relayを公開（次回起動）", ConnectionWanId);
            _ = SetConnectionParent(_connectionWan, _connectionBody);
            _connectionAutoMapping = Check(GenesisNetworkSettingsWindow.NatCheckboxLabel, ConnectionAutoMappingId);
            _natSettingsStatus = Control("STATIC", string.Empty, 0, 0);
            _ = SetConnectionParent(_natSettingsStatus, _connectionBody);
            _ypEnabled = Check("有効（保存時に反映）", YpEnabledId);
            _ypNew = Button("新規", YpNewId); _ypRemove = Button("削除…", YpRemoveId); _ypToggle = Button("有効 / 無効", YpToggleId);
            _ypList = Control("LISTBOX", "登録YP", YpListId, WindowStyleTabStop | 0x00200000 | 0x0010 | 0x0040 | 1); // native fixed owner draw, no sorting
            _ = SendMessageW(_ypList, 0x01A0, 0, Scale(24)); // LB_SETITEMHEIGHT
            var connectionLabels = new[] { "待受ポート", "IPv4上り (kbps)", "IPv6上り (kbps)", "合計リレー数", "配信毎リレー数", "視聴毎リレー数" };
            var ypLabels = new[] { "ID", "YP名", "Index URL", "Tracker URL", "Namespace" };
            for (var i = 0; i < _connectionInputs.Length; i++)
            {
                _connectionLabels[i] = Control("STATIC", connectionLabels[i], 0, 0);
                _connectionInputs[i] = Control("EDIT", "", ConnectionInputId + i, WindowStyleTabStop | 0x80);
                _ = SetConnectionParent(_connectionLabels[i], _connectionBody);
                _ = SetConnectionParent(_connectionInputs[i], _connectionBody);
                _ = SendMessageW(_connectionInputs[i], 0x00C5, 32, 0);
            }
            for (var i = 0; i < _ypInputs.Length; i++)
            {
                _ypLabels[i] = Control("STATIC", ypLabels[i], 0, 0);
                _ypInputs[i] = Control("EDIT", "", YpInputId + i, WindowStyleTabStop | 0x80);
                _ = SendMessageW(_ypInputs[i], 0x00C5, 32767, 0);
            }
            if (_viewModel.ConnectionSettings is { } connection) _connectionPage = new(connection);
            if (_viewModel.YellowPageSettings is { } yp) _ypPage = new(yp);
            // Keep the completion target alive for the pane, including across category switches.
            if (_viewModel.NetworkSettingsAdapter is { } adapter)
            {
                var pane = _settingsPane;
                _natSettingsPage = new(adapter, generation =>
                {
                    if (Volatile.Read(ref _detached) == 0)
                        _ = PostMessageW(pane, NatSettingsSaveCompleted, _networkSettingsToken, (nint)generation);
                });
            }
        }

        private void CloseNetworkSettingsPages()
        {
            _connectionPage?.Close(); _ypPage?.Close();
        }

        private void CloseNatSettingsPage()
        {
            _natSettingsPage?.Close();
            _natSettingsPage = null;
        }
        internal void CompleteNatSettingsSave(nuint token, long generation)
        {
            if (token == _networkSettingsToken && Volatile.Read(ref _detached) == 0 &&
                ConnectionCategory && _natSettingsPage?.CanAcceptCallback(generation) == true)
                RefreshIntegratedSettings();
        }
        private void ShowNetworkSettingsCategory()
        {
            void Show(nint h, bool show) => _ = ShowWindow(h, show ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var h in _connectionInputs.Concat(_connectionLabels)) Show(h, ConnectionCategory);
            foreach (var h in _ypInputs.Concat(_ypLabels).Concat(new[] { _ypList, _ypNew, _ypRemove, _ypToggle, _ypEnabled })) Show(h, YellowPagesCategory);
            Show(_connectionIpv6, ConnectionCategory);
            Show(_connectionWan, ConnectionCategory);
            Show(_connectionAutoMapping, ConnectionCategory);
            Show(_natSettingsStatus, ConnectionCategory);
            Show(_connectionBody, ConnectionCategory);
            _connectionScroll = 0;
            if (NetworkCategory)
            {
                Show(_generalRuntime, false); Show(_generalConfigured, false);
                Show(_generalNotice, false); Show(_generalStatus, !ConnectionCategory);
                Show(_generalSave, true); Show(_generalCancel, true);
            }
            _ = EnableWindow(_connectionCategory, _connectionPage is not null);
            _ = EnableWindow(_yellowPagesCategory, _ypPage is not null);
            _networkInputError = null;
            _ypShown = []; // force list selection to follow the freshly opened draft
        }

        private string ReadNetworkInput(nint control)
        {
            var length = GetSettingsTextLength(control);
            if (length is < 0 or > 32767) throw new InvalidOperationException();
            var buffer = new char[length + 1];
            if (GetWindowTextW(control, buffer, buffer.Length) != length) throw new InvalidOperationException();
            return new string(buffer, 0, length);
        }
        private bool ReadNetworkDraft()
        {
            try
            {
                if (ConnectionCategory)
                {
                    var v = _connectionInputs.Select(ReadNetworkInput).ToArray();
                    _connectionPage!.Edit(new(v[0], SendMessageW(_connectionIpv6, 0xF0, 0, 0) == 1, v[1], v[2], v[3], v[4], v[5],
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
            catch { _networkInputError = "入力を読み取れませんでした。保存値は変更していません。"; return false; }
        }
        private bool ProcessNetworkSettingsInput(int id, int code, nint control)
        {
            if (!SettingsPageOpen || !NetworkCategory || _updatingNetworkSettings) return false;
            if (control == _ypList && YellowPagesCategory && id == YpListId && code == 1)
            {
                var index = SendMessageW(_ypList, 0x0188, 0, 0).ToInt32();
                _ypPage!.Select(index >= 0 && index < _ypShown.Count ? _ypShown[index].Id : null);
                RefreshIntegratedSettings(); return true;
            }
            if (code != 0x0300 || !NetworkSettingsInputs.Contains(control)) return false;
            ReadNetworkDraft(); // read exactly, never replace input with a truncated buffer
            RefreshIntegratedSettings();
            return true;
        }

        private bool ProcessNetworkSettingsCommand(int id, nint control)
        {
            if (id == ConnectionCategoryId && control == _connectionCategory) { SelectSettingsCategory(GenesisIntegratedSettingsCategory.Connection); return true; }
            if (id == YellowPagesCategoryId && control == _yellowPagesCategory) { SelectSettingsCategory(GenesisIntegratedSettingsCategory.YellowPages); return true; }
            if (!NetworkCategory) return false;
            if (ConnectionCategory && id == ConnectionAutoMappingId && control == _connectionAutoMapping)
            {
                if (_natSettingsPage is { } nat && nat.Refresh().CanEdit)
                    _ = nat.SetEnabledAsync(SendMessageW(_connectionAutoMapping, 0xF0, 0, 0) == 1);
            }
            else if (control == _connectionIpv6 || control == _connectionWan || control == _ypEnabled) ReadNetworkDraft();
            else if (id == GeneralCancelId && control == _generalCancel)
            { _networkInputError = null; if (ConnectionCategory) _connectionPage!.Reload(); else _ypPage!.Cancel(); }
            else if (id == GeneralSaveId && control == _generalSave)
            {
                if (!ReadNetworkDraft()) { RefreshIntegratedSettings(); return true; }
                if (ConnectionCategory)
                {
                    if (_connectionPage!.BeginSave() is { } save)
                    {
                        _connectionSave = save;
                        _ = PostNetworkSettingsCompletionAsync(save, _settingsPane);
                    }
                }
                else _ = _ypPage!.Save();
            }
            else if (YellowPagesCategory && id == YpNewId && control == _ypNew) _ypPage!.Select(null);
            else if (YellowPagesCategory && id == YpToggleId && control == _ypToggle) _ = _ypPage!.Toggle();
            else if (YellowPagesCategory && id == YpRemoveId && control == _ypRemove)
            {
                if (_ypPage!.DeletionTarget is { } target && MessageBoxW(_owner,
                    $"YP「{target.Name}」（ID: {target.Id}）を削除しますか？", "YP削除の確認", 0x24) == 6)
                    _ = _ypPage.Remove(target); // checks the same immutable identity again after the modal confirmation
            }
            else return false;
            RefreshIntegratedSettings();
            return true;
        }

        private async Task PostNetworkSettingsCompletionAsync(Task<GenesisGuiNextConnectionSaveResult> save, nint pane)
        {
            try { await save.ConfigureAwait(false); } catch { /* UI consumes safe completion below. */ }
            if (Volatile.Read(ref _detached) == 0) _ = PostMessageW(pane, NetworkSettingsSaveCompleted, _networkSettingsToken, 0);
        }
        internal void CompleteNetworkSettingsSave(nuint token)
        {
            if (token != _networkSettingsToken || Volatile.Read(ref _detached) != 0 || _connectionSave is not { } save) return;
            _connectionPage?.CompleteSave(save);
            if (save.IsCompleted) _connectionSave = null;
            if (save.IsCompletedSuccessfully && save.Result.Saved &&
                _connectionPage is { } page && _viewModel.PublicChannels is { } publicChannels)
            {
                _ = ApplyLiveConnectionSettingsAsync(publicChannels, page.Saved, _settingsPane, token);
            }
            if (ConnectionCategory) RefreshIntegratedSettings(); // never display a late result in another category
        }

        private async Task ApplyLiveConnectionSettingsAsync(GenesisGuiNextPublicChannelComposition publicChannels,
            GenesisGuiNextConnectionPreferences saved, nint pane, nuint token)
        {
            try { await publicChannels.ApplyLivePublicSettingsAsync(saved).ConfigureAwait(false); }
            catch { /* 安全にスナップショット状態へ留める */ }
            if (Volatile.Read(ref _detached) == 0 && pane != 0)
                _ = PostMessageW(pane, LivePublicSettingsApplied, token, 0);
        }

        internal void CompleteLivePublicSettingsApply(nuint token)
        {
            if (token == _networkSettingsToken && Volatile.Read(ref _detached) == 0 && ConnectionCategory)
                RefreshIntegratedSettings();
        }

        private void RefreshNetworkSettings()
        {
            void Text(nint h, string value) => _ = SetWindowTextW(h, value);
            void Input(nint h, string value) { if (GetSettingsTextLength(h) != value.Length || ReadNetworkInput(h) != value) Text(h, value); }
            _updatingNetworkSettings = true;
            try
            {
                if (ConnectionCategory)
                {
                    var page = _connectionPage!; page.Refresh();
                    Text(_generalTitle, "接続・帯域・リレー設定");
                    Text(_connectionNotice, ConnectionNotice);
                    Text(_connectionRuntime, "起動時: " + ConnectionSummary(page.Startup));
                    Text(_connectionSaved, "保存済み: " + ConnectionSummary(page.Saved));
                    Text(_connectionStatus, page.Message);
                    var v = page.Input;
                    var values = new[] { v.ListenPort, v.TotalUpstreamIpv4Kbps, v.TotalUpstreamIpv6Kbps, v.MaxRelaysTotal, v.MaxRelaysPerBroadcastChannel, v.MaxRelaysPerViewingChannel };
                    for (var i = 0; i < values.Length; i++) { Input(_connectionInputs[i], values[i]); _ = EnableWindow(_connectionInputs[i], !page.IsSaving); }
                    _ = SendMessageW(_connectionIpv6, 0xF1, v.UseIpv6 ? 1u : 0u, 0);
                    _ = EnableWindow(_connectionIpv6, !page.IsSaving);
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
                    var page = _ypPage!; page.Refresh();
                    Text(_generalTitle, "YP設定 — 操作ごとに保存"); Text(_generalStatus, page.Message);
                    var entries = page.Entries;
                    if (!_ypShown.SequenceEqual(entries))
                    {
                        _ypShown = entries;
                        _ = SendMessageW(_ypList, 0x0184, 0, 0); // LB_RESETCONTENT
                        foreach (var entry in entries) _ = SendSettingsListText(_ypList, 0x0180, 0, YpListLabel(entry));
                    }
                    var index = entries.ToList().FindIndex(e => e.Id == page.SelectedId);
                    _ = SendMessageW(_ypList, 0x0186, unchecked((nuint)index), 0);
                    var v = page.Draft; var values = new[] { v.Id, v.Name, v.IndexUrl, v.FallbackTrackerUrl, v.Namespace };
                    for (var i = 0; i < values.Length; i++) Input(_ypInputs[i], values[i] ?? "");
                    _ = EnableWindow(_ypInputs[0], page.SelectedId is null);
                    _ = SendMessageW(_ypEnabled, 0xF1, v.IsEnabled ? 1u : 0u, 0);
                    _ = EnableWindow(_generalSave, !page.Conflict);
                    _ = EnableWindow(_generalCancel, true);
                    _ = EnableWindow(_ypToggle, page.SelectedId is not null && !page.Conflict);
                    _ = EnableWindow(_ypRemove, page.SelectedId is not null && !page.Conflict);
                }
                if (_networkInputError is { } error) { Text(ConnectionCategory ? _connectionStatus : _generalStatus, error); _ = EnableWindow(_generalSave, false); }
                if (ConnectionCategory) PlaceConnectionBody();
            }
            finally { _updatingNetworkSettings = false; }
        }
        private static string ConnectionSummary(GenesisGuiNextConnectionPreferences v)
        {
            return GenesisConnectionSettingsText.Summary(v);
        }
        private static string YpListLabel(GenesisYellowPageDefinition e) => $"{(e.IsEnabled ? "有効" : "無効")} · {e.Name} ({e.Id})";

        private void PlaceNetworkSettingsCategory(GenesisGuiNextSettingsLayout layout)
        {
            void Place(nint h, GenesisGuiNextAppShellPlacement.WindowRectangle b) => _ = MoveWindow(h, Scale(b.Left), Scale(b.Top), Scale(b.Width), Scale(b.Height), true);
            Place(_connectionCategory, GenesisGuiNextSettingsLayout.ConnectionTab); Place(_yellowPagesCategory, GenesisGuiNextSettingsLayout.YellowPagesTab);
            if (!NetworkCategory) return;
            var n = new GenesisGuiNextNetworkSettingsLayout(layout.Width);
            if (ConnectionCategory)
            {
                Place(_generalTitle, n.ConnectionTitle);
                Place(_connectionIpv6, n.Ipv6);
                Place(_connectionAutoMapping, n.AutoMapping);
                Place(_connectionBody, n.Body(layout));
                PlaceConnectionBody();
            }
            else
            {
                Place(_generalStatus, n.YpStatus); Place(_ypList, GenesisGuiNextNetworkSettingsLayout.YpList); Place(_ypEnabled, GenesisGuiNextNetworkSettingsLayout.Enabled);
                Place(_ypNew, GenesisGuiNextNetworkSettingsLayout.New(layout.Height)); Place(_ypRemove, GenesisGuiNextNetworkSettingsLayout.Remove(layout.Height)); Place(_ypToggle, GenesisGuiNextNetworkSettingsLayout.Toggle(layout.Height));
                for (var i = 0; i < 5; i++) { Place(_ypInputs[i], n.YpInput(i)); Place(_ypLabels[i], GenesisGuiNextNetworkSettingsLayout.YpLabel(i)); }
                _ = SendMessageW(_ypList, 0x01A0, 0, Scale(24));
            }
        }
        private void PaintNetworkSettingsFrames(nint dc)
        {
            if (!YellowPagesCategory) return;
            foreach (var h in ConnectionCategory ? _connectionInputs : _ypInputs)
            {
                if (!GetWindowRect(h, out var b)) continue;
                var point = new Point { X = b.Left, Y = b.Top }; _ = ScreenToClient(_settingsPane, ref point);
                using var pen = new VisualPen(dc, Scale(1), GetFocus() == h ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, point.X - 1, point.Y - 1, point.X + b.Right - b.Left + 1, point.Y + b.Bottom - b.Top + 1);
            }
        }
        private bool DrawSettingsYpList(DrawItem draw)
        {
            if (!IsModern || draw.ControlWindow != _ypList) return false;
            var selected = (draw.ItemState & 1) != 0;
            FillRectangle(draw.DeviceContext, draw.ItemRectangle, selected ? GenesisGuiNextDesign.Selected : GenesisGuiNextDesign.Input);
            if (draw.ItemId < _ypShown.Count)
            {
                var b = draw.ItemRectangle; b.Left += Scale(4); b.Right -= Scale(4);
                DrawCardLabel(draw.DeviceContext, b, YpListLabel(_ypShown[(int)draw.ItemId]), GenesisGuiNextDesign.Text, _visualFont?.Handle ?? 0,
                    _ypList, DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
            }
            if ((draw.ItemState & DrawItemFocused) != 0) { var b = draw.ItemRectangle; _ = DrawFocusRect(draw.DeviceContext, ref b); }
            return true;
        }
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint SendSettingsListText(nint window, uint message, nuint value, string text);
}

using System.Runtime.InteropServices;
using PeerCast.Nova.Network.Publishing;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisPublishingTabController
{
    private static int _nextPublishingProbeToken;
    private static readonly string[] AdvancedLabels = ["PCP公開モード", "待受NIC / IPv4", "内部IPv4（設定）", "内部PCPポート", "公開IPv4（設定）", "外部ポート", "YP Announce", "YP名", "PCP Endpoint"];

    private bool _publishingAdvancedExpanded, _publishingEditAdvanced, _publishingWritingAdvanced, _publishingPlacingAdvanced;
    private nint _publishingAdvancedPane, _publishingAdvancedHint, _publishingAdvancedToggle, _publishingAdvancedEdit, _publishingAdvancedSave, _publishingAdvancedCancel,
        _publishingAdvancedFillNat, _publishingAdvancedPickYp;
    private nint[] _publishingAdvancedFields = [], _publishingAdvancedLabels = [];
    private IReadOnlyList<ObsPublishLanInterface> _publishingInterfaces = [];
    private int _publishingAdvancedScroll;
    private nuint _publishingProbeGeneration;
    private string? _pendingExternalProbeAddress, _pendingExternalProbeMessage;

    private nint[] PublishingAdvancedButtons => _host.IsModern ? [_publishingAdvancedToggle, _publishingAdvancedEdit, _publishingAdvancedSave, _publishingAdvancedCancel, _publishingAdvancedFillNat, _publishingAdvancedPickYp] : [];
    private nint[] PublishingAdvancedControls => _host.IsModern ? [_publishingAdvancedPane, _publishingAdvancedHint, .. _publishingAdvancedLabels, .. _publishingAdvancedFields] : [];

    internal bool IsAdvancedCombo(nint window) => _host.IsModern && _publishingAdvancedFields.Length == 9 &&
        (window == _publishingAdvancedFields[0] || window == _publishingAdvancedFields[1] || window == _publishingAdvancedFields[6]);

    internal bool IsAdvancedPane(nint pane) => _host.IsModern && pane == _publishingAdvancedPane;

    private void InitializeBasicInputs()
    {
        if (_publishingEditor?.Input is not { } input) return;
        _publishingWritingInputs = true;
        try
        {
            var values = new[] { input.ChannelName, input.Genre, input.Description, input.Comment, input.ContactUrl, input.BitrateKbps };
            for (var i = 0; i < 6; i++) _ = SetWindowTextW(_publishingInputs[i], values[i]);
        }
        finally { _publishingWritingInputs = false; }
    }

    private static int AdvancedErrorIndex(string? field) => field switch
    {
        "LanAddress" or "WanAddress" or "LanInterfaceId" or "WanInterfaceId" => 1,
        "LanPort" or "WanPort" => 3,
        "AdvertisedAddress" => 4,
        "AdvertisedPort" => 5,
        "AnnounceEnabled" => 6,
        "AnnounceName" => 7,
        "AnnounceEndpoint" or "value" => 8,
        _ => 7,
    };

    private string InlineErrorLabel(string? field)
    {
        var basic = Array.IndexOf(BasicFieldNames, field);
        if (basic >= 0) return "（" + GenesisGuiNextPublishingSettingsWindow.FieldLabels[basic] + "）";
        if (field is not null) return "（" + AdvancedLabels[AdvancedErrorIndex(field)] + "）";
        return _publishingEditAdvanced && _publishingEditor?.Editing == true ? "（詳細設定：公開モード・YP名・PCP Endpointを確認）" : "";
    }

    private void CreateAdvanced(nint instance)
    {
        _publishingAdvancedPane = CreateWindowExW(0x10000, "STATIC", "", WindowStyleChild | 0x02000000 | 0x00200000,
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0);
        _ = SetWindowSubclass(_publishingAdvancedPane, PublishingEditorProcedure, PublishingEditorSubclassId, (nuint)_host.Owner);
        _publishingAdvancedFields = new nint[9]; _publishingAdvancedLabels = new nint[9];
        for (var i = 0; i < 9; i++)
        {
            _publishingAdvancedLabels[i] = CreateWindowExW(0, "STATIC", AdvancedLabels[i], WindowStyleChild,
                0, 0, 1, 1, _publishingAdvancedPane, 0, instance, 0);
            var combo = i is 0 or 1 or 6;
            _publishingAdvancedFields[i] = CreateWindowExW(0, combo ? "COMBOBOX" : "EDIT", "", WindowStyleChild |
                (i == 2 ? 0x0800u : WindowStyleTabStop) | (combo ? 0x200003u : 0x80u),
                0, 0, 1, 1, _publishingAdvancedPane, (nint)(AdvancedInputId + i), instance, 0);
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(_publishingAdvancedFields[i], AdvancedLabels[i]);
            if (!combo) _ = SendMessageW(_publishingAdvancedFields[i], 0x00C5, 8192, 0);
        }
        _publishingAdvancedHint = CreateWindowExW(0, "STATIC",
            "変更は次回の配信開始から適用します。保存だけでは待受・ポート開放・外部通信を開始しません。\r\nWANは外部接続の対象です。到達性・YP掲載は未確認です。\r\nPCP EndpointはYP IndexのHTTP URLとは別の設定です。", WindowStyleChild,
            0, 0, 1, 1, _publishingAdvancedPane, 0, instance, 0);
        _publishingAdvancedToggle = _host.CreateOwnerDrawButton("展開", _host.DetailsPane, AdvancedToggleId, 0, instance);
        _publishingAdvancedEdit = _host.CreateOwnerDrawButton("詳細設定を編集", _host.DetailsPane, AdvancedEditId, 0, instance);
        _publishingAdvancedSave = _host.CreateOwnerDrawButton("保存", _host.DetailsPane, AdvancedSaveId, 0, instance);
        _publishingAdvancedCancel = _host.CreateOwnerDrawButton("全体をキャンセル", _host.DetailsPane, AdvancedCancelId, 0, instance);
        _publishingAdvancedFillNat = _host.CreateOwnerDrawButton("NATから自動入力", _publishingAdvancedPane, AdvancedFillNatId, 0, instance);
        _publishingAdvancedPickYp = _host.CreateOwnerDrawButton("登録YPから選択…", _publishingAdvancedPane, AdvancedPickYpId, 0, instance);
    }

    private void InitializeAdvanced(int changedIndex = -1)
    {
        if (_publishingEditor?.Input is not { } input) return;
        _publishingWritingAdvanced = true;
        try
        {
            void Choices(int index, IEnumerable<string> values, int selection)
            {
                if (index == changedIndex) return; // Preserve the native open dropdown while its selection changes.
                var h = _publishingAdvancedFields[index];
                _ = SendMessageW(h, 0x014B, 0, 0);
                foreach (var value in values) _ = SendMessageTextW(h, 0x0143, 0, value);
                _ = SendMessageW(h, 0x014E, unchecked((nuint)selection), 0);
            }
            Choices(0, ["このPCのみ（Loopback）", "LAN（同一Subnet）", "WAN（明示公開）"], input.WanEnabled ? 2 : input.LanEnabled ? 1 : 0);
            _publishingInterfaces = _publishingEditor.InterfaceChoices();
            var id = input.WanEnabled ? input.WanInterfaceId : input.LanInterfaceId;
            var address = input.WanEnabled ? input.WanAddress : input.LanAddress;
            Choices(1, _publishingInterfaces.Select(n => $"{n.Name} | {n.Address}"),
                _publishingInterfaces.ToList().FindIndex(n => n.Id == id && n.Address == address)); // never choose a replacement automatically
            Choices(6, ["OFF", "ON（次回開始時）"], input.AnnounceEnabled ? 1 : 0);
            _ = SetWindowTextW(_publishingAdvancedFields[2], address);
            _ = SetWindowTextW(_publishingAdvancedFields[3], input.WanEnabled ? input.WanPort : input.LanPort);
            _ = SetWindowTextW(_publishingAdvancedFields[4], input.AdvertisedAddress);
            _ = SetWindowTextW(_publishingAdvancedFields[5], input.AdvertisedPort);
            _ = SetWindowTextW(_publishingAdvancedFields[7], input.AnnounceName);
            _ = SetWindowTextW(_publishingAdvancedFields[8], input.AnnounceEndpoint);
        }
        finally { _publishingWritingAdvanced = false; }
        PlaceAdvancedInputs();
    }

    private bool ProcessAdvancedInput(int id, int notification, nint control)
    {
        var index = id - AdvancedInputId;
        if (!_host.IsModern || index is < 0 or > 8 || _publishingAdvancedFields[index] != control) return false;
        if (_publishingWritingAdvanced || _publishingEditor is not { Editing: true, Saving: false } editor || !_publishingEditAdvanced || editor.Input is not { } input) return true;
        if (notification == 1 && index is 0 or 1 or 6)
        {
            var selection = (int)SendMessageW(control, 0x0147, 0, 0);
            if (index == 0 && selection is >= 0 and <= 2)
                _ = editor.EditAdvanced(input with { LanEnabled = selection == 1, WanEnabled = selection == 2 });
            else if (index == 1 && selection >= 0 && selection < _publishingInterfaces.Count && _publishingInterfaces[selection] is { IsUp: true } nic)
                _ = editor.EditAdvanced(input.WanEnabled ? input with { WanInterfaceId = nic.Id, WanAddress = nic.Address } : input with { LanInterfaceId = nic.Id, LanAddress = nic.Address });
            else if (index == 6 && selection is 0 or 1)
                _ = editor.EditAdvanced(input with { AnnounceEnabled = selection == 1 });
            InitializeAdvanced(index);
            _host.SynchronizeNativeSelection();
        }
        else if (notification == 0x0300 && index is 3 or 4 or 5 or 7 or 8)
        {
            string Read(int i)
            {
                var length = GetWindowTextLengthW(_publishingAdvancedFields[i]);
                if (length is < 0 or > 8192) throw new InvalidOperationException("Draft is too long.");
                var buffer = new char[length + 1];
                if (GetWindowTextW(_publishingAdvancedFields[i], buffer, buffer.Length) != length) throw new InvalidOperationException("Cannot read draft.");
                return new(buffer, 0, length);
            }
            try
            {
                _ = editor.EditAdvanced(input with
                {
                    LanPort = input.LanEnabled ? Read(3) : input.LanPort,
                    WanPort = input.WanEnabled ? Read(3) : input.WanPort,
                    AdvertisedAddress = Read(4),
                    AdvertisedPort = Read(5),
                    AnnounceName = Read(7),
                    AnnounceEndpoint = Read(8),
                });
            }
            catch (InvalidOperationException) { /* Keep the last complete draft; native edits are bounded. */ }
        }
        return true;
    }

    private bool ProcessAdvancedCommand(int id, nint control)
    {
        if (!_host.IsModern || _publishingEditor is not { Saving: false } editor) return false;
        if (id == AdvancedToggleId && control == _publishingAdvancedToggle)
        {
            _publishingAdvancedExpanded = !_publishingAdvancedExpanded;
            if (!_publishingAdvancedExpanded && _publishingEditAdvanced) _publishingEditAdvanced = false;
        }
        else if (id == AdvancedEditId && control == _publishingAdvancedEdit)
        {
            if (editor.Editing || editor.Begin())
            {
                _publishingAdvancedExpanded = _publishingEditAdvanced = true;
                _publishingAdvancedScroll = 0;
                InitializeAdvanced();
            }
        }
        else if (id == AdvancedFillNatId && control == _publishingAdvancedFillNat)
        {
            if (editor.Editing || editor.Begin())
            {
                _publishingAdvancedExpanded = _publishingEditAdvanced = true;
                var interfaces = _publishingInterfaces.Count > 0 ? _publishingInterfaces : editor.InterfaceChoices();
                var fallbackListenPort = _host.ViewModel.ConnectionSettings?.Active.ListenPort ?? 7144;
                var suggestion = PublishingAdvancedAssist.TryApplyNatSuggestion(
                    editor.Input!,
                    _host.ViewModel.NetworkSettingsAdapter?.ReadSnapshot(),
                    interfaces,
                    fallbackListenPort);
                if (suggestion.Applied && editor.EditAdvanced(suggestion.UpdatedInput))
                {
                    InitializeAdvanced();
                    editor.SetStatus(suggestion.Message);
                }
                else
                {
                    var presets = PublishingAdvancedAssist.TryBuildAllAnnouncePresets(_host.ViewModel.YellowPageSettings?.Entries ?? []);
                    var endpoint = !string.IsNullOrWhiteSpace(editor.Input!.AnnounceEndpoint)
                        ? editor.Input.AnnounceEndpoint.Trim()
                        : presets.Count > 0 ? presets[0].Endpoint : null;
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        StartExternalIpProbe(editor, endpoint, _host.DetailsPane);
                    }
                    else
                    {
                        editor.SetStatus(suggestion.Message);
                    }
                }
            }
        }
        else if (id == AdvancedPickYpId && control == _publishingAdvancedPickYp)
        {
            if (editor.Editing || editor.Begin())
            {
                _publishingAdvancedExpanded = _publishingEditAdvanced = true;
                ShowYellowPagePresetMenu(editor);
            }
        }
        else if (id == AdvancedSaveId && control == _publishingAdvancedSave) _ = SaveInlineAsync();
        else if (id == AdvancedCancelId && control == _publishingAdvancedCancel) _ = editor.Cancel();
        else return false;
        if (editor.Editing && !_publishingEditAdvanced) InitializeBasicInputs();
        _host.SynchronizeNativeSelection();
        _ = SetFocus(_publishingEditAdvanced && editor.Editing ? _publishingAdvancedFields[0] : _publishingAdvancedExpanded ? _publishingAdvancedEdit : editor.Editing ? _publishingInputs[0] : _publishingAdvancedToggle);
        return true;
    }

    private void SynchronizeAdvanced(bool visible)
    {
        var active = _publishingAdvancedExpanded && _publishingEditAdvanced && _publishingEditor?.Editing == true;
        _ = _host.PresentVisible(_publishingOptions, visible && !active ? ShowWindowShowNoActivate : ShowWindowHide);
        _ = _host.PresentVisible(_publishingAdvancedToggle, visible ? ShowWindowShowNoActivate : ShowWindowHide);
        _ = _host.PresentText(_publishingAdvancedToggle, _publishingAdvancedExpanded ? "折りたたむ" : "展開");
        _ = _host.PresentText(_publishingOptionsTitle, active ? "詳細設定（編集中）" : "詳細設定（保存値）");
        _ = _host.PresentVisible(_publishingAdvancedEdit, visible && _publishingAdvancedExpanded && !active ? ShowWindowShowNoActivate : ShowWindowHide);
        foreach (var h in new[] { _publishingAdvancedSave, _publishingAdvancedCancel, _publishingAdvancedPane, _publishingAdvancedHint, _publishingAdvancedFillNat, _publishingAdvancedPickYp })
            _ = _host.PresentVisible(h, visible && active ? ShowWindowShowNoActivate : ShowWindowHide);
        foreach (var h in PublishingAdvancedButtons) _ = _host.PresentEnabled(h, _publishingEditor is { Saving: false });
        var input = _publishingEditor?.Input;
        var rows = input is null ? [] : GenesisPublishingAdvancedLayout.Rows(input);
        for (var i = 0; i < 9; i++)
        {
            var show = visible && active && rows.Contains(i);
            _ = _host.PresentVisible(_publishingAdvancedLabels[i], show ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = _host.PresentVisible(_publishingAdvancedFields[i], show ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = _host.PresentEnabled(_publishingAdvancedFields[i], _publishingEditor is { Saving: false } && input is not null &&
                (i != 6 || input.WanEnabled || input.AnnounceEnabled));
        }
    }

    private void PlaceAdvanced(GenesisGuiNextPublishingModernLayout layout)
    {
        void Place(nint h, GenesisGuiNextPublishingGuideRectangle r) => _ = _host.PresentBounds(h, r.Left, r.Top, r.Width, r.Height, true);
        Place(_publishingAdvancedToggle, layout.AdvancedToggle);
        Place(_publishingOptions, layout.AdvancedViewport); Place(_publishingAdvancedPane, layout.AdvancedViewport);
        Place(_publishingAdvancedEdit, layout.AdvancedAction);
        Place(_publishingAdvancedSave, layout.AdvancedAction with { Width = 88 });
        Place(_publishingAdvancedCancel, layout.AdvancedAction with { Left = layout.AdvancedAction.Left + 100, Width = 156 });
        PlaceAdvancedInputs();
    }

    internal void PlaceAdvancedInputs()
    {
        if (_publishingPlacingAdvanced || _publishingEditor?.Input is not { } input || !GetClientRect(_publishingAdvancedPane, out var area)) return;
        _publishingPlacingAdvanced = true;
        try
        {
            var rows = GenesisPublishingAdvancedLayout.Rows(input); var page = _host.LogicalWidth(area.Bottom);
            _publishingAdvancedScroll = GenesisPublishingAdvancedLayout.Clamp(_publishingAdvancedScroll, page, rows.Length);
            var scroll = new Win32ScrollInfo
            {
                Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(),
                Mask = 7,
                Maximum = GenesisPublishingAdvancedLayout.ContentHeight(rows.Length) - 1,
                Page = (uint)Math.Max(1, page),
                Position = _publishingAdvancedScroll
            };
            _ = SetScrollInfo(_publishingAdvancedPane, 1, ref scroll, true);
            _ = GetClientRect(_publishingAdvancedPane, out area);
            var width = _host.LogicalWidth(area.Right);
            for (var row = 0; row < rows.Length; row++)
            {
                var index = rows[row]; var y = 4 + row * GenesisPublishingAdvancedLayout.RowHeight - _publishingAdvancedScroll;
                _ = MoveWindow(_publishingAdvancedLabels[index], _host.Scale(2), _host.Scale(y), _host.Scale(116), _host.Scale(30), true);
                var combo = index is 0 or 1 or 6;
                if (combo && SendMessageW(_publishingAdvancedFields[index], 0x0157, 0, 0) != 0) continue; // Moving an open ComboBox closes its native dropdown.
                _ = MoveWindow(_publishingAdvancedFields[index], _host.Scale(120), _host.Scale(y), _host.Scale(Math.Max(32, width - 124)), _host.Scale(combo ? 200 : 30), true);
                if (combo) _ = SendMessageW(_publishingAdvancedFields[index], 0x0153, nuint.MaxValue, _host.Scale(24));
            }
            var assistY = 4 + rows.Length * GenesisPublishingAdvancedLayout.RowHeight - _publishingAdvancedScroll;
            _ = MoveWindow(_publishingAdvancedFillNat, _host.Scale(2), _host.Scale(assistY), _host.Scale(148), _host.Scale(28), true);
            _ = MoveWindow(_publishingAdvancedPickYp, _host.Scale(158), _host.Scale(assistY), _host.Scale(148), _host.Scale(28), true);
            _ = MoveWindow(_publishingAdvancedHint, _host.Scale(2), _host.Scale(assistY + 36), _host.Scale(Math.Max(32, width - 6)), _host.Scale(100), true);
            _ = RedrawWindow(_publishingAdvancedPane, 0, 0, 0x0001 | 0x0004 | 0x0080);
        }
        finally { _publishingPlacingAdvanced = false; }
    }

    internal void ScrollAdvanced(uint message, nuint value)
    {
        if (_publishingEditor?.Input is not { } input || !GetClientRect(_publishingAdvancedPane, out var area)) return;
        var page = _host.LogicalWidth(area.Bottom);
        if (message == 0x020A) _publishingAdvancedScroll -= (short)(value >> 16) * 48 / 120;
        else
        {
            var info = new Win32ScrollInfo { Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(), Mask = 0x10 };
            _ = GetScrollInfo(_publishingAdvancedPane, 1, ref info);
            _publishingAdvancedScroll = (int)(value & 0xffff) switch
            {
                0 => _publishingAdvancedScroll - GenesisPublishingAdvancedLayout.RowHeight,
                1 => _publishingAdvancedScroll + GenesisPublishingAdvancedLayout.RowHeight,
                2 => _publishingEditScrollSafe(page, -1),
                3 => _publishingEditScrollSafe(page, 1),
                4 or 5 => info.TrackPosition,
                6 => 0,
                7 => GenesisPublishingAdvancedLayout.ContentHeight(GenesisPublishingAdvancedLayout.Rows(input).Length),
                _ => _publishingAdvancedScroll
            };
        }
        PlaceAdvancedInputs();
    }

    private int _publishingEditScrollSafe(int page, int direction) => _publishingAdvancedScroll + (page * direction);

    private void RevealAdvanced(nint window)
    {
        if (_publishingEditor?.Input is not { } input || !GetClientRect(_publishingAdvancedPane, out var area)) return;
        var rows = GenesisPublishingAdvancedLayout.Rows(input);
        var row = Array.IndexOf(rows, Array.IndexOf(_publishingAdvancedFields, window));
        if (row < 0) return;
        _publishingAdvancedScroll = GenesisPublishingAdvancedLayout.Reveal(_publishingAdvancedScroll, _host.LogicalWidth(area.Bottom), row, rows.Length);
        PlaceAdvancedInputs();
    }

    private nint[] PublishingEditTabOrder => _publishingEditAdvanced && _publishingEditor?.Input is { } input
        ? [.. GenesisPublishingAdvancedLayout.Rows(input).Where(i => i != 2 && (i != 6 || input.WanEnabled || input.AnnounceEnabled)).Select(i => _publishingAdvancedFields[i]), _publishingAdvancedFillNat, _publishingAdvancedPickYp, _publishingAdvancedSave, _publishingAdvancedCancel, _publishingEdit]
        : [.. _publishingInputs, _publishingSave, _publishingCancel, _publishingAdvancedToggle];

    private void StartExternalIpProbe(GenesisPublishingBasicEditor editor, string trackerEndpoint, nint targetPane)
    {
        var token = unchecked((nuint)Interlocked.Increment(ref _nextPublishingProbeToken));
        _publishingProbeGeneration = token;
        Volatile.Write(ref _pendingExternalProbeAddress, null);
        Volatile.Write(ref _pendingExternalProbeMessage, null);
        editor.SetStatus("NATから公開IPv4が未取得のため、登録YPへ問い合わせています…");
        _ = Task.Run(async () =>
        {
            var probe = await PcpExternalReachabilityProbe.ProbeAsync(trackerEndpoint, listenPort: 0).ConfigureAwait(false);
            Volatile.Write(ref _pendingExternalProbeAddress, probe.Succeeded ? probe.ExternalAddress : null);
            Volatile.Write(ref _pendingExternalProbeMessage, probe.FailureReason);
            if (!_host.IsDetached && targetPane != 0)
            {
                _ = PostMessageW(targetPane, PublishingExternalProbeCompleted, token, 0);
            }
        });
    }

    internal void CompleteExternalProbe(nuint token)
    {
        if (token != _publishingProbeGeneration || _host.IsDetached ||
            _publishingEditor is not { Editing: true, Saving: false } editor || editor.Input is not { } input)
        {
            return;
        }

        var externalAddress = Volatile.Read(ref _pendingExternalProbeAddress);
        var failureMessage = Volatile.Read(ref _pendingExternalProbeMessage);
        var interfaces = _publishingInterfaces.Count > 0 ? _publishingInterfaces : editor.InterfaceChoices();
        var suggestion = PublishingAdvancedAssist.TryApplyExternalAddressSuggestion(
            input,
            externalAddress,
            _host.ViewModel.NetworkSettingsAdapter?.ReadSnapshot(),
            interfaces,
            _host.ViewModel.ConnectionSettings?.Active.ListenPort ?? 7144,
            sourceLabel: "YP Tracker",
            failureMessage: string.IsNullOrWhiteSpace(failureMessage)
                ? "NATおよびYP Trackerから公開IPv4を取得できませんでした。手動で入力してください。"
                : $"公開IPv4を自動取得できませんでした（{failureMessage}）。");
        if (suggestion.Applied && editor.EditAdvanced(suggestion.UpdatedInput))
        {
            InitializeAdvanced();
        }
        editor.SetStatus(suggestion.Message);
    }

    private void ShowYellowPagePresetMenu(GenesisPublishingBasicEditor editor)
    {
        var entries = _host.ViewModel.YellowPageSettings?.Entries ?? [];
        var presets = PublishingAdvancedAssist.TryBuildAllAnnouncePresets(entries);

        if (presets.Count == 0)
        {
            editor.SetStatus("選択可能な登録YPがありません。設定タブの「YP」からYPを登録してください。");
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            var hasBulkOption = presets.Count >= 2;
            if (hasBulkOption && !AppendMenuW(menu, 0, 1, $"有効な登録YPをすべて設定（{presets.Count}件・複数YP同時掲載）"))
            {
                return;
            }

            for (var i = 0; i < presets.Count; i++)
            {
                var item = presets[i];
                var label = $"{item.Name} ({item.Endpoint})";
                var menuId = (nuint)(i + (hasBulkOption ? 2 : 1));
                if (!AppendMenuW(menu, 0, menuId, label))
                {
                    return;
                }
            }

            if (!GetWindowRect(_publishingAdvancedPickYp, out var bounds))
            {
                return;
            }

            var selected = TrackPopupMenuEx(menu, 0x0100u, bounds.Left, bounds.Bottom, _host.Owner, 0);
            if (selected == 0 || editor.Input is not { } current)
            {
                return;
            }

            if (hasBulkOption && selected == 1)
            {
                var primary = presets[0];
                var additional = presets.Skip(1)
                    .Select(static preset => new PublishingAnnounceTarget(preset.Name, preset.Endpoint))
                    .ToArray();
                var bulkUpdated = current with
                {
                    AnnounceEnabled = current.WanEnabled || current.AnnounceEnabled,
                    AnnounceName = primary.Name,
                    AnnounceEndpoint = primary.Endpoint,
                    AdditionalAnnounceTargets = additional,
                };
                if (!editor.EditAdvanced(bulkUpdated))
                {
                    return;
                }

                InitializeAdvanced();
                var names = string.Join(" / ", presets.Select(static p => p.Name));
                editor.SetStatus(current.WanEnabled
                    ? $"{presets.Count}件のYP（{names}）を同時掲載対象に設定しました。保存して反映してください。"
                    : $"{presets.Count}件のYPを設定しました。YP Announce を ON にするには公開モードを「WAN（明示公開）」にしてください。");
                return;
            }

            var itemIndex = (int)selected - (hasBulkOption ? 2 : 1);
            if (itemIndex < 0 || itemIndex >= presets.Count)
            {
                return;
            }

            var picked = presets[itemIndex];
            var updated = current with
            {
                AnnounceEnabled = current.WanEnabled || current.AnnounceEnabled,
                AnnounceName = picked.Name,
                AnnounceEndpoint = picked.Endpoint,
                AdditionalAnnounceTargets = [],
            };
            if (!editor.EditAdvanced(updated))
            {
                return;
            }

            InitializeAdvanced();
            if (!current.WanEnabled)
            {
                editor.SetStatus($"YP「{picked.Name}」を反映しました。YP Announce を ON にするには公開モードを「WAN（明示公開）」にしてください。");
            }
            else if (picked.UsedFallbackPort)
            {
                editor.SetStatus($"YP「{picked.Name}」({picked.Endpoint}) を反映しました。ポート番号はYPの案内値に合わせて確認してください。");
            }
            else
            {
                editor.SetStatus($"YP「{picked.Name}」({picked.Endpoint}) を反映しました。保存して次回配信から適用してください。");
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }
}

internal static class GenesisPublishingAdvancedLayout
{
    internal static int[] Rows(PublishingSettingsInput input) => input.WanEnabled ? (input.AnnounceEnabled ? [0, 1, 2, 3, 4, 5, 6, 7, 8] : [0, 1, 2, 3, 4, 5, 6]) :
        input.LanEnabled ? (input.AnnounceEnabled ? [0, 1, 2, 3, 6, 7, 8] : [0, 1, 2, 3, 6]) : (input.AnnounceEnabled ? [0, 6, 7, 8] : [0, 6]);
    internal const int RowHeight = 38;
    internal static int ContentHeight(int rows) => rows * RowHeight + 148;
    internal static int Clamp(int scroll, int height, int rows) => Math.Clamp(scroll, 0, Math.Max(0, ContentHeight(rows) - height));
    internal static int Reveal(int scroll, int height, int row, int rows) => Clamp(row * RowHeight + 4 < scroll ? row * RowHeight + 4 : Math.Max(scroll, row * RowHeight + 34 - height), height, rows);
}

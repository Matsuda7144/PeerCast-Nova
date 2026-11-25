using System.Runtime.InteropServices;
using PeerCast.Nova.Network.Publishing;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const int AdvancedToggleId = 1250, AdvancedEditId = 1251, AdvancedSaveId = 1252, AdvancedCancelId = 1253, AdvancedInputId = 1254;
    private static readonly string[] AdvancedLabels = ["PCP公開モード", "待受NIC / IPv4", "内部IPv4（設定）", "内部PCPポート", "公開IPv4（設定）", "外部ポート", "YP Announce", "YP名", "PCP Endpoint"];

    private sealed partial class AppShellState
    {
        private bool _publishingAdvancedExpanded, _publishingEditAdvanced, _publishingWritingAdvanced, _publishingPlacingAdvanced;
        private nint _publishingAdvancedPane, _publishingAdvancedHint, _publishingAdvancedToggle, _publishingAdvancedEdit, _publishingAdvancedSave, _publishingAdvancedCancel;
        private nint[] _publishingAdvancedFields = [], _publishingAdvancedLabels = [];
        private IReadOnlyList<ObsPublishLanInterface> _publishingInterfaces = [];
        private int _publishingAdvancedScroll;
        private nint[] PublishingAdvancedButtons => IsModern ? [_publishingAdvancedToggle, _publishingAdvancedEdit, _publishingAdvancedSave, _publishingAdvancedCancel] : [];
        private nint[] PublishingAdvancedControls => IsModern ? [_publishingAdvancedPane, _publishingAdvancedHint, .. _publishingAdvancedLabels, .. _publishingAdvancedFields] : [];
        internal bool IsPublishingAdvancedCombo(nint window) => IsModern && _publishingAdvancedFields.Length == 9 &&
            (window == _publishingAdvancedFields[0] || window == _publishingAdvancedFields[1] || window == _publishingAdvancedFields[6]);

        internal bool IsPublishingAdvancedPane(nint pane) => IsModern && pane == _publishingAdvancedPane;

        private void InitializePublishingBasicInputs()
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

        private string PublishingInlineErrorLabel(string? field)
        {
            var basic = Array.IndexOf(BasicFieldNames, field);
            if (basic >= 0) return "（" + GenesisGuiNextPublishingSettingsWindow.FieldLabels[basic] + "）";
            if (field is not null) return "（" + AdvancedLabels[AdvancedErrorIndex(field)] + "）";
            return _publishingEditAdvanced && _publishingEditor?.Editing == true ? "（詳細設定：公開モード・YP名・PCP Endpointを確認）" : "";
        }

        private void CreatePublishingAdvanced(nint instance)
        {
            _publishingAdvancedPane = CreateWindowExW(0x10000, "STATIC", "", WindowStyleChild | 0x02000000 | 0x00200000,
                0, 0, 1, 1, _detailsPane, 0, instance, 0);
            _ = SetWindowSubclass(_publishingAdvancedPane, PublishingEditorProcedure, PublishingEditorSubclassId, (nuint)_owner);
            _publishingAdvancedFields = new nint[9]; _publishingAdvancedLabels = new nint[9];
            for (var i = 0; i < 9; i++)
            {
                _publishingAdvancedLabels[i] = CreateWindowExW(0, "STATIC", AdvancedLabels[i], WindowStyleChild,
                    0, 0, 1, 1, _publishingAdvancedPane, 0, instance, 0);
                var combo = i is 0 or 1 or 6;
                _publishingAdvancedFields[i] = CreateWindowExW(0, combo ? "COMBOBOX" : "EDIT", "", WindowStyleChild |
                    (i == 2 ? 0x0800u : WindowStyleTabStop) | (combo ? 0x200003u : 0x80u),
                    0, 0, 1, 1, _publishingAdvancedPane, (nint)(AdvancedInputId + i), instance, 0);
                if (!combo) _ = SendMessageW(_publishingAdvancedFields[i], 0x00C5, 8192, 0);
            }
            _publishingAdvancedHint = CreateWindowExW(0, "STATIC",
                "変更は次回の配信開始から適用します。保存だけでは待受・ポート開放・外部通信を開始しません。\r\nWANは外部接続の対象です。到達性・YP掲載は未確認です。\r\nPCP EndpointはYP IndexのHTTP URLとは別の設定です。", WindowStyleChild,
                0, 0, 1, 1, _publishingAdvancedPane, 0, instance, 0);
            _publishingAdvancedToggle = CreateRecordingModeButton("展開", _detailsPane, AdvancedToggleId, 0, instance);
            _publishingAdvancedEdit = CreateRecordingModeButton("詳細設定を編集", _detailsPane, AdvancedEditId, 0, instance);
            _publishingAdvancedSave = CreateRecordingModeButton("保存", _detailsPane, AdvancedSaveId, 0, instance);
            _publishingAdvancedCancel = CreateRecordingModeButton("全体をキャンセル", _detailsPane, AdvancedCancelId, 0, instance);
        }

        private void InitializePublishingAdvanced(int changedIndex = -1)
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
                    foreach (var value in values) _ = SendPublishingComboText(h, 0x0143, 0, value);
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
            PlacePublishingAdvancedInputs();
        }

        private bool ProcessPublishingAdvancedInput(int id, int notification, nint control)
        {
            var index = id - AdvancedInputId;
            if (!IsModern || index is < 0 or > 8 || _publishingAdvancedFields[index] != control) return false;
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
                InitializePublishingAdvanced(index);
                SynchronizeNativeSelection();
            }
            else if (notification == 0x0300 && index is 3 or 4 or 5 or 7 or 8)
            {
                string Read(int i)
                {
                    var length = GetSettingsTextLength(_publishingAdvancedFields[i]);
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

        private bool ProcessPublishingAdvancedCommand(int id, nint control)
        {
            if (!IsModern || _publishingEditor is not { Saving: false } editor) return false;
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
                    InitializePublishingAdvanced();
                }
            }
            else if (id == AdvancedSaveId && control == _publishingAdvancedSave) _ = SavePublishingInlineAsync();
            else if (id == AdvancedCancelId && control == _publishingAdvancedCancel) _ = editor.Cancel();
            else return false;
            if (editor.Editing && !_publishingEditAdvanced) InitializePublishingBasicInputs();
            SynchronizeNativeSelection();
            _ = SetFocus(_publishingEditAdvanced && editor.Editing ? _publishingAdvancedFields[0] : _publishingAdvancedExpanded ? _publishingAdvancedEdit : editor.Editing ? _publishingInputs[0] : _publishingAdvancedToggle);
            return true;
        }

        private void SynchronizePublishingAdvanced(bool visible)
        {
            var active = _publishingAdvancedExpanded && _publishingEditAdvanced && _publishingEditor?.Editing == true;
            _ = PresentVisible(_publishingOptions, visible && !active ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_publishingAdvancedToggle, visible ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentText(_publishingAdvancedToggle, _publishingAdvancedExpanded ? "折りたたむ" : "展開");
            _ = PresentText(_publishingOptionsTitle, active ? "詳細設定（編集中）" : "詳細設定（保存値）");
            _ = PresentVisible(_publishingAdvancedEdit, visible && _publishingAdvancedExpanded && !active ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var h in new[] { _publishingAdvancedSave, _publishingAdvancedCancel, _publishingAdvancedPane, _publishingAdvancedHint })
                _ = PresentVisible(h, visible && active ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var h in PublishingAdvancedButtons) _ = PresentEnabled(h, _publishingEditor is { Saving: false });
            var input = _publishingEditor?.Input;
            var rows = input is null ? [] : GenesisPublishingAdvancedLayout.Rows(input);
            for (var i = 0; i < 9; i++)
            {
                var show = visible && active && rows.Contains(i);
                _ = PresentVisible(_publishingAdvancedLabels[i], show ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = PresentVisible(_publishingAdvancedFields[i], show ? ShowWindowShowNoActivate : ShowWindowHide);
                _ = PresentEnabled(_publishingAdvancedFields[i], _publishingEditor is { Saving: false } && input is not null &&
                    (i != 6 || input.WanEnabled || input.AnnounceEnabled));
            }
        }

        private void PlacePublishingAdvanced(GenesisGuiNextPublishingModernLayout layout)
        {
            void Place(nint h, GenesisGuiNextPublishingGuideRectangle r) => _ = PresentBounds(h, r.Left, r.Top, r.Width, r.Height, true);
            Place(_publishingAdvancedToggle, layout.AdvancedToggle);
            Place(_publishingOptions, layout.AdvancedViewport); Place(_publishingAdvancedPane, layout.AdvancedViewport);
            Place(_publishingAdvancedEdit, layout.AdvancedAction);
            Place(_publishingAdvancedSave, layout.AdvancedAction with { Width = 88 });
            Place(_publishingAdvancedCancel, layout.AdvancedAction with { Left = layout.AdvancedAction.Left + 100, Width = 156 });
            PlacePublishingAdvancedInputs();
        }

        internal void PlacePublishingAdvancedInputs()
        {
            if (_publishingPlacingAdvanced || _publishingEditor?.Input is not { } input || !GetClientRect(_publishingAdvancedPane, out var area)) return;
            _publishingPlacingAdvanced = true;
            try
            {
                var rows = GenesisPublishingAdvancedLayout.Rows(input); var page = LogicalWidth(area.Bottom);
                _publishingAdvancedScroll = GenesisPublishingAdvancedLayout.Clamp(_publishingAdvancedScroll, page, rows.Length);
                var scroll = new PublishingEditScrollInfo
                {
                    Size = (uint)Marshal.SizeOf<PublishingEditScrollInfo>(),
                    Mask = 7,
                    Maximum = GenesisPublishingAdvancedLayout.ContentHeight(rows.Length) - 1,
                    Page = (uint)Math.Max(1, page),
                    Position = _publishingAdvancedScroll
                };
                _ = SetPublishingEditScroll(_publishingAdvancedPane, 1, ref scroll, true);
                _ = GetClientRect(_publishingAdvancedPane, out area);
                var width = LogicalWidth(area.Right);
                for (var row = 0; row < rows.Length; row++)
                {
                    var index = rows[row]; var y = 4 + row * GenesisPublishingAdvancedLayout.RowHeight - _publishingAdvancedScroll;
                    _ = MoveWindow(_publishingAdvancedLabels[index], Scale(2), Scale(y), Scale(116), Scale(30), true);
                    var combo = index is 0 or 1 or 6;
                    if (combo && SendMessageW(_publishingAdvancedFields[index], 0x0157, 0, 0) != 0) continue; // Moving an open ComboBox closes its native dropdown.
                    _ = MoveWindow(_publishingAdvancedFields[index], Scale(120), Scale(y), Scale(Math.Max(32, width - 124)), Scale(combo ? 200 : 30), true);
                    if (combo) _ = SendMessageW(_publishingAdvancedFields[index], 0x0153, nuint.MaxValue, Scale(24));
                }
                _ = MoveWindow(_publishingAdvancedHint, Scale(2), Scale(rows.Length * GenesisPublishingAdvancedLayout.RowHeight + 8 - _publishingAdvancedScroll), Scale(Math.Max(32, width - 6)), Scale(100), true);
                _ = RedrawWindow(_publishingAdvancedPane, 0, 0, 0x0001 | 0x0004 | 0x0080);
            }
            finally { _publishingPlacingAdvanced = false; }
        }

        internal void ScrollPublishingAdvanced(uint message, nuint value)
        {
            if (_publishingEditor?.Input is not { } input || !GetClientRect(_publishingAdvancedPane, out var area)) return;
            var page = LogicalWidth(area.Bottom);
            if (message == 0x020A) _publishingAdvancedScroll -= (short)(value >> 16) * 48 / 120;
            else
            {
                var info = new PublishingEditScrollInfo { Size = (uint)Marshal.SizeOf<PublishingEditScrollInfo>(), Mask = 0x10 };
                _ = GetPublishingEditScroll(_publishingAdvancedPane, 1, ref info);
                _publishingAdvancedScroll = (int)(value & 0xffff) switch
                {
                    0 => _publishingAdvancedScroll - GenesisPublishingAdvancedLayout.RowHeight,
                    1 => _publishingAdvancedScroll + GenesisPublishingAdvancedLayout.RowHeight,
                    2 => _publishingAdvancedScroll - page,
                    3 => _publishingAdvancedScroll + page,
                    4 or 5 => info.Track,
                    6 => 0,
                    7 => GenesisPublishingAdvancedLayout.ContentHeight(GenesisPublishingAdvancedLayout.Rows(input).Length),
                    _ => _publishingAdvancedScroll
                };
            }
            PlacePublishingAdvancedInputs();
        }

        private void RevealPublishingAdvanced(nint window)
        {
            if (_publishingEditor?.Input is not { } input || !GetClientRect(_publishingAdvancedPane, out var area)) return;
            var rows = GenesisPublishingAdvancedLayout.Rows(input);
            var row = Array.IndexOf(rows, Array.IndexOf(_publishingAdvancedFields, window));
            if (row < 0) return;
            _publishingAdvancedScroll = GenesisPublishingAdvancedLayout.Reveal(_publishingAdvancedScroll, LogicalWidth(area.Bottom), row, rows.Length);
            PlacePublishingAdvancedInputs();
        }

        private nint[] PublishingEditTabOrder => _publishingEditAdvanced && _publishingEditor?.Input is { } input
            ? [.. GenesisPublishingAdvancedLayout.Rows(input).Where(i => i != 2 && (i != 6 || input.WanEnabled || input.AnnounceEnabled)).Select(i => _publishingAdvancedFields[i]), _publishingAdvancedSave, _publishingAdvancedCancel, _publishingEdit]
            : [.. _publishingInputs, _publishingSave, _publishingCancel, _publishingAdvancedToggle];
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint SendPublishingComboText(nint window, uint message, nuint value, string text);
}

internal static class GenesisPublishingAdvancedLayout
{
    internal static int[] Rows(PublishingSettingsInput input) => input.WanEnabled ? (input.AnnounceEnabled ? [0, 1, 2, 3, 4, 5, 6, 7, 8] : [0, 1, 2, 3, 4, 5, 6]) :
        input.LanEnabled ? (input.AnnounceEnabled ? [0, 1, 2, 3, 6, 7, 8] : [0, 1, 2, 3, 6]) : (input.AnnounceEnabled ? [0, 6, 7, 8] : [0, 6]);
    internal const int RowHeight = 38;
    internal static int ContentHeight(int rows) => rows * RowHeight + 112;
    internal static int Clamp(int scroll, int height, int rows) => Math.Clamp(scroll, 0, Math.Max(0, ContentHeight(rows) - height));
    internal static int Reveal(int scroll, int height, int row, int rows) => Clamp(row * RowHeight + 4 < scroll ? row * RowHeight + 4 : Math.Max(scroll, row * RowHeight + 34 - height), height, rows);
}

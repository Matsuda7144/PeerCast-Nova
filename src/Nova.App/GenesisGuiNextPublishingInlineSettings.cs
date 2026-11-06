using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const int PublishingEditId = 1240, PublishingSaveId = 1241, PublishingCancelId = 1242, PublishingInputId = 1243;
    private const nuint PublishingEditorSubclassId = 0x50424945;
    private static readonly SubclassProcedure PublishingEditorProcedure = ProcessPublishingEditor;
    private static readonly string[] BasicFieldNames = ["ChannelName", "Genre", "Description", "Comment", "ContactUrl", "BitrateKbps"];

    private static nint ProcessPublishingEditor(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint owner)
    {
        if (message == WindowMessageNcDestroy) _ = RemoveWindowSubclass(window, PublishingEditorProcedure, id);
        if (!StatesByOwner.TryGetValue((nint)owner, out var state)) return DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0005 && state.IsPublishingAdvancedPane(window)) state.PlacePublishingAdvancedInputs();
        if (message == WindowMessageCommand) { state.ProcessCommand(wParam, lParam); return 0; }
        if (message is 0x0115 or 0x020A) { if (state.IsPublishingAdvancedPane(window)) state.ScrollPublishingAdvanced(message, wParam); else state.ScrollPublishingEditor(message, wParam); return 0; }
        if (message is 0x0133 or 0x0134 or 0x0138) return state.PrepareDarkControl(wParam, window);
        if (message == 0x0014 && GetClientRect(window, out var area))
        { _ = FillRect((nint)wParam, ref area, state.PublishingEditBrush); return 1; }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    internal static bool TryMovePublishingInlineTab(nint owner, nint focus, bool reverse) =>
        StatesByOwner.TryGetValue(owner, out var state) && state.MovePublishingInlineTab(focus, reverse);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "Native shell Detach releases the inline editor and its pending save remains owned by the root settings controller.")]
    private sealed partial class AppShellState
    {
        private GenesisPublishingBasicEditor? _publishingEditor;
        private nint _publishingEditPane, _publishingEdit, _publishingSave, _publishingCancel, _publishingEditNote;
        private nint[] _publishingInputs = [], _publishingInputLabels = [];
        private bool _publishingWritingInputs;
        private int _publishingEditScroll;
        private nint[] PublishingEditorButtons => IsModern ? [_publishingEdit, _publishingSave, _publishingCancel, .. PublishingAdvancedButtons] : [];
        private nint[] PublishingEditorControls => IsModern ? [_publishingEditPane, _publishingEditNote, .. _publishingInputLabels, .. _publishingInputs, .. PublishingEditorButtons, .. PublishingAdvancedControls] : [];

        private void CreatePublishingInlineSettings(nint instance)
        {
            _publishingEditPane = CreateWindowExW(0x00010000, "STATIC", "", WindowStyleChild | 0x02000000 | 0x00200000,
                0, 0, 1, 1, _detailsPane, 0, instance, 0);
            _ = SetWindowSubclass(_publishingEditPane, PublishingEditorProcedure, PublishingEditorSubclassId, (nuint)_owner);
            _publishingInputLabels = new nint[6]; _publishingInputs = new nint[6];
            for (var i = 0; i < 6; i++)
            {
                _publishingInputLabels[i] = CreateWindowExW(0, "STATIC", GenesisGuiNextPublishingSettingsWindow.FieldLabels[i],
                    WindowStyleChild | WindowStyleVisible, 0, 0, 1, 1, _publishingEditPane, 0, instance, 0);
                _publishingInputs[i] = CreateWindowExW(0, "EDIT", "", WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 0x80,
                    0, 0, 1, 1, _publishingEditPane, (nint)(PublishingInputId + i), instance, 0);
                _ = SendMessageW(_publishingInputs[i], 0x00C5, 8192, 0); // same draft bound as the existing settings window
            }
            _publishingEditNote = CreateWindowExW(0, "STATIC", "申告するkbpsです。OBS送信速度は制御しません。空欄は未指定。",
                WindowStyleChild | WindowStyleVisible, 0, 0, 1, 1, _publishingEditPane, 0, instance, 0);
            _publishingEdit = CreateRecordingModeButton("編集", _detailsPane, PublishingEditId, 0, instance);
            _publishingSave = CreateRecordingModeButton("保存", _detailsPane, PublishingSaveId, 0, instance);
            _publishingCancel = CreateRecordingModeButton("キャンセル", _detailsPane, PublishingCancelId, 0, instance);
            CreatePublishingAdvanced(instance);
        }

        private bool ProcessPublishingInlineInput(int id, int code, nint control)
        {
            if (!IsModern || id < PublishingInputId || id >= PublishingInputId + 6 || _publishingInputs[id - PublishingInputId] != control) return false;
            if (code == 0x0300 && !_publishingWritingInputs && _publishingEditor is { Editing: true, Saving: false })
            {
                var values = new string[6];
                for (var i = 0; i < values.Length; i++)
                {
                    var length = GetSettingsTextLength(_publishingInputs[i]);
                    if (length is < 0 or > 8192) return true;
                    var buffer = new char[length + 1];
                    if (GetWindowTextW(_publishingInputs[i], buffer, buffer.Length) != length) return true;
                    values[i] = new(buffer, 0, length);
                }
                _publishingEditor.Edit(values[0], values[1], values[2], values[3], values[4], values[5]);
            }
            return true;
        }

        private bool ProcessPublishingInlineCommand(int id, nint control)
        {
            if (!IsModern || _publishingEditor is not { } editor) return false;
            if (id == PublishingEditId && control == _publishingEdit)
            {
                if (editor.Editing || editor.Begin())
                {
                    _publishingEditAdvanced = _publishingAdvancedExpanded = false;
                    InitializePublishingBasicInputs();
                    _publishingEditScroll = 0;
                    SynchronizeNativeSelection();
                    _ = SetFocus(_publishingInputs[0]);
                }
            }
            else if (id == PublishingSaveId && control == _publishingSave)
            {
                _ = SavePublishingInlineAsync();
            }
            else if (id == PublishingCancelId && control == _publishingCancel)
            {
                if (editor.Cancel()) { SynchronizeNativeSelection(); _ = SetFocus(_publishingEdit); }
            }
            else return false;
            SynchronizeNativeSelection();
            return true;
        }

        private async Task SavePublishingInlineAsync()
        {
            if (_publishingEditor is not { Editing: true, Saving: false } editor) return;
            var save = editor.SaveAsync();
            SynchronizeNativeSelection();
            await save.ConfigureAwait(false);
            PostPublishingUi(() =>
            {
                if (editor.Editing)
                {
                    var basic = Array.IndexOf(BasicFieldNames, editor.Result.Field);
                    if (basic >= 0)
                    {
                        if (_publishingEditAdvanced) InitializePublishingBasicInputs();
                        _publishingEditAdvanced = _publishingAdvancedExpanded = false;
                    }
                    else if (editor.Result.Field is not null || _publishingEditAdvanced || editor.Input is { AnnounceEnabled: true } || editor.Input?.AnnounceEndpoint.Length > 0)
                    {
                        if (!_publishingEditAdvanced) InitializePublishingAdvanced();
                        _publishingEditAdvanced = _publishingAdvancedExpanded = true;
                    }
                }
                SynchronizeNativeSelection();
                if (!UsesPublishingPanel) return;
                var index = Array.IndexOf(BasicFieldNames, editor.Result.Field);
                _ = SetFocus(editor.Editing && _publishingEditAdvanced ? _publishingAdvancedFields[AdvancedErrorIndex(editor.Result.Field)] :
                    editor.Editing && index >= 0 ? _publishingInputs[index] : editor.Editing ? _publishingSave : _publishingAdvancedExpanded ? _publishingAdvancedEdit : _publishingEdit);
            });
        }

        // Call before navigation, opening the legacy editor, or normal main-window close.
        internal bool CanLeavePublishingEditor()
        {
            if (!IsModern || _publishingEditor is not { Editing: true } editor) return true;
            if (editor.Saving)
            {
                _ = MessageBoxW(_owner, "設定を保存しています。完了してから操作してください。", "配信設定", 0x40);
                return false;
            }
            if (editor.Dirty && MessageBoxW(_owner,
                "未保存の配信設定（基本・詳細）をすべて破棄しますか？\r\n「いいえ」で編集を続け、保存またはキャンセルしてください。", "配信設定", 0x24) != 6) return false;
            return editor.Cancel();
        }

        private void SynchronizePublishingInline(bool visible)
        {
            var editing = _publishingEditor?.Editing == true && !_publishingEditAdvanced;
            _ = PresentVisible(_publishingBasic, visible && !editing ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var h in new[] { _publishingEditPane, _publishingEditNote }.Concat(_publishingInputLabels).Concat(_publishingInputs))
                _ = PresentVisible(h, visible && editing ? ShowWindowShowNoActivate : ShowWindowHide);
            _ = PresentVisible(_publishingEdit, visible && !editing ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var h in new[] { _publishingSave, _publishingCancel })
                _ = PresentVisible(h, visible && editing ? ShowWindowShowNoActivate : ShowWindowHide);
            foreach (var h in _publishingInputs.Concat(PublishingEditorButtons))
                _ = PresentEnabled(h, _publishingEditor is { Saving: false });
            _ = PresentText(_publishingBasicTitle, editing ? "基本設定（編集中）" : "基本設定（保存済み）");
            _ = PresentText(_publishingEdit, _publishingEditor?.Editing == true && _publishingEditAdvanced ? "基本設定へ" : "編集");
            SynchronizePublishingAdvanced(visible);
        }

        private void PlacePublishingInline(GenesisGuiNextPublishingModernLayout layout)
        {
            var box = layout.Basic;
            if (_publishingAdvancedExpanded)
            {
                _ = PresentBounds(_publishingBasic, box.Left, box.Top, box.Width, box.Height, true);
                _ = PresentBounds(_publishingEdit, box.Left, layout.BasicCard.Bottom - 34, 104, 26, true);
                PlacePublishingAdvanced(layout);
                return;
            }
            var viewHeight = Math.Max(24, box.Height - 36);
            _ = PresentBounds(_publishingBasic, box.Left, box.Top, box.Width, viewHeight, true);
            _ = PresentBounds(_publishingEditPane, box.Left, box.Top, box.Width, viewHeight, true);
            _ = PresentBounds(_publishingEdit, box.Left, box.Bottom - 28, 64, 26, true);
            _ = PresentBounds(_publishingSave, box.Left, box.Bottom - 28, 64, 26, true);
            _ = PresentBounds(_publishingCancel, box.Left + 72, box.Bottom - 28, 100, 26, true);
            PlacePublishingInlineInputs();
            PlacePublishingAdvanced(layout);
        }

        private void PlacePublishingInlineInputs()
        {
            if (!GetClientRect(_publishingEditPane, out var area)) return;
            var height = LogicalWidth(area.Bottom);
            _publishingEditScroll = GenesisPublishingBasicLayout.Clamp(_publishingEditScroll, height);
            var scroll = new PublishingEditScrollInfo
            {
                Size = (uint)Marshal.SizeOf<PublishingEditScrollInfo>(),
                Mask = 7,
                Maximum = GenesisPublishingBasicLayout.ContentHeight - 1,
                Page = (uint)Math.Max(1, height),
                Position = _publishingEditScroll
            };
            _ = SetPublishingEditScroll(_publishingEditPane, 1, ref scroll, true);
            _ = GetClientRect(_publishingEditPane, out area);
            var layout = new GenesisPublishingBasicLayout(LogicalWidth(area.Right), height, _publishingEditScroll);
            for (var i = 0; i < 6; i++)
            {
                var label = layout.Label(i); var input = layout.Input(i);
                _ = MoveWindow(_publishingInputLabels[i], Scale(label.Left), Scale(label.Top), Scale(label.Width), Scale(label.Height), true);
                _ = MoveWindow(_publishingInputs[i], Scale(input.Left), Scale(input.Top), Scale(input.Width), Scale(input.Height), true);
            }
            var note = layout.Note;
            _ = MoveWindow(_publishingEditNote, Scale(note.Left), Scale(note.Top), Scale(note.Width), Scale(note.Height), true);
            _ = RedrawWindow(_publishingEditPane, 0, 0, 0x0001 | 0x0004 | 0x0080);
        }

        internal nint PublishingEditBrush => _topBrush;

        internal void ScrollPublishingEditor(uint message, nuint value)
        {
            if (!GetClientRect(_publishingEditPane, out var area)) return;
            var page = LogicalWidth(area.Bottom);
            if (message == 0x020A) _publishingEditScroll -= (short)(value >> 16) * 48 / 120;
            else
            {
                var info = new PublishingEditScrollInfo { Size = (uint)Marshal.SizeOf<PublishingEditScrollInfo>(), Mask = 0x10 };
                _ = GetPublishingEditScroll(_publishingEditPane, 1, ref info);
                _publishingEditScroll = (int)(value & 0xffff) switch
                {
                    0 => _publishingEditScroll - 28,
                    1 => _publishingEditScroll + 28,
                    2 => _publishingEditScroll - page,
                    3 => _publishingEditScroll + page,
                    4 or 5 => info.Track,
                    6 => 0,
                    7 => GenesisPublishingBasicLayout.ContentHeight,
                    _ => _publishingEditScroll,
                };
            }
            PlacePublishingInlineInputs();
        }

        internal bool MovePublishingInlineTab(nint focus, bool reverse)
        {
            if (!UsesPublishingPanel || _publishingEditor is not { Editing: true, Saving: false }) return false;
            var order = PublishingEditTabOrder;
            var index = Array.IndexOf(order, focus);
            if (index < 0) return false;
            _ = SetFocus(order[GenesisGuiNextMainWindowKeyboardNavigation.ResolveNextIndex(order.Length, index, reverse)]);
            return true;
        }

        internal bool IsPublishingInlineInput(nint window) => IsModern && (_publishingInputs.Contains(window) || _publishingAdvancedFields.Contains(window));
        internal void PaintPublishingInlineInput(nint window)
        {
            var dc = GetDC(window);
            if (dc == 0) return;
            try
            {
                if (!GetClientRect(window, out var box)) return;
                using var pen = new VisualPen(dc, Scale(1), GetFocus() == window ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
            }
            finally { _ = ReleaseDC(window, dc); }
        }

        internal bool PublishingInlineControlMessage(nint window, uint message, nuint key)
        {
            if (!UsesPublishingPanel || !_publishingInputs.Concat(_publishingAdvancedFields).Concat(PublishingEditorButtons).Contains(window)) return false;
            var advanced = _publishingAdvancedFields.Contains(window);
            if (advanced && message == 0x0007) RevealPublishingAdvanced(window);
            var index = Array.IndexOf(_publishingInputs, window);
            if (message == 0x0007 && index >= 0 && GetClientRect(_publishingEditPane, out var area))
            {
                _publishingEditScroll = GenesisPublishingBasicLayout.Reveal(_publishingEditScroll, LogicalWidth(area.Bottom), index);
                PlacePublishingInlineInputs();
            }
            if (message == 0x020A) { if (advanced) ScrollPublishingAdvanced(message, key); else ScrollPublishingEditor(message, key); return true; }
            if (message == 0x0100 && key is 0x0D or 0x1B)
            {
                if (IsPublishingAdvancedCombo(window) && SendMessageW(window, 0x0157, 0, 0) != 0) return false;
                if (key == 0x1B) { if (CanLeavePublishingEditor()) { SynchronizeNativeSelection(); _ = SetFocus(_publishingEdit); } }
                else if (index >= 0 || advanced || window == _publishingSave || window == _publishingAdvancedSave) _ = SavePublishingInlineAsync();
                else _ = SendMessageW(window, 0x00F5, 0, 0);
                return true;
            }
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PublishingEditScrollInfo { public uint Size, Mask; public int Minimum, Maximum; public uint Page; public int Position, Track; }
    [DllImport("user32.dll", EntryPoint = "SetScrollInfo")] private static extern int SetPublishingEditScroll(nint window, int bar, ref PublishingEditScrollInfo info, bool redraw);
    [DllImport("user32.dll", EntryPoint = "GetScrollInfo")] private static extern bool GetPublishingEditScroll(nint window, int bar, ref PublishingEditScrollInfo info);
}

internal readonly record struct GenesisPublishingBasicLayout(int Width, int Height, int Scroll)
{
    internal const int ContentHeight = 236;
    internal GenesisGuiNextPublishingGuideRectangle Label(int index) => new(2, 4 + index * 30 - Scroll, 116, 26);
    internal GenesisGuiNextPublishingGuideRectangle Input(int index) => new(120, 4 + index * 30 - Scroll, Math.Max(32, Width - 124), 26);
    internal GenesisGuiNextPublishingGuideRectangle Note => new(2, 188 - Scroll, Math.Max(32, Width - 6), 44);
    internal static int Clamp(int scroll, int height) => Math.Clamp(scroll, 0, Math.Max(0, ContentHeight - height));
    internal static int Reveal(int scroll, int height, int index) => Clamp(
        index * 30 + 4 < scroll ? index * 30 + 4 : Math.Max(scroll, index * 30 + 30 - height), height);
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "ネイティブシェルの Detach() がインラインエディター等を解放し、未保存処理の所有権はルート設定コントローラーに残る。")]
internal sealed partial class GenesisPublishingTabController
{
    private const nuint PublishingEditorSubclassId = 0x50424945;
    private static readonly ConcurrentDictionary<nint, GenesisPublishingTabController> ControllersByOwner = new();
    private static readonly Win32SubclassProcedure PublishingEditorProcedure = ProcessPublishingEditor;
    private static readonly string[] BasicFieldNames = ["ChannelName", "Genre", "Description", "Comment", "ContactUrl", "BitrateKbps"];

    private GenesisPublishingBasicEditor? _publishingEditor;
    private nint _publishingEditPane, _publishingEdit, _publishingSave, _publishingCancel, _publishingEditNote;
    private nint[] _publishingInputs = [], _publishingInputLabels = [];
    private bool _publishingWritingInputs, _publishingPlacingInputs;
    private int _publishingEditScroll;

    private nint[] PublishingEditorButtons => _host.IsModern ? [_publishingEdit, _publishingSave, _publishingCancel, .. PublishingAdvancedButtons] : [];
    private nint[] PublishingEditorControls => _host.IsModern ? [_publishingEditPane, _publishingEditNote, .. _publishingInputLabels, .. _publishingInputs, .. PublishingEditorButtons, .. PublishingAdvancedControls] : [];

    private static nint ProcessPublishingEditor(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint owner)
    {
        if (message == WindowMessageNcDestroy) _ = RemoveWindowSubclass(window, PublishingEditorProcedure, id);
        if (!ControllersByOwner.TryGetValue((nint)owner, out var controller)) return DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0005)
        {
            if (controller.IsAdvancedPane(window)) controller.PlaceAdvancedInputs();
            else controller.PlaceInlineInputs();
        }
        if (message == WindowMessageCommand) { controller._host.ProcessCommand(wParam, lParam); return 0; }
        if (message is 0x0115 or 0x020A) { if (controller.IsAdvancedPane(window)) controller.ScrollAdvanced(message, wParam); else controller.ScrollEditor(message, wParam); return 0; }
        if (message is 0x0133 or 0x0134 or 0x0138) return controller._host.PrepareDarkControl(wParam, window);
        if (message == 0x0014 && GetClientRect(window, out var area))
        { _ = FillRect((nint)wParam, ref area, controller._host.TopBrush); return 1; }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void CreateInlineSettings(nint instance)
    {
        ControllersByOwner[_host.Owner] = this;
        _publishingEditPane = CreateWindowExW(0x00010000, "STATIC", "", WindowStyleChild | 0x02000000 | 0x00200000,
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0);
        _ = SetWindowSubclass(_publishingEditPane, PublishingEditorProcedure, PublishingEditorSubclassId, (nuint)_host.Owner);
        _publishingInputLabels = new nint[6]; _publishingInputs = new nint[6];
        for (var i = 0; i < 6; i++)
        {
            _publishingInputLabels[i] = CreateWindowExW(0, "STATIC", GenesisGuiNextPublishingSettingsWindow.FieldLabels[i],
                WindowStyleChild | WindowStyleVisible, 0, 0, 1, 1, _publishingEditPane, 0, instance, 0);
            _publishingInputs[i] = CreateWindowExW(0, "EDIT", "", WindowStyleChild | WindowStyleVisible | WindowStyleTabStop | 0x80,
                0, 0, 1, 1, _publishingEditPane, (nint)(PublishingInputId + i), instance, 0);
            _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetName(
                _publishingInputs[i],
                GenesisGuiNextPublishingSettingsWindow.FieldLabels[i]);
            _ = SendMessageW(_publishingInputs[i], 0x00C5, 8192, 0); // same draft bound as the existing settings window
        }
        _publishingEditNote = CreateWindowExW(0, "STATIC", "申告するkbpsです。OBS送信速度は制御しません。空欄は未指定。",
            WindowStyleChild | WindowStyleVisible, 0, 0, 1, 1, _publishingEditPane, 0, instance, 0);
        _publishingEdit = _host.CreateOwnerDrawButton("編集", _host.DetailsPane, PublishingEditId, 0, instance);
        _publishingSave = _host.CreateOwnerDrawButton("保存", _host.DetailsPane, PublishingSaveId, 0, instance);
        _publishingCancel = _host.CreateOwnerDrawButton("キャンセル", _host.DetailsPane, PublishingCancelId, 0, instance);
        CreateAdvanced(instance);
    }

    internal bool ProcessInput(int id, int code, nint control) =>
        ProcessAdvancedInput(id, code, control) || ProcessInlineInput(id, code, control);

    private bool ProcessInlineInput(int id, int code, nint control)
    {
        if (!_host.IsModern || id < PublishingInputId || id >= PublishingInputId + 6 || _publishingInputs[id - PublishingInputId] != control) return false;
        if (code == 0x0300 && !_publishingWritingInputs && _publishingEditor is { Editing: true, Saving: false })
        {
            var values = new string[6];
            for (var i = 0; i < values.Length; i++)
            {
                var length = GetWindowTextLengthW(_publishingInputs[i]);
                if (length is < 0 or > 8192) return true;
                var buffer = new char[length + 1];
                if (GetWindowTextW(_publishingInputs[i], buffer, buffer.Length) != length) return true;
                values[i] = new(buffer, 0, length);
            }
            _publishingEditor.Edit(values[0], values[1], values[2], values[3], values[4], values[5]);
        }
        return true;
    }

    private bool ProcessInlineCommand(int id, nint control)
    {
        if (!_host.IsModern || _publishingEditor is not { } editor) return false;
        if (id == PublishingEditId && control == _publishingEdit)
        {
            if (editor.Editing || editor.Begin())
            {
                _publishingEditAdvanced = _publishingAdvancedExpanded = false;
                InitializeBasicInputs();
                _publishingEditScroll = 0;
                _host.SynchronizeNativeSelection();
                _ = SetFocus(_publishingInputs[0]);
            }
        }
        else if (id == PublishingSaveId && control == _publishingSave)
        {
            _ = SaveInlineAsync();
        }
        else if (id == PublishingCancelId && control == _publishingCancel)
        {
            if (editor.Cancel()) { _host.SynchronizeNativeSelection(); _ = SetFocus(_publishingEdit); }
        }
        else return false;
        _host.SynchronizeNativeSelection();
        return true;
    }

    private async Task SaveInlineAsync()
    {
        if (_publishingEditor is not { Editing: true, Saving: false } editor) return;
        var save = editor.SaveAsync();
        _host.SynchronizeNativeSelection();
        await save.ConfigureAwait(false);
        _host.EnqueueUiAction(() =>
        {
            if (editor.Editing)
            {
                var basic = Array.IndexOf(BasicFieldNames, editor.Result.Field);
                if (basic >= 0)
                {
                    if (_publishingEditAdvanced) InitializeBasicInputs();
                    _publishingEditAdvanced = _publishingAdvancedExpanded = false;
                }
                else if (editor.Result.Field is not null || _publishingEditAdvanced || editor.Input is { AnnounceEnabled: true } || editor.Input?.AnnounceEndpoint.Length > 0)
                {
                    if (!_publishingEditAdvanced) InitializeAdvanced();
                    _publishingEditAdvanced = _publishingAdvancedExpanded = true;
                }
            }
            _host.SynchronizeNativeSelection();
            if (!UsesPanel) return;
            var index = Array.IndexOf(BasicFieldNames, editor.Result.Field);
            _ = SetFocus(editor.Editing && _publishingEditAdvanced ? _publishingAdvancedFields[AdvancedErrorIndex(editor.Result.Field)] :
                editor.Editing && index >= 0 ? _publishingInputs[index] : editor.Editing ? _publishingSave : _publishingAdvancedExpanded ? _publishingAdvancedEdit : _publishingEdit);
        });
    }

    // ナビゲーション、レガシー設定ウィンドウを開く前、またはメインウィンドウ終了前に呼び出す。
    internal bool CanLeaveEditor()
    {
        if (!_host.IsModern || _publishingEditor is not { Editing: true } editor) return true;
        if (editor.Saving)
        {
            _ = MessageBoxW(_host.Owner, "設定を保存しています。完了してから操作してください。", "配信設定", 0x40);
            return false;
        }
        if (editor.Dirty && MessageBoxW(_host.Owner,
            "未保存の配信設定（基本・詳細）をすべて破棄しますか？\r\n「いいえ」で編集を続け、保存またはキャンセルしてください。", "配信設定", 0x24) != 6) return false;
        return editor.Cancel();
    }

    private void SynchronizeInline(bool visible)
    {
        var editing = _publishingEditor?.Editing == true && !_publishingEditAdvanced;
        _ = _host.PresentVisible(_publishingBasic, visible && !editing ? ShowWindowShowNoActivate : ShowWindowHide);
        foreach (var h in new[] { _publishingEditPane, _publishingEditNote }.Concat(_publishingInputLabels).Concat(_publishingInputs))
            _ = _host.PresentVisible(h, visible && editing ? ShowWindowShowNoActivate : ShowWindowHide);
        _ = _host.PresentVisible(_publishingEdit, visible && !editing ? ShowWindowShowNoActivate : ShowWindowHide);
        foreach (var h in new[] { _publishingSave, _publishingCancel })
            _ = _host.PresentVisible(h, visible && editing ? ShowWindowShowNoActivate : ShowWindowHide);
        foreach (var h in _publishingInputs.Concat(PublishingEditorButtons))
            _ = _host.PresentEnabled(h, _publishingEditor is { Saving: false });
        _ = _host.PresentText(_publishingBasicTitle, editing ? "基本設定（編集中）" : "基本設定（保存済み）");
        _ = _host.PresentText(_publishingEdit, _publishingEditor?.Editing == true && _publishingEditAdvanced ? "基本設定へ" : "編集");
        SynchronizeAdvanced(visible);
    }

    private void PlaceInline(GenesisGuiNextPublishingModernLayout layout)
    {
        var box = layout.Basic;
        _ = _host.PresentBounds(_publishingBasic, box.Left, box.Top, box.Width, box.Height, true);
        _ = _host.PresentBounds(_publishingEditPane, box.Left, box.Top, box.Width, box.Height, true);
        var action = layout.BasicAction;
        _ = _host.PresentBounds(_publishingEdit, action.Left, action.Top, action.Width, action.Height, true);
        _ = _host.PresentBounds(_publishingSave, action.Left, action.Top, 88, action.Height, true);
        _ = _host.PresentBounds(_publishingCancel, action.Left + 100, action.Top, 128, action.Height, true);
        PlaceInlineInputs();
        PlaceAdvanced(layout);
    }

    internal void PlaceInlineInputs()
    {
        if (_publishingPlacingInputs || !GetClientRect(_publishingEditPane, out var area)) return;
        _publishingPlacingInputs = true;
        try
        {
            var height = _host.LogicalWidth(area.Bottom);
            _publishingEditScroll = GenesisPublishingBasicLayout.Clamp(_publishingEditScroll, height);
            var scroll = new Win32ScrollInfo
            {
                Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(),
                Mask = 7,
                Maximum = GenesisPublishingBasicLayout.ContentHeight - 1,
                Page = (uint)Math.Max(1, height),
                Position = _publishingEditScroll
            };
            _ = SetScrollInfo(_publishingEditPane, 1, ref scroll, true);
            _ = GetClientRect(_publishingEditPane, out area);
            var layout = new GenesisPublishingBasicLayout(_host.LogicalWidth(area.Right), height, _publishingEditScroll);
            for (var i = 0; i < 6; i++)
            {
                var label = layout.Label(i); var input = layout.Input(i);
                _ = MoveWindow(_publishingInputLabels[i], _host.Scale(label.Left), _host.Scale(label.Top), _host.Scale(label.Width), _host.Scale(label.Height), true);
                _ = MoveWindow(_publishingInputs[i], _host.Scale(input.Left), _host.Scale(input.Top), _host.Scale(input.Width), _host.Scale(input.Height), true);
            }
            var note = layout.Note;
            _ = MoveWindow(_publishingEditNote, _host.Scale(note.Left), _host.Scale(note.Top), _host.Scale(note.Width), _host.Scale(note.Height), true);
            _ = RedrawWindow(_publishingEditPane, 0, 0, 0x0001 | 0x0004 | 0x0080);
        }
        finally { _publishingPlacingInputs = false; }
    }

    internal void ScrollEditor(uint message, nuint value)
    {
        if (!GetClientRect(_publishingEditPane, out var area)) return;
        var page = _host.LogicalWidth(area.Bottom);
        if (message == 0x020A) _publishingEditScroll -= (short)(value >> 16) * 48 / 120;
        else
        {
            var info = new Win32ScrollInfo { Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(), Mask = 0x10 };
            _ = GetScrollInfo(_publishingEditPane, 1, ref info);
            _publishingEditScroll = (int)(value & 0xffff) switch
            {
                0 => _publishingEditScroll - GenesisPublishingBasicLayout.RowHeight,
                1 => _publishingEditScroll + GenesisPublishingBasicLayout.RowHeight,
                2 => _publishingEditScroll - page,
                3 => _publishingEditScroll + page,
                4 or 5 => info.TrackPosition,
                6 => 0,
                7 => GenesisPublishingBasicLayout.ContentHeight,
                _ => _publishingEditScroll,
            };
        }
        PlaceInlineInputs();
    }

    internal bool MoveInlineTab(nint focus, bool reverse)
    {
        if (!UsesPanel || _publishingEditor is not { Editing: true, Saving: false }) return false;
        var order = PublishingEditTabOrder;
        var index = Array.IndexOf(order, focus);
        if (index < 0) return false;
        _ = SetFocus(order[GenesisGuiNextMainWindowKeyboardNavigation.ResolveNextIndex(order.Length, index, reverse)]);
        return true;
    }

    internal bool IsInlineInput(nint window) => _host.IsModern && (_publishingInputs.Contains(window) || _publishingAdvancedFields.Contains(window));

    internal bool IsEditorPane(nint pane) => _host.IsModern && (pane == _publishingEditPane || pane == _publishingAdvancedPane);

    internal void PaintInlineInput(nint window)
    {
        var dc = GetDC(window);
        if (dc == 0) return;
        try
        {
            if (!GetClientRect(window, out var box)) return;
            using var pen = new GenesisVisualPen(dc, _host.Scale(1), GetFocus() == window ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
        }
        finally { _ = ReleaseDC(window, dc); }
    }

    internal bool HandleInlineControlMessage(nint window, uint message, nuint key)
    {
        if (!UsesPanel || !_publishingInputs.Concat(_publishingAdvancedFields).Concat(PublishingEditorButtons).Contains(window)) return false;
        var advanced = _publishingAdvancedFields.Contains(window);
        if (advanced && message == 0x0007) RevealAdvanced(window);
        var index = Array.IndexOf(_publishingInputs, window);
        if (message == 0x0007 && index >= 0 && GetClientRect(_publishingEditPane, out var area))
        {
            _publishingEditScroll = GenesisPublishingBasicLayout.Reveal(_publishingEditScroll, _host.LogicalWidth(area.Bottom), index);
            PlaceInlineInputs();
        }
        if (message == 0x020A && IsAdvancedCombo(window) && SendMessageW(window, 0x0157, 0, 0) != 0) return false;
        if (message == 0x020A) { if (advanced) ScrollAdvanced(message, key); else ScrollEditor(message, key); return true; }
        if (message == 0x0100 && key is 0x0D or 0x1B)
        {
            if (IsAdvancedCombo(window) && SendMessageW(window, 0x0157, 0, 0) != 0) return false;
            if (key == 0x1B) { if (CanLeaveEditor()) { _host.SynchronizeNativeSelection(); _ = SetFocus(_publishingEdit); } }
            else if (index >= 0 || advanced || window == _publishingSave || window == _publishingAdvancedSave) _ = SaveInlineAsync();
            else _ = SendMessageW(window, 0x00F5, 0, 0);
            return true;
        }
        return false;
    }
}

internal readonly record struct GenesisPublishingBasicLayout(int Width, int Height, int Scroll)
{
    internal const int RowHeight = 38;
    internal const int ContentHeight = 288;
    internal GenesisGuiNextPublishingGuideRectangle Label(int index) => new(2, 4 + index * RowHeight - Scroll, 116, 30);
    internal GenesisGuiNextPublishingGuideRectangle Input(int index) => new(120, 4 + index * RowHeight - Scroll, Math.Max(32, Width - 124), 30);
    internal GenesisGuiNextPublishingGuideRectangle Note => new(2, 6 * RowHeight + 8 - Scroll, Math.Max(32, Width - 6), 48);
    internal static int Clamp(int scroll, int height) => Math.Clamp(scroll, 0, Math.Max(0, ContentHeight - height));
    internal static int Reveal(int scroll, int height, int index) => Clamp(
        index * RowHeight + 4 < scroll ? index * RowHeight + 4 : Math.Max(scroll, index * RowHeight + 34 - height), height);
}

using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const nuint ConnectionBodySubclassId = 0x4E43424F;
    private static readonly Win32SubclassProcedure ConnectionBodyProcedure = ProcessConnectionBody;

    internal static nint CreateConnectionBodyWindow(nint parent, nint instance, nint owner)
    {
        var window = CreateWindowExW(0x00010000, "STATIC", "", WindowStyleChild | 0x02000000 | 0x00200000,
            0, 0, 1, 1, parent, 0, instance, 0);
        _ = SetWindowSubclass(window, ConnectionBodyProcedure, ConnectionBodySubclassId, (nuint)owner);
        return window;
    }

    private static nint ProcessConnectionBody(nint window, uint message, nuint value, nint data, nuint id, nuint owner)
    {
        // STATIC otherwise returns HTTRANSPARENT and consumes non-client clicks.
        // This scroll container needs normal window hit testing and scrollbar tracking.
        if (message is 0x0084 or 0x00A1 or 0x00A3) return DefWindowProcW(window, message, value, data);
        if (message == WindowMessageNcDestroy) _ = RemoveWindowSubclass(window, ConnectionBodyProcedure, id);
        if (!StatesByOwner.TryGetValue((nint)owner, out var state)) return DefSubclassProc(window, message, value, data);
        if (message == WindowMessageCommand) { state.ProcessCommand(value, data); return 0; }
        if (message is 0x0115 or 0x020A) { state.ScrollConnectionBody(message, value); return 0; }
        if (message is 0x0133 or 0x0134) return state.PrepareDarkControl(value, GetParent(window));
        if (message is 0x0135 or 0x0138) return state.PrepareStaticControl(value, GetParent(window));
        if (message == 0x0014) return 1;
        if (message == WindowMessagePaint) { state.PaintConnectionBody(); return 0; }
        return DefSubclassProc(window, message, value, data);
    }

    private sealed partial class AppShellState
    {
        private const string ConnectionNotice = GenesisConnectionSettingsText.Notice;
        private nint _connectionBody, _connectionNotice, _connectionRuntime, _connectionSaved, _connectionStatus;
        private int _connectionScroll, _connectionContentHeight;
        private bool _placingConnectionBody;
        private nint[] ConnectionBodyTexts => [_connectionNotice, _connectionRuntime, _connectionSaved, _connectionStatus];
        private nint[] ConnectionBodyControls => [.. ConnectionBodyTexts, .. _connectionLabels, .. _connectionInputs, _connectionWan, _natSettingsStatus];

        private void CreateConnectionBody(nint instance)
        {
            _connectionBody = CreateConnectionBodyWindow(_settingsPane, instance, _owner);
            nint Text() => CreateWindowExW(0, "STATIC", "", WindowStyleChild | WindowStyleVisible,
                0, 0, 1, 1, _connectionBody, 0, instance, 0);
            _connectionNotice = Text(); _connectionRuntime = Text(); _connectionSaved = Text(); _connectionStatus = Text();
        }

        private void PlaceConnectionBody()
        {
            if (!ConnectionCategory || _placingConnectionBody || !GetClientRect(_connectionBody, out var area) ||
                !GetWindowRect(_connectionBody, out var bodyBounds)) return;
            _placingConnectionBody = true;
            try
            {
                // Reserve scrollbar width even when it disappears, keeping wrapping stable.
                var width = Math.Max(1, LogicalWidth(bodyBounds.Right - bodyBounds.Left) - 24);
                var page = LogicalWidth(area.Bottom);
                var dc = GetDC(_connectionBody);
                if (dc == 0) return;
                var placements = new List<(nint Control, GenesisGuiNextAppShellPlacement.WindowRectangle Box)>();
                try
                {
                    using var font = GenesisWindowUiFont.SelectForDrawing(dc, _connectionNotice);
                    int TextHeight(nint control, int w)
                    {
                        var box = new Win32Rect { Right = Scale(w) };
                        var height = DrawTextW(dc, ReadCardControlText(control), -1, ref box, 0x0010 | 0x0400 | 0x8000);
                        return Math.Max(20, LogicalWidth(height) + 4);
                    }
                    void Add(nint control, int x, int y, int w, int h) => placements.Add((control, new(x, y, x + w, y + h)));
                    var top = 4;
                    var height = TextHeight(_connectionNotice, width);
                    Add(_connectionNotice, 2, top, width, height); top += height + 8;
                    var column = (width - 16) / 2;
                    height = Math.Max(TextHeight(_connectionRuntime, column), TextHeight(_connectionSaved, column));
                    Add(_connectionRuntime, 2, top, column, height);
                    Add(_connectionSaved, column + 18, top, column, height); top += height + 8;
                    var fieldWidth = width / 3;
                    for (var i = 0; i < 6; i++)
                    {
                        var x = 2 + i % 3 * fieldWidth; var y = top + i / 3 * 34;
                        Add(_connectionLabels[i], x, y, 144, 26);
                        Add(_connectionInputs[i], x + 148, y, fieldWidth - 160, 26);
                    }
                    top += 68;
                    Add(_connectionWan, 2, top, width, 26); top += 30;
                    height = TextHeight(_natSettingsStatus, width);
                    Add(_natSettingsStatus, 2, top, width, height); top += height + 8;
                    height = TextHeight(_connectionStatus, width);
                    Add(_connectionStatus, 2, top, width, height);
                    _connectionContentHeight = top + height + 4;
                }
                finally { _ = ReleaseDC(_connectionBody, dc); }
                _connectionScroll = GenesisConnectionBodyLayout.Clamp(_connectionScroll, page, _connectionContentHeight);
                var scroll = new Win32ScrollInfo
                {
                    Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(),
                    Mask = 7,
                    Maximum = _connectionContentHeight - 1,
                    Page = (uint)Math.Max(1, page),
                    Position = _connectionScroll
                };
                _ = SetScrollInfo(_connectionBody, 1, ref scroll, true);
                foreach (var (control, box) in placements)
                    _ = MoveWindow(control, Scale(box.Left), Scale(box.Top - _connectionScroll), Scale(box.Width), Scale(box.Height), true);
                _ = RedrawWindow(_connectionBody, 0, 0, 0x0001 | 0x0004 | 0x0080);
            }
            finally { _placingConnectionBody = false; }
        }

        internal void ScrollConnectionBody(uint message, nuint value)
        {
            if (!ConnectionCategory || !GetClientRect(_connectionBody, out var area)) return;
            var page = LogicalWidth(area.Bottom);
            var info = new Win32ScrollInfo { Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(), Mask = 0x10 };
            _ = GetScrollInfo(_connectionBody, 1, ref info);
            _connectionScroll = message == 0x020A ? _connectionScroll - (short)(value >> 16) * 48 / 120 : (int)(value & 0xffff) switch
            {
                0 => _connectionScroll - 24,
                1 => _connectionScroll + 24,
                2 => _connectionScroll - page,
                3 => _connectionScroll + page,
                4 or 5 => info.TrackPosition,
                6 => 0,
                7 => _connectionContentHeight,
                _ => _connectionScroll,
            };
            PlaceConnectionBody();
        }

        internal bool ConnectionBodyControlMessage(nint window, uint message, nuint value)
        {
            if (!ConnectionCategory || !ConnectionBodyControls.Contains(window)) return false;
            if (message == 0x020A) { ScrollConnectionBody(message, value); return true; }
            if (message == 0x0007 && GetWindowRect(window, out var box) && GetClientRect(_connectionBody, out var area))
            {
                var point = new Win32Point { X = box.Left, Y = box.Top };
                _ = ScreenToClient(_connectionBody, ref point);
                var top = LogicalWidth(point.Y) + _connectionScroll;
                _connectionScroll = GenesisConnectionBodyLayout.Reveal(_connectionScroll, LogicalWidth(area.Bottom),
                    top, top + LogicalWidth(box.Bottom - box.Top), _connectionContentHeight);
                PlaceConnectionBody();
            }
            return false;
        }

        internal void PaintConnectionBody()
        {
            var dc = BeginPaint(_connectionBody, out var paint);
            try
            {
                if (dc == 0 || !GetClientRect(_connectionBody, out var area)) return;
                _ = FillRect(dc, ref area, _topBrush);
                foreach (var input in _connectionInputs)
                {
                    if (!GetWindowRect(input, out var box)) continue;
                    var point = new Win32Point { X = box.Left, Y = box.Top }; _ = ScreenToClient(_connectionBody, ref point);
                    using var pen = new VisualPen(dc, Scale(1), GetFocus() == input ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                    _ = GdiRectangle(dc, point.X - 1, point.Y - 1, point.X + box.Right - box.Left + 1, point.Y + box.Bottom - box.Top + 1);
                }
            }
            finally { _ = EndPaint(_connectionBody, ref paint); }
        }
    }
}

internal static class GenesisConnectionBodyLayout
{
    internal static int Clamp(int scroll, int page, int content) => Math.Clamp(scroll, 0, Math.Max(0, content - page));
    internal static int Reveal(int scroll, int page, int top, int bottom, int content) =>
        Clamp(top < scroll ? top : Math.Max(scroll, bottom - page), page, content);
}

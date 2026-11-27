using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private const nuint VisualSubclassId = 0x4E56534C;
    private static readonly Win32SubclassProcedure VisualProcedure = ProcessVisualMessage;

    private static nint ProcessVisualMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint owner)
    {
        if (message == WindowMessageNcDestroy)
            _ = RemoveWindowSubclass(window, VisualProcedure, id);
        else if (StatesByOwner.TryGetValue((nint)owner, out var state))
        {
            if (window == (nint)owner && message is (0x001A or 0x031A)) state.RefreshWindowChrome();
            if (window == (nint)owner && message == 0x0010 && !state.Publishing.CanLeaveEditor()) return 0;
            if (state.Publishing.HandleInlineControlMessage(window, message, wParam)) return 0;
            if (state.Settings.HandleControlMessage(window, message, wParam)) return 0;
            if (state.Publishing.IsInlineInput(window))
            {
                if (message is 0x0007 or 0x0008) _ = RedrawWindow(window, 0, 0, 0x0001);
                if (message is WindowMessagePaint or 0x0085)
                {
                    var painted = DefSubclassProc(window, message, wParam, lParam);
                    if (state.Publishing.IsAdvancedCombo(window)) state.PaintTopInputFrame(window);
                    else state.Publishing.PaintInlineInput(window);
                    return painted;
                }
            }
            if (state.RelayTree.HandleContextMenu(window, message, lParam)) return 0;
            if (window == GetParent(state.ListWindow) && message == WindowMessageCommand &&
                state.RelayTree.ProcessCommand((int)(wParam & 0xffff), lParam, (int)((wParam >> 16) & 0xffff))) return 0;
            if (message == WindowMessageDrawItem && state.RelayTree.DrawControl(lParam)) return 1;
            if (message == WindowMessageNotify && state.RelayTree.HandleNotify(lParam)) return 1;
            if (state.ListHeaderWindow != 0 && window == state.ListHeaderWindow && message == 0x0014 &&
                state.HeaderBrush != 0 && GetClientRect(window, out var headerRect))
            {
                _ = FillRect((nint)wParam, ref headerRect, state.HeaderBrush);
                return 1;
            }
            if (window == state.ListWindow)
            {
                if (message == 0x0014 && state.ListBrush != 0 && GetClientRect(window, out var listRect))
                {
                    _ = FillRect((nint)wParam, ref listRect, state.ListBrush);
                    return 1;
                }
                var result = DefSubclassProc(window, message, wParam, lParam);
                if (message is WindowMessagePaint or 0x0085) state.PaintModernListFrame();
                return result;
            }
            if (message == 0x0014 && state.IsOwnerDrawVisualButton(window)) return 1;
            if (message == WindowMessageNotify && state.HandleListViewCustomDraw(lParam, out var drawResult))
                return drawResult;
            if (state.PaintTopStatus(window, message)) return message == 0x0014 ? 1 : 0;
            if (state.IsTopInput(window) && message is (0x0007 or 0x0008))
                _ = RedrawWindow(window, 0, 0, 0x0001 | 0x0400); // repaint the native focus border
            if (state.IsTopInput(window) && message is (0x0085 or WindowMessagePaint))
            {
                var result = DefSubclassProc(window, message, wParam, lParam);
                state.PaintTopInputFrame(window);
                return result;
            }
            state.Settings.RefreshOnActivation(window, message, wParam);
            if (message is 0x0007 or 0x0008) state.Settings.RefreshPathFocus(window);
            if (state.Settings.PaintText(window, message)) return message == 0x0014 ? 1 : 0;
            if (state.Publishing.PaintText(window, message)) return message == 0x0014 ? 1 : 0;
            if (message is 0x0007 or 0x0008) state.Publishing.RefreshFocus(window);
            if (state.PaintRecordingText(window, message)) return message == 0x0014 ? 1 : 0;
            if (message is 0x0007 or 0x0008) state.RefreshRecordingFocus(window);
            if (state.PaintChannelCardText(window, message)) return message == 0x0014 ? 1 : 0;
            if (message is 0x0200 or 0x02A3) state.TrackControlHover(window, message);
            if ((window == GetParent(state.ListWindow) || window == (nint)owner) &&
                message == 0x0014 && GetClientRect(window, out var rectangle))
            {
                _ = FillRect((nint)wParam, ref rectangle, state.MainBrush);
                return 1;
            }
            if (window == GetParent(state.ListWindow) && message == WindowMessageControlColorStatic)
            {
                _ = SetBkColor((nint)wParam, GenesisGuiNextDesign.Background);
                _ = SetTextColor((nint)wParam, GenesisGuiNextDesign.SecondaryText);
                return state.MainBrush;
            }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private sealed partial class AppShellState
    {
        private nint _mainBrush, _sidebarBrush, _topBrush, _inputBrush, _listBrush, _headerBrush, _listHeader;
        private readonly bool _modernAppearance;
        internal bool IsModern => _modernAppearance;
        internal nint ListWindow => _listView;
        internal nint ListHeaderWindow => _listHeader;
        internal nint MainBrush => _mainBrush;
        internal nint ListBrush => _listBrush;
        internal nint HeaderBrush => _headerBrush;
        internal bool IsOwnerDrawVisualButton(nint window) =>
            VisualButtons.Contains(window) || CardButtons.Contains(window) || RecordingToolbarControls.Contains(window);
        private nint _hoverControl;
        private int _hoverRow = -1, _paintSelectedRow = -1;
        private nint _rowImages, _originalImages;
        private uint _visualDpi;
        private int _visualsDetached;
        private GenesisGuiNextWindowChrome? _windowChrome;
        internal void RefreshWindowChrome() => _windowChrome?.Refresh();
        private GenesisUiFontLease? _visualFont;
        private readonly Dictionary<nint, nint> _originalFonts = new();
        private nint[] VisualButtons => [.. _navigationButtons, _refreshButton, _favoriteButton];
        private nint[] VisualFontControls => [.. VisualButtons, .. RecordingVisualControls, .. _publishing.VisualControls, .. _settings.VisualControls, .. _relayTree.VisualControls, _publishing.ConnectionStateWindow, _listView, TopStatusWindow, _searchBox.LabelWindow,
            _searchBox.EditWindow, _searchBox.FilterWindow];

        internal void AttachVisuals()
        {
            if (!IsModern) return;
            _mainBrush = CreateSolidBrush(GenesisGuiNextDesign.Background);
            _sidebarBrush = CreateSolidBrush(GenesisGuiNextDesign.Sidebar);
            _topBrush = CreateSolidBrush(GenesisGuiNextDesign.Panel);
            _inputBrush = CreateSolidBrush(GenesisGuiNextDesign.Input);
            _listBrush = CreateSolidBrush(GenesisGuiNextDesign.List);
            _headerBrush = CreateSolidBrush(GenesisGuiNextDesign.Header);
            foreach (var control in VisualSubclassControls)
                _ = SetWindowSubclass(control, VisualProcedure, VisualSubclassId, (nuint)_owner);
            _listHeader = SendMessageW(_listView, 0x101F, 0, 0);
            if (_listHeader != 0)
            {
                _ = SetWindowTheme(_listHeader, " ", " ");
                _ = SetWindowSubclass(_listHeader, VisualProcedure, VisualSubclassId, (nuint)_owner);
            }
            _windowChrome = GenesisGuiNextWindowChrome.TryCreate(_owner, IsModern);
        }

        internal void DetachVisuals()
        {
            if (!IsModern || Interlocked.Exchange(ref _visualsDetached, 1) != 0) return;
            _windowChrome?.Dispose(); _windowChrome = null;
            if (_listHeader != 0 && IsWindow(_listHeader)) _ = RemoveWindowSubclass(_listHeader, VisualProcedure, VisualSubclassId);
            _listHeader = 0;
            foreach (var control in VisualSubclassControls)
                if (IsWindow(control)) _ = RemoveWindowSubclass(control, VisualProcedure, VisualSubclassId);
            foreach (var (control, font) in _originalFonts)
                if (IsWindow(control) && SendMessageW(control, 0x0031, 0, 0) == _visualFont?.Handle)
                    _ = SendMessageW(control, 0x0030, (nuint)font, 1);
            _originalFonts.Clear();
            _visualFont?.Dispose();
            _visualFont = null;
            _cardTitleFont?.Dispose();
            _cardTitleFont = null;
            _channelNameFont?.Dispose(); _channelNameFont = null;
            if (_rowImages != 0)
            {
                if (IsWindow(_listView)) _ = SendMessageW(_listView, 0x1003, 1, _originalImages);
                _ = ImageList_Destroy(_rowImages);
                _rowImages = 0;
            }
            foreach (var brush in new[] { _mainBrush, _sidebarBrush, _topBrush, _inputBrush, _listBrush, _headerBrush })
                if (brush != 0) _ = DeleteObject(brush);
            _mainBrush = _sidebarBrush = _topBrush = _inputBrush = _listBrush = _headerBrush = 0;
        }

        private void UpdateVisualDpi(uint dpi)
        {
            if (!IsModern || _visualDpi == dpi) return;
            var font = GenesisUiFontLease.TryCreate(new SystemUiFontNativeApi(), dpi, GenesisGuiNextDesign.BodyFontHeight);
            if (font is not null)
            {
                var header = SendMessageW(_listView, 0x101F, 0, 0);
                var headerFont = SendMessageW(header, 0x0031, 0, 0);
                foreach (var control in VisualFontControls)
                {
                    var current = SendMessageW(control, 0x0031, 0, 0);
                    if (_visualFont is null || current != _visualFont.Handle) _originalFonts[control] = current;
                    _ = SendMessageW(control, 0x0030, (nuint)font.Handle, 1);
                }
                // Compact column labels retain the existing Japanese header font.
                _ = SendMessageW(header, 0x0030, (nuint)headerFont, 1);
                _visualFont?.Dispose();
                _visualFont = font;
            }
            var titleFont = GenesisUiFontLease.TryCreate(new SystemUiFontNativeApi(), dpi, GenesisGuiNextDesign.CardTitleFontHeight);
            if (titleFont is not null)
            {
                _cardTitleFont?.Dispose();
                _cardTitleFont = titleFont;
            }
            _channelNameFont?.Dispose();
            _channelNameFont = GenesisUiFontLease.TryCreate(new SystemUiFontNativeApi(), dpi, 16);
            SetModernRowHeight(UsesChannelListStyle ? GenesisGuiNextChannelListStyle.RowHeight : GenesisGuiNextDesign.RowImageHeight, dpi);
            _channelListStyle = null;
            _visualDpi = dpi;
            _searchBox.UpdateItemHeight(dpi);
        }

        internal bool PaintPaneBackground(nint pane, nint dc)
        {
            if (!IsModern) return false;
            if (_settings.PaintBackground(pane, dc)) return true;
            if (_publishing.PaintPanel(pane, dc)) return true;
            if (PaintRecordingPanel(pane, dc)) return true;
            if (PaintChannelCard(pane, dc)) return true;
            var brush = pane == _sidebar ? _sidebarBrush : pane == _statusBar ? _topBrush : 0;
            if (brush == 0 || !GetClientRect(pane, out var rectangle)) return false;
            _ = FillRect(dc, ref rectangle, brush);
            if (pane == _statusBar)
            {
                rectangle.Top = Math.Max(0, rectangle.Bottom - Scale(1));
                FillRectangle(dc, rectangle, GenesisGuiNextDesign.Border);
            }
            return true;
        }

        internal void TrackControlHover(nint control, uint message)
        {
            if (control == GetParent(_listView) || CardTexts.Contains(control)) return;
            var next = message == 0x02A3 ? 0 : control;
            var old = _hoverControl;
            if (old == next) return;
            _hoverControl = next;
            if (old != 0)
            {
                _ = InvalidateRect(old, 0, false);
                if (IsTopInput(old)) _ = RedrawWindow(old, 0, 0, 0x0001 | 0x0400);
            }
            if (next != 0)
            {
                RequestMouseLeave(next);
                _ = InvalidateRect(next, 0, false);
                if (IsTopInput(next)) _ = RedrawWindow(next, 0, 0, 0x0001 | 0x0400);
            }
        }

        internal void TrackListHover(uint message, nint lParam)
        {
            var hit = new Win32ListViewHitTest
            {
                Point = new Win32Point((short)((long)lParam & 0xffff), (short)(((long)lParam >> 16) & 0xffff)),
            };
            var next = message == 0x02A3 ? -1 : SendMessageW(_listView, 0x1012, 0, ref hit).ToInt32();
            if (next == _hoverRow) return;
            if (_hoverRow >= 0) _ = SendMessageW(_listView, 0x1015, (nuint)_hoverRow, _hoverRow);
            _hoverRow = next;
            if (next >= 0) _ = SendMessageW(_listView, 0x1015, (nuint)next, next);
            if (message != 0x02A3) RequestMouseLeave(_listView);
        }

        private void PaintRowSeparator(Win32CustomDraw draw)
        {
            var line = draw.ItemRectangle;
            if (GetClientRect(_listView, out var client)) line.Right = client.Right;
            line.Top = Math.Max(line.Top, line.Bottom - Scale(1));
            FillRectangle(draw.DeviceContext, line, GenesisGuiNextDesign.Separator);
        }

        private bool DrawTopButton(Win32DrawItem draw)
        {
            var disabled = (draw.ItemState & DrawItemDisabled) != 0;
            var active = (draw.ItemState & 0x0001) != 0;
            var colors = GenesisGuiNextDesign.TopButtonColors(draw.ControlWindow == _refreshButton, disabled,
                _hoverControl == draw.ControlWindow, active, (draw.ItemState & DrawItemFocused) != 0);
            FillRectangle(draw.DeviceContext, draw.ItemRectangle, colors.Background);
            DrawModernSurface(draw.DeviceContext, draw.ItemRectangle, colors.Background, colors.Border, Scale(6));
            var label = new char[128];
            var length = GetWindowTextW(draw.ControlWindow, label, label.Length);
            _ = SetBkMode(draw.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(draw.DeviceContext, colors.Text);
            var rectangle = draw.ItemRectangle;
            rectangle.Left += Scale(4); rectangle.Right -= Scale(4);
            _ = DrawTextW(draw.DeviceContext, new string(label, 0, length), -1, ref rectangle,
                DrawTextSingleLine | DrawTextVerticalCenter | DrawTextNoPrefix | 0x0001 | 0x8000);
            if ((draw.ItemState & DrawItemFocused) != 0) _ = DrawFocusRect(draw.DeviceContext, ref rectangle);
            return true;
        }

        private bool DrawYellowPageItem(Win32DrawItem draw)
        {
            var selected = (draw.ItemState & 1) != 0;
            FillRectangle(draw.DeviceContext, draw.ItemRectangle,
                selected ? GenesisGuiNextDesign.Selected : GenesisGuiNextDesign.Input);
            if (draw.ItemId == uint.MaxValue) return true;
            var length = SendMessageW(draw.ControlWindow, 0x0149, draw.ItemId, 0).ToInt32();
            if (length is < 0 or > 4096) return true;
            var text = new char[length + 1];
            _ = SendMessageW(draw.ControlWindow, 0x0148, draw.ItemId, text);
            _ = SetTextColor(draw.DeviceContext, (draw.ItemState & DrawItemDisabled) != 0 ? GenesisGuiNextDesign.DisabledText : GenesisGuiNextDesign.Text);
            _ = SetBkMode(draw.DeviceContext, TransparentBackgroundMode);
            var bounds = draw.ItemRectangle;
            bounds.Left += Scale(6); bounds.Right -= Scale(4);
            _ = DrawTextW(draw.DeviceContext, new string(text, 0, length), -1, ref bounds,
                DrawTextSingleLine | DrawTextVerticalCenter | DrawTextNoPrefix | 0x8000);
            if ((draw.ItemState & DrawItemFocused) != 0) _ = DrawFocusRect(draw.DeviceContext, ref bounds);
            return true;
        }

        internal bool DrawListHeader(nint pointer, out nint result)
        {
            result = 0;
            if (!IsModern || pointer == 0 || Volatile.Read(ref _detached) != 0) return false;
            var notification = Marshal.PtrToStructure<Win32NotifyHeader>(pointer);
            var header = SendMessageW(_listView, 0x101F, 0, 0);
            if (notification.WindowFrom != header || notification.Code != NotifyCustomDraw) return false;
            var draw = Marshal.PtrToStructure<Win32CustomDraw>(pointer);
            if (draw.DrawStage == CustomDrawStagePrePaint)
            {
                result = CustomDrawNotifyItemDraw | CustomDrawNotifyPostPaint;
                return true;
            }
            if (draw.DrawStage == 0x00000002) // CDDS_POSTPAINT: paint the unused header area.
            {
                var count = SendMessageW(header, 0x1200, 0, 0).ToInt32();
                if (GetClientRect(header, out var client))
                {
                    for (var i = 0; i < count; i++)
                    {
                        var item = new Win32Rect();
                        if (SendMessageW(header, 0x1207, (nuint)i, ref item) != 0)
                            client.Left = Math.Max(client.Left, item.Right);
                    }
                    if (client.Left < client.Right) FillRectangle(draw.DeviceContext, client, GenesisGuiNextDesign.Header);
                }
                return true;
            }
            if (draw.DrawStage != CustomDrawStageItemPrePaint || draw.ItemSpec > int.MaxValue) return false;
            var buffer = Marshal.AllocHGlobal(512);
            try
            {
                var item = new Win32HeaderItem { Mask = 0x0002 | 0x0004, Text = buffer, TextCharacters = 256 };
                if (SendMessageW(header, 0x120B, draw.ItemSpec, ref item) == 0) return false;
                var text = UsesStyledChannelColumns && ReferenceEquals(_paintColumns, SelectedListColumns) && draw.ItemSpec == 1
                    ? "詳細" : Marshal.PtrToStringUni(buffer) ?? string.Empty;
                FillRectangle(draw.DeviceContext, draw.ItemRectangle, GenesisGuiNextDesign.Header);
                var line = draw.ItemRectangle; line.Top = line.Bottom - Scale(1);
                FillRectangle(draw.DeviceContext, line, GenesisGuiNextDesign.Border);
                using var font = GenesisWindowUiFont.SelectForDrawing(draw.DeviceContext, header);
                _ = SetBkMode(draw.DeviceContext, TransparentBackgroundMode);
                _ = SetTextColor(draw.DeviceContext, GenesisGuiNextDesign.SecondaryText);
                var rectangle = draw.ItemRectangle;
                rectangle.Left += Scale(8); rectangle.Right -= Scale(8);
                if ((item.Format & 0x0600) != 0)
                {
                    var x = rectangle.Right - Scale(5); var y = (rectangle.Top + rectangle.Bottom) / 2;
                    using var pen = new GenesisVisualPen(draw.DeviceContext, Scale(1), GenesisGuiNextDesign.Accent);
                    var direction = (item.Format & 0x0400) != 0 ? -1 : 1;
                    Line(draw.DeviceContext, x - Scale(3), y - direction * Scale(2), x, y + direction * Scale(2));
                    Line(draw.DeviceContext, x, y + direction * Scale(2), x + Scale(3), y - direction * Scale(2));
                    rectangle.Right -= Scale(14);
                }
                var alignment = (item.Format & 3) == 1 ? 2u : (item.Format & 3) == 2 ? 1u : 0u;
                _ = DrawTextW(draw.DeviceContext, text, -1, ref rectangle,
                    DrawTextSingleLine | DrawTextVerticalCenter | DrawTextNoPrefix | 0x8000 | alignment);
                result = 4; // CDRF_SKIPDEFAULT: retain native header input, draw only its pixels.
                return true;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private void DrawNavigationIcon(nint dc, int index, Win32Rect bounds, bool disabled)
        {
            using var pen = new GenesisVisualPen(dc, Scale(1), disabled ? GenesisGuiNextDesign.SecondaryText : GenesisGuiNextDesign.Text);
            var left = bounds.Left + Scale(12);
            var top = bounds.Top + (bounds.Bottom - bounds.Top - Scale(18)) / 2;
            int X(int x) => left + Scale(x);
            int Y(int y) => top + Scale(y);
            void L(int x, int y, int x2, int y2) => Line(dc, X(x), Y(y), X(x2), Y(y2));
            void Circle(int x, int y, int x2, int y2) => _ = Ellipse(dc, X(x), Y(y), X(x2), Y(y2));
            void Box(int x, int y, int x2, int y2) => _ = GdiRectangle(dc, X(x), Y(y), X(x2), Y(y2));
            switch (index)
            {
                case 0:
                    for (var i = 0; i < 3; i++) { var y = 3 + i * 6; L(0, y, 2, y); L(6, y, 18, y); }
                    break;
                case 1:
                case 2:
                    var points = Enumerable.Range(0, 11).Select(i =>
                    {
                        var angle = -Math.PI / 2 + i * Math.PI / 5;
                        var radius = i % 2 == 0 ? 9 : 4;
                        return new Win32Point(X(9 + (int)Math.Round(Math.Cos(angle) * radius)),
                            Y(9 + (int)Math.Round(Math.Sin(angle) * radius)));
                    }).ToArray();
                    _ = Polyline(dc, points, points.Length);
                    break;
                case 3: Box(0, 2, 18, 15); L(7, 6, 12, 9); L(12, 9, 7, 12); L(7, 12, 7, 6); break;
                case 4: Circle(0, 0, 18, 18); Circle(5, 5, 13, 13); break;
                case 5: Circle(0, 0, 13, 13); L(12, 12, 18, 18); break;
                case 6: L(9, 14, 9, 0); L(9, 0, 3, 6); L(9, 0, 15, 6); L(0, 17, 18, 17); break;
                case 7:
                    Circle(3, 3, 15, 15); Circle(7, 7, 11, 11);
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * Math.PI / 4;
                        L(9 + (int)Math.Round(6 * Math.Cos(angle)), 9 + (int)Math.Round(6 * Math.Sin(angle)),
                            9 + (int)Math.Round(9 * Math.Cos(angle)), 9 + (int)Math.Round(9 * Math.Sin(angle)));
                    }
                    break;
                case 8: Box(2, 0, 16, 18); L(5, 5, 13, 5); L(5, 9, 13, 9); L(5, 13, 11, 13); break;
                case 10: Box(0, 0, 6, 5); L(3, 5, 3, 15); L(3, 9, 11, 9); L(3, 15, 11, 15); Box(11, 6, 18, 11); Box(11, 13, 18, 18); break;
                default: Circle(0, 0, 18, 18); L(9, 7, 9, 14); L(9, 3, 9, 4); break;
            }
        }
    }

    private static void Line(nint dc, int x, int y, int x2, int y2)
    {
        _ = MoveToEx(dc, x, y, 0); _ = LineTo(dc, x2, y2);
    }
    private static void RequestMouseLeave(nint window)
    {
        var request = new Win32TrackMouseEvent { Size = (uint)Marshal.SizeOf<Win32TrackMouseEvent>(), Flags = 2, Window = window };
        _ = TrackMouseEvent(ref request);
    }
}

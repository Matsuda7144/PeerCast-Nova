using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    internal static void RefreshTopStatus(nint listView)
    {
        if (StatesByListView.TryGetValue(listView, out var state)) state.RefreshTopStatus();
    }

    private sealed partial class AppShellState
    {
        private GenesisGuiNextTopStatusValues _topStatus = GenesisGuiNextTopStatusPresentation.Create(null, null);
        private bool _topStatusInitialized;
        private readonly GenesisGuiNextTrafficSampler _trafficSampler = new(TimeProvider.System);
        internal void RefreshTopStatus()
        {
            if (!IsModern || Volatile.Read(ref _detached) != 0) return;
            // Existing GUI-thread refreshes are serialized; identical captures do not repaint.
            NovaApplicationNetworkSnapshot? network = null;
            try { network = _viewModel.NetworkSettingsAdapter?.ReadSnapshot(); }
            catch { /* Do not retain a last successful Port value after a failed read. */ }
            PeerCast.Nova.Network.Transport.PcpTransportSnapshot? traffic = null;
            try { traffic = PeerCast.Nova.Network.Transport.PcpTransportStatistics.Shared.Capture(); }
            catch { /* A failed observation clears the sampling baseline. */ }
            var value = GenesisGuiNextTopStatusPresentation.Create(_viewModel.RelayActivation?.Snapshot, network, _trafficSampler.Sample(traffic));
            if (Volatile.Read(ref _detached) != 0 || _topStatusInitialized && value == _topStatus) return;
            _topStatusInitialized = true;
            _topStatus = value;
            _ = SetWindowTextW(TopStatusWindow, value.AccessibleText);
            _ = InvalidateRect(TopStatusWindow, 0, true);
        }

        private nint TopStatusWindow => _statusLabel;
        private nint[] TopVisualControls => [TopStatusWindow, _searchBox.LabelWindow, _searchBox.EditWindow, _searchBox.FilterWindow];
        internal bool IsTopFilter(nint window) => window == _searchBox.FilterWindow;
        internal bool IsTopInput(nint window) => IsModern && Volatile.Read(ref _detached) == 0 &&
            (window == _searchBox.EditWindow || IsTopFilter(window) || window == _relayTree.ChannelsComboWindow && window != 0);

        internal bool PaintTopStatus(nint control, uint message)
        {
            if (!IsModern || Volatile.Read(ref _detached) != 0 ||
                (control != TopStatusWindow && control != _searchBox.LabelWindow)) return false;
            if (message == 0x0014) return true;
            if (message != WindowMessagePaint) return false;
            var dc = BeginPaint(control, out var paint);
            try
            {
                if (dc == 0 || !GetClientRect(control, out var bounds)) return true;
                _ = FillRect(dc, ref bounds, _topBrush);
                if (control == _searchBox.LabelWindow)
                {
                    using var pen = new GenesisVisualPen(dc, Scale(1), GenesisGuiNextDesign.SecondaryText);
                    var x = Scale(3); var y = (bounds.Bottom - Scale(14)) / 2;
                    _ = Ellipse(dc, x, y, x + Scale(11), y + Scale(11));
                    Line(dc, x + Scale(10), y + Scale(10), x + Scale(15), y + Scale(15));
                    return true;
                }

                // Draw only the current projection; painting never reads runtimes or performs I/O.
                var width = LogicalWidth(bounds.Right);
                for (var i = 0; i < 5; i++)
                {
                    var cell = GenesisGuiNextTopBarLayout.StatusCell(width, i);
                    var box = bounds;
                    box.Left = Scale(cell.Left + 6); box.Right = Scale(cell.Left + cell.Width - 4);
                    var label = GenesisGuiNextTopStatusPresentation.Label(i, width < GenesisGuiNextTopBarLayout.FullStatusWidth);
                    using var font = GenesisWindowUiFont.SelectForDrawing(dc, control);
                    var measured = new Win32Rect();
                    _ = DrawTextW(dc, label, -1, ref measured, DrawTextSingleLine | DrawTextNoPrefix | 0x0400); // DT_CALCRECT
                    DrawCardLabel(dc, box, label, GenesisGuiNextDesign.SecondaryText, 0, control,
                        DrawTextSingleLine | DrawTextVerticalCenter);
                    box.Left += measured.Right + Scale(4);
                    var value = _topStatus.Value(i);
                    DrawCardLabel(dc, box, GenesisGuiNextTopStatusPresentation.Display(value, width < GenesisGuiNextTopBarLayout.FullStatusWidth),
                        value.Color, 0, control, DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
                    if (i == 4) continue;
                    using var divider = new GenesisVisualPen(dc, Scale(1), GenesisGuiNextDesign.Separator);
                    var x = Scale(cell.Left + cell.Width - 1);
                    Line(dc, x, Scale(7), x, bounds.Bottom - Scale(7));
                }
                return true;
            }
            finally { _ = EndPaint(control, ref paint); }
        }

        internal void PaintTopInputFrame(nint window)
        {
            if (!(IsTopInput(window) || _publishing.IsAdvancedCombo(window)) || !GetWindowRect(window, out var windowBounds)) return;
            var dc = GetWindowDC(window);
            if (dc == 0) return;
            try
            {
                var bounds = new Win32Rect { Right = windowBounds.Right - windowBounds.Left, Bottom = windowBounds.Bottom - windowBounds.Top };
                var focus = GetFocus();
                var focused = focus == window || (focus != 0 && GetParent(focus) == window);
                var color = !IsWindowEnabled(window) ? GenesisGuiNextDesign.Separator :
                    focused || _hoverControl == window ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border;
                using var pen = new GenesisVisualPen(dc, Scale(1), color);
                if (IsTopFilter(window) || _publishing.IsAdvancedCombo(window) || window == _relayTree.ChannelsComboWindow)
                {
                    // Cover only the native recessed edge, keeping the native
                    // field, popup, selection and keyboard behavior intact.
                    var edge = bounds;
                    edge.Bottom = Scale(3);
                    FillRectangle(dc, edge, GenesisGuiNextDesign.Input);
                    edge.Top = bounds.Bottom - Scale(3); edge.Bottom = bounds.Bottom;
                    FillRectangle(dc, edge, GenesisGuiNextDesign.Input);
                    edge = bounds; edge.Right = Scale(3);
                    FillRectangle(dc, edge, GenesisGuiNextDesign.Input);
                    var arrow = bounds;
                    arrow.Left = Math.Max(1, arrow.Right - Scale(23));
                    arrow.Top += Scale(1); arrow.Right -= Scale(1); arrow.Bottom -= Scale(1);
                    FillRectangle(dc, arrow, GenesisGuiNextDesign.Input);
                    var x = (arrow.Left + arrow.Right) / 2; var y = (arrow.Top + arrow.Bottom) / 2;
                    // Draw after the native paint and background fills, independently of the frame's state color.
                    using var arrowPen = new GenesisVisualPen(dc, Scale(1), GenesisGuiNextDesign.SecondaryText);
                    Line(dc, x - Scale(4), y - Scale(2), x, y + Scale(2));
                    Line(dc, x, y + Scale(2), x + Scale(4), y - Scale(2));
                }
                _ = GdiRectangle(dc, 0, 0, bounds.Right, bounds.Bottom);
            }
            finally { _ = ReleaseDC(window, dc); }
        }
    }
}

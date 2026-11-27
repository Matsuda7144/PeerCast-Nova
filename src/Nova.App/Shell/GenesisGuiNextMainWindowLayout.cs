using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private static readonly ConcurrentDictionary<nint, AppShellState> StatesByOwner = new();
    private const nuint PaneSplitterSubclassId = 0x5053504C;
    private static readonly Win32SubclassProcedure PaneSplitterProcedure = ProcessPaneSplitter;

    internal static void ResizeForOwner(nint owner)
    {
        if (StatesByOwner.TryGetValue(owner, out var state)) state.ApplyClientLayout();
    }

    internal static int? GetDetailsHeightForOwner(nint owner) =>
        StatesByOwner.TryGetValue(owner, out var state) ? state.DetailsHeight : null;

    private static nint ProcessPaneSplitter(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint owner)
    {
        if (message == WindowMessageNcDestroy)
        {
            _ = RemoveWindowSubclass(window, PaneSplitterProcedure, id);
        }

        if (StatesByOwner.TryGetValue((nint)owner, out var state) &&
            state.HandlePaneSplitterMessage(window, message, out var result))
        {
            return result;
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private sealed partial class AppShellState
    {
        private uint _layoutDpi = 96;
        private int? _detailsHeight;
        private nint _paneSplitter;
        private bool _splitterDragging, _splitterHover;
        private int _splitterDragStartScreenY, _splitterDragStartHeight;
        private (int Width, int Height, uint Dpi)? _clientLayout;
        internal int? DetailsHeight => _detailsHeight;
        internal bool LayoutInProgress { get; private set; }
        internal bool IsDetailsPane(nint window) => window == _detailsPane;
        private int Scale(int value) => GenesisMainWindowLayout.Scale(value, _layoutDpi);
        private int LogicalWidth(int value) => GenesisMainWindowLayout.Logical(value, _layoutDpi);

        internal void CreatePaneSplitter(nint instance)
        {
            _detailsHeight = GenesisMainWindowResizeGuard.TryGetSavedDetailsHeight(_owner);
            var parent = GetParent(_listView);
            if (parent == 0)
            {
                return;
            }

            _paneSplitter = CreateWindowExW(
                0,
                "STATIC",
                string.Empty,
                WindowStyleChild | WindowStyleVisible | 0x0100u | WindowStyleClipSiblings,
                0,
                0,
                1,
                1,
                parent,
                0,
                instance,
                0);
            if (_paneSplitter != 0)
            {
                _ = SetWindowSubclass(_paneSplitter, PaneSplitterProcedure, PaneSplitterSubclassId, (nuint)_owner);
                _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.SetNameAndDescription(
                    _paneSplitter,
                    "一覧と詳細の境界スプリッター",
                    help: "上下にドラッグして一覧と詳細ペインの高さを調整します");
            }
        }

        internal void DestroyPaneSplitter()
        {
            if (_paneSplitter != 0)
            {
                _ = PeerCast.Nova.Windows.Win32AccessibilityAnnotation.Clear(_paneSplitter);
                if (IsWindow(_paneSplitter))
                {
                    _ = RemoveWindowSubclass(_paneSplitter, PaneSplitterProcedure, PaneSplitterSubclassId);
                }

                DestroyWindowIfPresent(_paneSplitter);
                _paneSplitter = 0;
            }
        }

        private void UpdatePaneSplitterVisibility()
        {
            if (_paneSplitter != 0 && IsWindow(_paneSplitter))
            {
                _ = ShowWindow(_paneSplitter, !_settings.IsOpen && !_publishing.UsesPanel && !_relayTree.IsPageShown ? ShowWindowShowNoActivate : ShowWindowHide);
            }
        }

        internal bool HandlePaneSplitterMessage(nint window, uint message, out nint result)
        {
            result = 0;
            switch (message)
            {
                case 0x0020: // WM_SETCURSOR
                    _ = SetCursor(LoadCursorW(0, (nint)32645)); // IDC_SIZENS
                    result = 1;
                    return true;
                case 0x0014: // WM_ERASEBKGND
                    result = 1;
                    return true;
                case WindowMessagePaint:
                    PaintPaneSplitter(window);
                    return true;
                case 0x0201: // WM_LBUTTONDOWN
                    if (GetCursorPos(out var downPoint) && GetClientRect(_detailsPane, out var currentDetails))
                    {
                        _splitterDragging = true;
                        _splitterDragStartScreenY = downPoint.Y;
                        _splitterDragStartHeight = Math.Max(
                            GenesisGuiNextAppShellPlacement.MinimumDetailsHeight,
                            LogicalWidth(currentDetails.Bottom));
                        _ = SetCapture(window);
                        _ = InvalidateRect(window, 0, false);
                    }
                    return true;
                case 0x0200: // WM_MOUSEMOVE
                    if (_splitterDragging && GetCursorPos(out var movePoint))
                    {
                        var deltaLogical = LogicalWidth(movePoint.Y - _splitterDragStartScreenY);
                        _detailsHeight = Math.Max(
                            GenesisGuiNextAppShellPlacement.MinimumDetailsHeight,
                            _splitterDragStartHeight - deltaLogical);
                        ApplyClientLayout(force: true);
                        _ = RedrawWindow(_owner, 0, 0, 0x0100); // ドラッグ中の再描画を即時反映し残像を防ぐ
                    }
                    else if (!_splitterHover)
                    {
                        _splitterHover = true;
                        var track = new Win32TrackMouseEvent
                        {
                            Size = (uint)Marshal.SizeOf<Win32TrackMouseEvent>(),
                            Flags = 0x00000002u,
                            Window = window,
                        };
                        _ = TrackMouseEvent(ref track);
                        _ = InvalidateRect(window, 0, false);
                    }
                    return true;
                case 0x02A3: // WM_MOUSELEAVE
                    _splitterHover = false;
                    _ = InvalidateRect(window, 0, false);
                    return true;
                case 0x0202: // WM_LBUTTONUP
                case 0x0215: // WM_CAPTURECHANGED
                    if (_splitterDragging)
                    {
                        _splitterDragging = false;
                        if (message == 0x0202)
                        {
                            _ = ReleaseCapture();
                        }

                        if (GetClientRect(_detailsPane, out var settledDetails))
                        {
                            _detailsHeight = Math.Max(
                                GenesisGuiNextAppShellPlacement.MinimumDetailsHeight,
                                LogicalWidth(settledDetails.Bottom));
                            GenesisMainWindowResizeGuard.SaveDetailsHeight(_owner, _detailsHeight.Value);
                        }

                        _ = InvalidateRect(window, 0, false);
                    }
                    return true;
                case 0x0203: // WM_LBUTTONDBLCLK
                    _detailsHeight = GenesisGuiNextAppShellPlacement.DetailsHeight;
                    ApplyClientLayout(force: true);
                    if (GetClientRect(_detailsPane, out var resetDetails))
                    {
                        _detailsHeight = Math.Max(
                            GenesisGuiNextAppShellPlacement.MinimumDetailsHeight,
                            LogicalWidth(resetDetails.Bottom));
                    }

                    GenesisMainWindowResizeGuard.SaveDetailsHeight(_owner, _detailsHeight.Value);
                    return true;
                default:
                    return false;
            }
        }

        private void PaintPaneSplitter(nint window)
        {
            var dc = BeginPaint(window, out var paint);
            if (dc == 0)
            {
                return;
            }

            try
            {
                if (!GetClientRect(window, out var area))
                {
                    return;
                }

                var background = IsModern ? GenesisGuiNextDesign.Background : (uint)BackgroundColor;
                var bgBrush = CreateSolidBrush(background);
                if (bgBrush != 0)
                {
                    try { _ = FillRect(dc, ref area, bgBrush); }
                    finally { _ = DeleteObject(bgBrush); }
                }

                var gripWidth = Math.Min(Scale(48), Math.Max(8, area.Right - area.Left - Scale(16)));
                var gripHeight = Math.Max(2, Scale(2));
                var gripLeft = area.Left + (area.Right - area.Left - gripWidth) / 2;
                var gripTop = area.Top + (area.Bottom - area.Top - gripHeight) / 2;
                var gripRect = new Win32Rect
                {
                    Left = gripLeft,
                    Top = gripTop,
                    Right = gripLeft + gripWidth,
                    Bottom = gripTop + gripHeight,
                };
                var gripColor = _splitterDragging || _splitterHover
                    ? (IsModern ? GenesisGuiNextDesign.Accent : 0x00A0A0A0u)
                    : (IsModern ? GenesisGuiNextDesign.Border : 0x00505050u);
                var gripBrush = CreateSolidBrush(gripColor);
                if (gripBrush != 0)
                {
                    try { _ = FillRect(dc, ref gripRect, gripBrush); }
                    finally { _ = DeleteObject(gripBrush); }
                }
            }
            finally
            {
                _ = EndPaint(window, ref paint);
            }
        }

        private void PlaceFooter()
        {
            if (!GetClientRect(_owner, out var client)) return;
            var footer = SystemGenesisMainWindowInteractionBridgeFactory.GetStatusWindow(GetParent(_listView));
            var top = LogicalWidth(client.Bottom) - 28;
            var width = LogicalWidth(client.Right) - GenesisMainWindowLayout.ContentLeft - 12;
            // These control the optional internal playback window, not external-player playback.
            // Keep the existing bridge available, but do not reserve footer space in Modern.
            var showModes = !IsModern;
            if (!GenesisPlaybackModeButtonBridge.TryResize(_owner, footer,
                GenesisMainWindowLayout.ContentLeft, top, width, 24, _layoutDpi, showModes))
                _ = MoveWindow(footer, Scale(GenesisMainWindowLayout.ContentLeft), Scale(top), Scale(width), Scale(24), true);
        }

        internal void ApplyClientLayout(bool force = false)
        {
            if (LayoutInProgress || Volatile.Read(ref _detached) != 0 ||
                !GetClientRect(_owner, out var client)) return;
            var dpi = GenesisMainWindowResizeGuard.GetDpi(_owner);
            if (!force && _clientLayout == (client.Right, client.Bottom, dpi)) return;
            if (!GenesisMainWindowLayout.TryResolve(client.Right, client.Bottom, dpi,
                out var sidebar, out var status, out var list, out var details, _detailsHeight) ||
                !TryResolveControlLayout(status.Width, out var statusWidth, out var searchLeft,
                    out var searchWidth, out var refreshLeft, out var favoriteLeft, IsModern) ||
                !TryResolvePlaybackButtonLayout(sidebar.Width, sidebar.Height, out var playLeft,
                    out var stopLeft, out var playbackTop, out var playbackWidth, out var playbackHeight)) return;
            LayoutInProgress = true;
            try
            {
                _layoutDpi = dpi;
                UpdateVisualDpi(dpi);
                // The interaction bridge reparents channel controls into a notification
                // container. Grow that retained parent before positioning its children.
                var parent = GetParent(_listView);
                if (parent != _owner)
                    _ = MoveWindow(parent, 0, 0, client.Right, client.Bottom, true);
                void Place(nint window, int x, int y, int width, int height) =>
                    _ = MoveWindow(window, Scale(x), Scale(y), Scale(width), Scale(height), true);
                void Pane(nint window, GenesisGuiNextAppShellPlacement.WindowRectangle bounds) =>
                    Place(window, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
                Pane(_sidebar, sidebar);
                Pane(_statusBar, status);
                Pane(_listView, list);
                Pane(_detailsPane, GenesisMainWindowLayout.ResolveDetailsPaneBounds(_publishing.UsesPanel, list, details));
                if (_paneSplitter != 0)
                {
                    Pane(_paneSplitter, new(list.Left, list.Bottom, list.Right, details.Top));
                    UpdatePaneSplitterVisibility();
                }
                _publishing.ResetPanePlacement();
                if (_settings.PaneWindow != 0)
                {
                    Pane(_settings.PaneWindow, new(list.Left, list.Top, list.Right, details.Bottom));
                    _settings.Place(list.Width, details.Bottom - list.Top);
                }
                for (var i = 0; i < _navigationButtons.Length; i++)
                    Place(_navigationButtons[i], 0, NavigationItemTop + GenesisGuiNextNavigationPresentation.Row(i, IsModern) * GenesisGuiNextNavigationPresentation.RowHeight(IsModern),
                        sidebar.Width, GenesisGuiNextNavigationPresentation.RowHeight(IsModern) - 2);
                Place(_playButton, playLeft, playbackTop, playbackWidth, playbackHeight);
                Place(_stopButton, stopLeft, playbackTop, playbackWidth, playbackHeight);
                Place(_statusLabel, 8, 0, statusWidth, status.Height);
                _searchBox.ApplyLayout(searchLeft, searchWidth, status.Height, dpi);
                var topControlHeight = IsModern ? GenesisGuiNextTopBarLayout.Height : status.Height - 4;
                Place(_refreshButton, refreshLeft, 2, RefreshButtonWidth, topControlHeight);
                Place(_favoriteButton, favoriteLeft, 2, IsModern ? GenesisGuiNextTopBarLayout.FavoriteWidth : FavoriteButtonWidth, topControlHeight);

                var actions = ResolveDetailsActions(details.Width);
                _detailsTextWidth = actions.TextWidth;
                Place(_detailsStopRecordingButton, actions.StopLeft, 8, DetailsStopRecordingButtonWidth, DetailsCopyButtonHeight);
                Place(_detailsCopyButton, actions.CopyLeft, 8, DetailsCopyButtonWidth, DetailsCopyButtonHeight);
                // Recording-page toolbar positions stay fixed in logical coordinates.
                var modeButtons = new[] { _activeRecordingsModeButton, _recordingLibraryModeButton,
                    _recordingRulesModeButton, _recordingHistoryModeButton, _recordingLibraryRefreshButton,
                    _recordingHistoryRefreshButton, _createRuleButton, _editRuleButton, _toggleRuleButton,
                    _deleteRuleButton, _moveRuleUpButton, _moveRuleDownButton };
                var slots = new[] { 0, 1, 2, 3, 4, 4, 4, 5, 6, 7, 8, 9 };
                for (var i = 0; i < modeButtons.Length; i++)
                    Place(modeButtons[i], RecordingToolbarLeft(slots[i]), 8, RecordingModeButtonWidth, 26);
                Place(_relayStart, 12, RelayControlsTop, 84, 26);
                Place(_relayStop, 102, RelayControlsTop, 84, 26);

                PlaceFooter();
                _detailsUpdates.Reset();
                SynchronizeNativeSelection();
                _clientLayout = (client.Right, client.Bottom, dpi);
                // 新規露出領域と子コントロール・枠の再描画をまとめる。
                _ = RedrawWindow(_owner, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0400);
            }
            finally { LayoutInProgress = false; }
        }
    }

    private static int RecordingToolbarLeft(int slot) => 12 + slot * (RecordingModeButtonWidth + RecordingModeButtonGap);

    internal static (int RecordLeft, int StopLeft, int CopyLeft, int TextWidth) ResolveDetailsActions(int width)
    {
        var copy = Math.Max(0, width - DetailsCopyButtonWidth - 12);
        var stop = Math.Max(0, copy - DetailsStopRecordingButtonWidth - DetailsRecordingButtonGap);
        var record = Math.Max(0, stop - DetailsRecordButtonWidth - DetailsRecordingButtonGap);
        return (record, stop, copy, Math.Max(1, record - 20));
    }
}

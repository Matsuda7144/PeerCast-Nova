using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private static readonly ConcurrentDictionary<nint, AppShellState> StatesByOwner = new();

    internal static void ResizeForOwner(nint owner)
    {
        if (StatesByOwner.TryGetValue(owner, out var state)) state.ApplyClientLayout();
    }

    private sealed partial class AppShellState
    {
        private uint _layoutDpi = 96;
        private (int Width, int Height, uint Dpi)? _clientLayout;
        internal bool LayoutInProgress { get; private set; }
        internal bool IsDetailsPane(nint window) => window == _detailsPane;
        private int Scale(int value) => GenesisMainWindowLayout.Scale(value, _layoutDpi);
        private int LogicalWidth(int value) => GenesisMainWindowLayout.Logical(value, _layoutDpi);

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

        internal void ApplyClientLayout()
        {
            if (LayoutInProgress || Volatile.Read(ref _detached) != 0 ||
                !GetClientRect(_owner, out var client)) return;
            var dpi = GenesisMainWindowResizeGuard.GetDpi(_owner);
            if (_clientLayout == (client.Right, client.Bottom, dpi)) return;
            if (!GenesisMainWindowLayout.TryResolve(client.Right, client.Bottom, dpi,
                out var sidebar, out var status, out var list, out var details) ||
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
                Pane(_detailsPane, details);
                _publishingPanePlacement = null;
                if (_settingsPane != 0)
                {
                    Pane(_settingsPane, new(list.Left, list.Top, list.Right, details.Bottom));
                    PlaceIntegratedSettings(list.Width, details.Bottom - list.Top);
                }
                for (var i = 0; i < _navigationButtons.Length; i++)
                    Place(_navigationButtons[i], 0, NavigationItemTop + i * NavigationItemHeight,
                        sidebar.Width, NavigationItemHeight - 2);
                Place(_playButton, playLeft, playbackTop, playbackWidth, playbackHeight);
                Place(_stopButton, stopLeft, playbackTop, playbackWidth, playbackHeight);
                var statusLabel = FindWindowExW(_statusBar, 0, "STATIC", null);
                Place(statusLabel, 8, 0, statusWidth, status.Height);
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
                // Coalesce repaint for the newly exposed client area and retained children.
                _ = RedrawWindow(_owner, 0, 0, 0x0001 | 0x0004 | 0x0080);
            }
            finally { LayoutInProgress = false; }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);

    private static int RecordingToolbarLeft(int slot) => 12 + slot * (RecordingModeButtonWidth + RecordingModeButtonGap);

    internal static (int RecordLeft, int StopLeft, int CopyLeft, int TextWidth) ResolveDetailsActions(int width)
    {
        var copy = Math.Max(0, width - DetailsCopyButtonWidth - 12);
        var stop = Math.Max(0, copy - DetailsStopRecordingButtonWidth - DetailsRecordingButtonGap);
        var record = Math.Max(0, stop - DetailsRecordButtonWidth - DetailsRecordingButtonGap);
        return (record, stop, copy, Math.Max(1, record - 20));
    }
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed class GenesisChannelDirectContextMenuPlacement
{
    private readonly nint _listView;

    private GenesisChannelDirectContextMenuPlacement(nint listView)
    {
        _listView = listView;
    }

    public static GenesisChannelDirectContextMenuPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var listView = FindWindowExW(
            (nint)windowToken,
            0,
            "SysListView32",
            null);
        return listView == 0 ? null : new(listView);
    }

    public IAsyncDisposable? Open(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(channelCatalogSession);
        return GenesisChannelDirectContextMenuBridge.TryCreate(
            _listView,
            viewModel,
            channelCatalogSession);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);
}

internal static class GenesisChannelDirectContextMenuDispatch
{
    public static bool TryOpen(
        GenesisGuiNextNativeTabRow? row,
        Func<GenesisGuiNextNativeTabRow, bool> synchronizeSelection,
        Action<GenesisGuiNextNativeTabRow> openMenu)
    {
        ArgumentNullException.ThrowIfNull(synchronizeSelection);
        ArgumentNullException.ThrowIfNull(openMenu);
        if (row is null || row.RecordingRule is not null || row.RecordingLibrary is not null || row.RecordingHistory is not null || row.Log is not null || row.Publishing is not null)
        {
            return false;
        }

        // Selection synchronization is best-effort presentation. The rendered-row
        // identity is already authoritative for the command target, so a native or
        // managed selection failure must not make the context menu disappear.
        _ = synchronizeSelection(row);
        openMenu(row);
        return true;
    }
}

internal static class GenesisChannelDirectContextMenuRowResolver
{
    public static GenesisGuiNextNativeTabRow? Resolve(
        int rowIndex,
        GenesisGuiNextNativeTabRow? registeredRow,
        GenesisMainViewModel viewModel,
        GenesisGuiNextTabsViewModel? guiNextTabs)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (rowIndex < 0)
        {
            return null;
        }

        if (guiNextTabs?.SelectedTab == GenesisGuiNextTab.Recording)
        {
            return null;
        }

        if (registeredRow is not null)
        {
            return registeredRow;
        }

        IReadOnlyList<GenesisGuiNextNativeTabRow> currentRows;
        if (guiNextTabs is null)
        {
            currentRows = GenesisGuiNextNativeTabProjection.CreateLiveChannels(
                viewModel.Channels);
        }
        else if (guiNextTabs.SelectedTab == GenesisGuiNextTab.Channels)
        {
            currentRows = SystemGenesisMainWindowInteractionBridgeFactory
                .CreateChannelsRowsForNativeList(viewModel.Channels, guiNextTabs);
        }
        else
        {
            currentRows = GenesisGuiNextNativeTabProjection.Create(
                guiNextTabs,
                guiNextTabs.SelectedTab);
        }

        return rowIndex < currentRows.Count
            ? currentRows[rowIndex]
            : null;
    }
}

internal enum GenesisChannelDirectKeyboardCommand
{
    None,
    ContextMenu,
    CopySource,
    Play,
    Refresh,
    Stop,
}

internal static class GenesisChannelDirectKeyboard
{
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyC = 0x43;
    private const ushort VirtualKeyF5 = 0x74;

    public static GenesisChannelDirectKeyboardCommand Resolve(
        ushort virtualKey,
        bool shiftPressed,
        bool controlPressed)
    {
        if (GenesisChannelContextMenuKeyboard.IsOpenGesture(
                virtualKey,
                shiftPressed))
        {
            return GenesisChannelDirectKeyboardCommand.ContextMenu;
        }

        if (virtualKey == VirtualKeyC && controlPressed)
        {
            return GenesisChannelDirectKeyboardCommand.CopySource;
        }

        return virtualKey switch
        {
            VirtualKeyEnter => GenesisChannelDirectKeyboardCommand.Play,
            VirtualKeyF5 => GenesisChannelDirectKeyboardCommand.Refresh,
            VirtualKeyEscape => GenesisChannelDirectKeyboardCommand.Stop,
            _ => GenesisChannelDirectKeyboardCommand.None,
        };
    }
}

internal static class GenesisChannelDirectContextMenuMessage
{
    public static bool IsKeyboardInvocation(nint lParam) =>
        lParam == (nint)(-1);
}

internal static class GenesisChannelNativeHeaderSort
{
    private const int HeaderNotifyItemClickAnsi = -302;
    private const int HeaderNotifyItemClickUnicode = -322;

    public static bool IsColumnClick(uint code) =>
        unchecked((int)code) is
            HeaderNotifyItemClickAnsi or
            HeaderNotifyItemClickUnicode;
}

internal sealed class GenesisChannelDirectContextMenuBridge : IAsyncDisposable
{
    private const uint WindowMessageNotify = 0x004E;
    private const uint WindowMessageContextMenu = 0x007B;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint GetAncestorRoot = 2;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyControl = 0x11;
    private const nuint SubclassIdentifier = 0x7144230;
    private static readonly ConcurrentDictionary<nint, ContextState> StatesByListView = new();
    private static readonly SubclassProcedure SubclassProcedureDelegate = ProcessListViewMessage;
    private readonly nint _listView;
    private int _disposed;

    private GenesisChannelDirectContextMenuBridge(nint listView)
    {
        _listView = listView;
    }

    public static GenesisChannelDirectContextMenuBridge? TryCreate(
        nint listView,
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(channelCatalogSession);
        if (listView == 0 || !IsWindow(listView))
        {
            return null;
        }

        var state = new ContextState(listView, viewModel, channelCatalogSession);
        if (!StatesByListView.TryAdd(listView, state))
        {
            return null;
        }

        if (!SetWindowSubclass(
                listView,
                SubclassProcedureDelegate,
                SubclassIdentifier,
                0))
        {
            _ = StatesByListView.TryRemove(listView, out _);
            return null;
        }

        return new(listView);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _ = StatesByListView.TryRemove(_listView, out _);
            if (_listView != 0 && IsWindow(_listView))
            {
                _ = RemoveWindowSubclass(
                    _listView,
                    SubclassProcedureDelegate,
                    SubclassIdentifier);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static nint ProcessListViewMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        if (message == WindowMessageNcDestroy)
        {
            _ = StatesByListView.TryRemove(window, out _);
            _ = RemoveWindowSubclass(
                window,
                SubclassProcedureDelegate,
                SubclassIdentifier);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        if (StatesByListView.TryGetValue(window, out var state))
        {
            if (message == WindowMessageNotify &&
                state.ProcessHeaderNotify(lParam))
            {
                return 0;
            }

            if (message == WindowMessageKeyDown &&
                state.ProcessKey((ushort)wParam))
            {
                return 0;
            }

            if (message == WindowMessageContextMenu)
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "direct-context-menu",
                    GenesisChannelDirectContextMenuMessage.IsKeyboardInvocation(lParam)
                        ? "keyboard"
                        : "pointer");
                state.ProcessContextMenu(lParam);
                return 0;
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private sealed class ContextState
    {
        private readonly nint _listView;
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly GenesisGuiNextTabsViewModel? _guiNextTabs;
        private readonly GenesisInteractionCommandLanes _commandLanes = new();

        public ContextState(
            nint listView,
            GenesisMainViewModel viewModel,
            GenesisChannelCatalogSession channelCatalogSession)
        {
            _listView = listView;
            _viewModel = viewModel;
            _interaction = new(viewModel, channelCatalogSession);
            _guiNextTabs = channelCatalogSession.GuiNextTabs;
        }

        public bool ProcessHeaderNotify(nint lParam)
        {
            if (lParam == 0)
            {
                return false;
            }

            var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
            if (!GenesisChannelNativeHeaderSort.IsColumnClick(header.Code))
            {
                return false;
            }

            var notification = Marshal.PtrToStructure<HeaderNotify>(lParam);
            if (_guiNextTabs is null ||
                _guiNextTabs.SelectedTab == GenesisGuiNextTab.Channels)
            {
                _ = _interaction.SortChannelColumn(notification.Item);
            }

            return true;
        }

        public bool ProcessKey(ushort virtualKey)
        {
            var command = GenesisChannelDirectKeyboard.Resolve(
                virtualKey,
                IsShiftPressed(),
                IsControlPressed());
            switch (command)
            {
                case GenesisChannelDirectKeyboardCommand.ContextMenu:
                    ShowSelectedContextMenu();
                    return true;
                case GenesisChannelDirectKeyboardCommand.CopySource:
                    CopySelectedSource();
                    return true;
                case GenesisChannelDirectKeyboardCommand.Play:
                    PlaySelectedChannel();
                    return true;
                case GenesisChannelDirectKeyboardCommand.Refresh:
                    if (_commandLanes.CanStartRefresh &&
                        _interaction.CanRefreshCurrentView)
                    {
                        _commandLanes.TrackRefresh(ObserveCommandAsync(
                            _interaction.RefreshCurrentViewAsync()));
                    }

                    return true;
                case GenesisChannelDirectKeyboardCommand.Stop:
                    if (_commandLanes.CanStartStop)
                    {
                        _commandLanes.TrackStop(ObserveCommandAsync(
                            _interaction.StopAsync()));
                    }

                    return true;
                default:
                    return false;
            }
        }

        public void ProcessContextMenu(nint lParam)
        {
            if (GenesisChannelDirectContextMenuMessage.IsKeyboardInvocation(lParam))
            {
                ShowSelectedContextMenu();
                return;
            }

            ProcessPointerContextMenu();
        }

        private void ProcessPointerContextMenu()
        {
            var rowIndex = GenesisChannelContextMenuNative.GetRowAtCursor(_listView);
            var registeredRow = GenesisChannelListRenderedChannels.FindRow(
                _listView,
                rowIndex);
            if (registeredRow?.RecordingRule is not null || registeredRow?.RecordingLibrary is not null || registeredRow?.RecordingHistory is not null || registeredRow?.Publishing is not null)
            {
                return;
            }

            var row = GenesisChannelDirectContextMenuRowResolver.Resolve(
                rowIndex,
                registeredRow,
                _viewModel,
                _guiNextTabs);
            _ = GenesisChannelDirectContextMenuDispatch.TryOpen(
                row,
                candidate => SynchronizeSelection(rowIndex, candidate),
                OpenMenu);
        }

        private void ShowSelectedContextMenu()
        {
            var target = ResolveKeyboardTarget();
            if (target is null)
            {
                return;
            }

            var resolvedTarget = target.Value;
            var row = resolvedTarget.RenderedRow;
            if (_interaction.IsRecordingHistoryView || row.RecordingRule is not null || row.RecordingLibrary is not null || row.RecordingHistory is not null || row.Log is not null || row.Publishing is not null)
            {
                return;
            }

            var owner = ResolveOwner();
            _ = SetForegroundWindow(owner);
            var actions = GenesisChannelContextActions.Capture(_viewModel, row);
            var command = GenesisChannelContextMenuNative.ShowResolvedRow(
                owner,
                _listView,
                resolvedTarget.Row,
                row.IsInteractive,
                GenesisChannelContextMenuFavoritePresentation.Resolve(
                    _guiNextTabs,
                    row),
                actions);
            ProcessCommand(command, row, owner, actions);
        }

        private void CopySelectedSource()
        {
            if (_interaction.IsRecordingHistoryView) return;
            var source = ResolveKeyboardTarget()?.RenderedRow.DisplayChannel.Source;
            if (!string.IsNullOrWhiteSpace(source))
            {
                _ = GenesisChannelContextMenuNative.CopySourceToClipboard(
                    ResolveOwner(),
                    source);
            }
        }

        private void PlaySelectedChannel()
        {
            if (!_commandLanes.CanStartPlayback)
            {
                return;
            }

            var interactiveChannel = ResolveKeyboardTarget()?.RenderedRow.InteractiveChannel;
            if (interactiveChannel is not null)
            {
                _commandLanes.TrackPlayback(ObserveActivationAsync(
                    _interaction.ActivateChannelAsync(interactiveChannel)));
            }
        }

        private GenesisChannelContextMenuRenderedKeyboardTarget? ResolveKeyboardTarget() =>
            GenesisChannelContextMenuKeyboard.ResolveRenderedTarget(
                _listView,
                GenesisChannelContextMenuNative.ResolveKeyboardMenuRow,
                GenesisChannelListRenderedChannels.FindRow);

        private bool SynchronizeSelection(
            int rowIndex,
            GenesisGuiNextNativeTabRow row) =>
            GenesisChannelListNativeSelection.SelectRowWithoutEnsureVisible(
                _listView,
                rowIndex,
                _ => SynchronizeManagedSelection(row));

        private bool SynchronizeManagedSelection(GenesisGuiNextNativeTabRow row)
        {
            if (row.InteractiveChannel is { } interactiveChannel)
            {
                return _interaction.SelectChannel(interactiveChannel);
            }

            _viewModel.SelectedChannel = null;
            return true;
        }

        private void OpenMenu(GenesisGuiNextNativeTabRow row)
        {
            if (_interaction.IsRecordingHistoryView || row.RecordingRule is not null || row.RecordingLibrary is not null || row.RecordingHistory is not null || row.Log is not null || row.Publishing is not null)
            {
                return;
            }

            var owner = ResolveOwner();
            _ = SetForegroundWindow(owner);
            var actions = GenesisChannelContextActions.Capture(_viewModel, row);
            var command = GenesisChannelContextMenuNative.Show(
                owner,
                row.IsInteractive,
                GenesisChannelContextMenuFavoritePresentation.Resolve(
                    _guiNextTabs,
                    row),
                actions);
            ProcessCommand(command, row, owner, actions);
        }

        private nint ResolveOwner()
        {
            var owner = GetAncestor(_listView, GetAncestorRoot);
            return owner == 0 ? _listView : owner;
        }

        private void ProcessCommand(
            GenesisChannelContextMenuCommand command,
            GenesisGuiNextNativeTabRow row,
            nint owner,
            GenesisChannelContextTarget actions)
        {
            if (_interaction.IsRecordingHistoryView || row.RecordingRule is not null || row.RecordingLibrary is not null || row.RecordingHistory is not null || row.Log is not null || row.Publishing is not null)
            {
                return;
            }

            if (GenesisChannelContextActions.IsAction(command))
            {
                _ = GenesisChannelContextActions.ExecuteAsync(
                    _viewModel, actions, command, new SystemGenesisChannelContextEffects(owner));
            }
            else if (command == GenesisChannelContextMenuCommand.Play &&
                row.InteractiveChannel is { } interactiveChannel &&
                _commandLanes.CanStartPlayback)
            {
                _commandLanes.TrackPlayback(ObserveCommandAsync(
                    _interaction.PlaySourceAsync(interactiveChannel.Source)));
            }
            else if (command == GenesisChannelContextMenuCommand.ManualSourceUrl)
            {
                _ = GenesisSourceUrlPlaybackDialog.TryShow(
                    owner,
                    _viewModel,
                    _interaction);
            }
            else if (command == GenesisChannelContextMenuCommand.CopySource)
            {
                var source = row.DisplayChannel.Source;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    _ = GenesisChannelContextMenuNative.CopySourceToClipboard(
                        owner,
                        source);
                }
            }
            else if (command == GenesisChannelContextMenuCommand.Favorite &&
                _guiNextTabs is not null)
            {
                _ = GenesisGuiNextFavoriteToggle.Apply(_guiNextTabs, row);
            }
            else if (command == GenesisChannelContextMenuCommand.Refresh &&
                _commandLanes.CanStartRefresh &&
                _interaction.CanRefreshCurrentView)
            {
                _commandLanes.TrackRefresh(ObserveCommandAsync(
                    _interaction.RefreshCurrentViewAsync()));
            }
        }

        private static bool IsShiftPressed() =>
            (GetKeyState(VirtualKeyShift) & 0x8000) != 0;

        private static bool IsControlPressed() =>
            (GetKeyState(VirtualKeyControl) & 0x8000) != 0;

        private static async Task ObserveCommandAsync(ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch
            {
                // GUI commands convert expected failures to bounded state.
            }
        }

        private static async Task ObserveActivationAsync(ValueTask<bool> activation)
        {
            try
            {
                _ = await activation.ConfigureAwait(false);
            }
            catch
            {
                // Playback activation converts failures to bounded GUI state.
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyHeader
    {
        public nint WindowFrom;
        public nuint IdFrom;
        public uint Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HeaderNotify
    {
        public NotifyHeader Header;
        public int Item;
        public int Button;
        public nint ItemData;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint window,
        SubclassProcedure subclassProcedure,
        nuint subclassIdentifier,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint window,
        SubclassProcedure subclassProcedure,
        nuint subclassIdentifier);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed class GenesisChannelContextMenuPlacement
{
    private readonly nint _listView;

    private GenesisChannelContextMenuPlacement(nint listView)
    {
        _listView = listView;
    }

    public static GenesisChannelContextMenuPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var listView = GenesisMainWindowControlRegistry.ResolveChannelList((nint)windowToken);
        return listView == 0 ? null : new(listView);
    }

    public IAsyncDisposable? Open(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(channelCatalogSession);
        return GenesisChannelContextMenuBridge.TryCreate(
            _listView,
            viewModel,
            channelCatalogSession);
    }

}

internal static class GenesisChannelContextMenuKeyboard
{
    private const ushort VirtualKeyApplication = 0x5D;
    private const ushort VirtualKeyF10 = 0x79;
    private const int NoFallbackRow = int.MaxValue;

    public static bool IsOpenGesture(ushort virtualKey, bool shiftPressed) =>
        virtualKey == VirtualKeyApplication ||
        (virtualKey == VirtualKeyF10 && shiftPressed);

    public static GenesisChannelContextMenuKeyboardTarget? ResolveTarget(
        nint listView,
        int requestedRow,
        Func<nint, int, int> resolveRow,
        Func<nint, int, GenesisChannelItem?> findRenderedChannel)
    {
        ArgumentNullException.ThrowIfNull(resolveRow);
        ArgumentNullException.ThrowIfNull(findRenderedChannel);
        var row = resolveRow(listView, requestedRow);
        if (row < 0)
        {
            return null;
        }

        var channel = findRenderedChannel(listView, row);
        return channel is null
            ? null
            : new GenesisChannelContextMenuKeyboardTarget(row, channel);
    }

    public static GenesisChannelContextMenuRenderedKeyboardTarget? ResolveRenderedTarget(
        nint listView,
        Func<nint, int, int> resolveRow,
        Func<nint, int, GenesisGuiNextNativeTabRow?> findRenderedRow)
    {
        ArgumentNullException.ThrowIfNull(resolveRow);
        ArgumentNullException.ThrowIfNull(findRenderedRow);
        var row = resolveRow(listView, NoFallbackRow);
        if (row < 0 || row == NoFallbackRow)
        {
            return null;
        }

        var renderedRow = findRenderedRow(listView, row);
        return renderedRow is null || renderedRow.RecordingLibrary is not null || renderedRow.RecordingHistory is not null || renderedRow.Publishing is not null
            ? null
            : new GenesisChannelContextMenuRenderedKeyboardTarget(row, renderedRow);
    }
}

internal readonly record struct GenesisChannelContextMenuKeyboardTarget(
    int Row,
    GenesisChannelItem Channel);

internal readonly record struct GenesisChannelContextMenuRenderedKeyboardTarget(
    int Row,
    GenesisGuiNextNativeTabRow RenderedRow);

internal static class GenesisChannelKeyboardSelection
{
    public static GenesisChannelContextMenuKeyboardTarget? ResolveTarget(
        nint listView,
        GenesisChannelItem? selectedChannel,
        IReadOnlyList<GenesisChannelItem> channels,
        Func<nint, int, int> resolveRow,
        Func<nint, int, GenesisChannelItem?> findRenderedChannel)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(resolveRow);
        ArgumentNullException.ThrowIfNull(findRenderedChannel);
        if (selectedChannel is null)
        {
            return null;
        }

        var requestedRow = -1;
        for (var index = 0; index < channels.Count; index++)
        {
            if (string.Equals(
                    channels[index].Source,
                    selectedChannel.Source,
                    StringComparison.Ordinal))
            {
                requestedRow = index;
                break;
            }
        }

        return requestedRow < 0
            ? null
            : GenesisChannelContextMenuKeyboard.ResolveTarget(
                listView,
                requestedRow,
                resolveRow,
                findRenderedChannel);
    }
}

internal static class GenesisChannelContextMenuPointer
{
    public static bool TryOpenAfterSelection(
        bool selectionSynchronized,
        Action openMenu)
    {
        ArgumentNullException.ThrowIfNull(openMenu);

        // The rendered row is already the authoritative command target. Native and
        // managed selection synchronization is presentation-only, so a failed or
        // redundant selection update must not consume the first right click.
        _ = selectionSynchronized;
        openMenu();
        return true;
    }
}

internal sealed class GenesisChannelContextMenuBridge : IAsyncDisposable
{
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowMessageNotify = 0x004E;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint GetAncestorRoot = 2;
    private const int NotifyRightClick = -5;
    private const int ListViewNotifyKeyDown = -155;
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyC = 0x43;
    private const ushort VirtualKeyF5 = 0x74;
    private static readonly ConcurrentDictionary<nint, ContextState> StatesByContainer = new();
    private static readonly Win32WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private readonly nint _listView;
    private readonly nint _notificationTarget;
    private readonly WindowRectangle _originalRectangle;
    private readonly nint _container;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisChannelContextMenuBridge(
        nint listView,
        nint notificationTarget,
        WindowRectangle originalRectangle,
        nint container,
        string className,
        nint instance)
    {
        _listView = listView;
        _notificationTarget = notificationTarget;
        _originalRectangle = originalRectangle;
        _container = container;
        _className = className;
        _instance = instance;
    }

    public static GenesisChannelContextMenuBridge? TryCreate(
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

        var notificationTarget = GetParent(listView);
        if (notificationTarget == 0 ||
            !GetWindowRect(listView, out var screenRectangle))
        {
            return null;
        }

        var topLeft = new Win32Point(screenRectangle.Left, screenRectangle.Top);
        var bottomRight = new Win32Point(screenRectangle.Right, screenRectangle.Bottom);
        if (!ScreenToClient(notificationTarget, ref topLeft) ||
            !ScreenToClient(notificationTarget, ref bottomRight))
        {
            return null;
        }

        var originalRectangle = new WindowRectangle(
            topLeft.X,
            topLeft.Y,
            bottomRight.X,
            bottomRight.Y);
        var width = Math.Max(1, originalRectangle.Right - originalRectangle.Left);
        var height = Math.Max(1, originalRectangle.Bottom - originalRectangle.Top);
        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisChannelContext-{Guid.NewGuid():N}";
        var windowClass = new Win32WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<Win32WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            return null;
        }

        var container = CreateWindowExW(
            0,
            className,
            string.Empty,
            WindowStyleChild | WindowStyleVisible,
            originalRectangle.Left,
            originalRectangle.Top,
            width,
            height,
            notificationTarget,
            0,
            instance,
            0);
        if (container == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        if (SetParent(listView, container) == 0 ||
            !MoveWindow(listView, 0, 0, width, height, true))
        {
            _ = SetParent(listView, notificationTarget);
            _ = MoveWindow(
                listView,
                originalRectangle.Left,
                originalRectangle.Top,
                width,
                height,
                true);
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var state = new ContextState(
            container,
            notificationTarget,
            listView,
            viewModel,
            channelCatalogSession);
        if (!StatesByContainer.TryAdd(container, state))
        {
            _ = SetParent(listView, notificationTarget);
            _ = MoveWindow(
                listView,
                originalRectangle.Left,
                originalRectangle.Top,
                width,
                height,
                true);
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        return new(
            listView,
            notificationTarget,
            originalRectangle,
            container,
            className,
            instance);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            StatesByContainer.TryRemove(_container, out _);
            if (_listView != 0 &&
                IsWindow(_listView) &&
                _notificationTarget != 0 &&
                IsWindow(_notificationTarget))
            {
                var width = Math.Max(1, _originalRectangle.Right - _originalRectangle.Left);
                var height = Math.Max(1, _originalRectangle.Bottom - _originalRectangle.Top);
                _ = SetParent(_listView, _notificationTarget);
                _ = MoveWindow(
                    _listView,
                    _originalRectangle.Left,
                    _originalRectangle.Top,
                    width,
                    height,
                    true);
            }

            if (_container != 0 && IsWindow(_container))
            {
                _ = DestroyWindow(_container);
            }

            _ = UnregisterClassW(_className, _instance);
        }

        return ValueTask.CompletedTask;
    }

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageNcDestroy)
        {
            StatesByContainer.TryRemove(window, out _);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageNotify &&
            StatesByContainer.TryGetValue(window, out var state))
        {
            state.ProcessNotify(lParam);
            return SendMessageW(
                state.NotificationTarget,
                message,
                wParam,
                lParam);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class ContextState
    {
        private readonly nint _container;
        private readonly nint _listView;
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly GenesisGuiNextTabsViewModel? _guiNextTabs;
        private readonly GenesisInteractionCommandLanes _commandLanes = new();

        public ContextState(
            nint container,
            nint notificationTarget,
            nint listView,
            GenesisMainViewModel viewModel,
            GenesisChannelCatalogSession channelCatalogSession)
        {
            _container = container;
            NotificationTarget = notificationTarget;
            _listView = listView;
            _viewModel = viewModel;
            _interaction = new(viewModel, channelCatalogSession);
            _guiNextTabs = channelCatalogSession.GuiNextTabs;
        }

        public nint NotificationTarget { get; }

        public void ProcessNotify(nint lParam)
        {
            if (lParam == 0)
            {
                return;
            }

            var header = Marshal.PtrToStructure<Win32NotifyHeader>(lParam);
            if (header.WindowFrom != _listView)
            {
                return;
            }

            var code = unchecked((int)header.Code);
            if (code == ListViewNotifyKeyDown)
            {
                var keyDown = Marshal.PtrToStructure<Win32ListViewKeyDown>(lParam);
                ProcessKey(keyDown.VirtualKey);
                return;
            }

            if (code != NotifyRightClick)
            {
                return;
            }

            var row = GenesisChannelContextMenuNative.GetRowAtCursor(_listView);
            var renderedRow = GenesisChannelListRenderedChannels.FindRow(_listView, row);
            if (GenesisRecordingFileContextActions.TryCapture(
                    renderedRow,
                    out var recordingTarget))
            {
                _ = GenesisChannelListNativeSelection.SelectRowWithoutEnsureVisible(
                    _listView,
                    row,
                    _ => SynchronizeManagedSelection(renderedRow!));
                OpenRecordingFileMenu(recordingTarget!, listViewRow: -1);
                return;
            }

            if (_interaction.IsRecordingHistoryView || renderedRow is null || renderedRow.RecordingRule is not null || renderedRow.RecordingLibrary is not null || renderedRow.RecordingHistory is not null || renderedRow.Log is not null || renderedRow.Publishing is not null)
            {
                return;
            }

            var selectionSynchronized =
                GenesisChannelListNativeSelection.SelectRowWithoutEnsureVisible(
                    _listView,
                    row,
                    _ => SynchronizeManagedSelection(renderedRow));
            var actions = GenesisChannelContextActions.Capture(_viewModel, renderedRow);
            _ = GenesisChannelContextMenuPointer.TryOpenAfterSelection(
                selectionSynchronized,
                () => ProcessContextMenuCommand(
                    GenesisChannelContextMenuNative.Show(
                        _container,
                        renderedRow.IsInteractive,
                        ResolveFavoriteText(renderedRow),
                        actions),
                    renderedRow,
                    actions));
        }

        private void ProcessKey(ushort virtualKey)
        {
            if (GenesisChannelContextMenuKeyboard.IsOpenGesture(
                    virtualKey,
                    IsShiftPressed()))
            {
                ShowSelectedContextMenu();
                return;
            }

            if (virtualKey == VirtualKeyC && IsControlPressed())
            {
                var target = ResolveKeyboardTarget();
                var source = GenesisGuiNextChannelDetailsProjection.FromRow(target?.RenderedRow).Source;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    _ = GenesisChannelContextMenuNative.CopySourceToClipboard(
                        _container,
                        source);
                }

                return;
            }

            if (virtualKey == VirtualKeyEnter && _commandLanes.CanStartPlayback)
            {
                var interactiveChannel = ResolveKeyboardTarget()?.RenderedRow.InteractiveChannel;
                if (interactiveChannel is not null)
                {
                    _commandLanes.TrackPlayback(ObserveActivationAsync(
                        _interaction.ActivateChannelAsync(interactiveChannel)));
                }
            }
            else if (virtualKey == VirtualKeyF5 &&
                _commandLanes.CanStartRefresh &&
                _interaction.CanRefreshCurrentView)
            {
                _commandLanes.TrackRefresh(ObserveCommandAsync(
                    _interaction.RefreshCurrentViewAsync()));
            }
            else if (virtualKey == VirtualKeyEscape && _commandLanes.CanStartStop)
            {
                _commandLanes.TrackStop(ObserveCommandAsync(_interaction.StopAsync()));
            }
        }

        private void ShowSelectedContextMenu()
        {
            var target = ResolveKeyboardTarget();
            if (target is null)
            {
                return;
            }

            var resolvedTarget = target.Value;
            var renderedRow = resolvedTarget.RenderedRow;
            if (GenesisRecordingFileContextActions.TryCapture(
                    renderedRow,
                    out var recordingTarget))
            {
                OpenRecordingFileMenu(recordingTarget!, resolvedTarget.Row);
                return;
            }

            if (_interaction.IsRecordingHistoryView || renderedRow.RecordingRule is not null || renderedRow.RecordingLibrary is not null || renderedRow.RecordingHistory is not null || renderedRow.Log is not null || renderedRow.Publishing is not null)
            {
                return;
            }

            var actions = GenesisChannelContextActions.Capture(_viewModel, renderedRow);
            ProcessContextMenuCommand(
                GenesisChannelContextMenuNative.ShowResolvedRow(
                    _container,
                    _listView,
                    resolvedTarget.Row,
                    renderedRow.IsInteractive,
                    ResolveFavoriteText(renderedRow),
                    actions),
                renderedRow,
                actions);
        }

        private GenesisChannelContextMenuRenderedKeyboardTarget? ResolveKeyboardTarget() =>
            GenesisChannelContextMenuKeyboard.ResolveRenderedTarget(
                _listView,
                GenesisChannelContextMenuNative.ResolveKeyboardMenuRow,
                GenesisChannelListRenderedChannels.FindRow);

        private bool SynchronizeManagedSelection(GenesisGuiNextNativeTabRow row)
        {
            if (row.InteractiveChannel is { } interactiveChannel)
            {
                return _interaction.SelectChannel(interactiveChannel);
            }

            _viewModel.SelectedChannel = null;
            return true;
        }

        private string? ResolveFavoriteText(GenesisGuiNextNativeTabRow row) =>
            GenesisChannelContextMenuFavoritePresentation.Resolve(_guiNextTabs, row);

        private void ProcessContextMenuCommand(
            GenesisChannelContextMenuCommand command,
            GenesisGuiNextNativeTabRow row,
            GenesisChannelContextTarget actions)
        {
            if (_interaction.IsRecordingHistoryView || row.RecordingRule is not null || row.RecordingLibrary is not null || row.RecordingHistory is not null || row.Log is not null || row.Publishing is not null)
            {
                return;
            }

            if (GenesisChannelContextActions.IsAction(command))
            {
                _ = GenesisChannelContextActions.ExecuteAsync(
                    _viewModel, actions, command, new SystemGenesisChannelContextEffects(_container));
            }
            else if (command == GenesisChannelContextMenuCommand.Play &&
                row.InteractiveChannel is { } interactiveChannel &&
                _commandLanes.CanStartPlayback)
            {
                _commandLanes.TrackPlayback(ObserveCommandAsync(
                    _interaction.PlaySourceAsync(interactiveChannel.Source)));
            }
            else if (command == GenesisChannelContextMenuCommand.PlayInNewWindow &&
                row.InteractiveChannel is { } newWindowChannel &&
                _commandLanes.CanStartPlayback)
            {
                _commandLanes.TrackPlayback(ObserveCommandAsync(
                    _interaction.PlaySourceInNewWindowAsync(newWindowChannel.Source)));
            }
            else if (command == GenesisChannelContextMenuCommand.ManualSourceUrl)
            {
                var owner = GetAncestor(_container, GetAncestorRoot);
                _ = GenesisSourceUrlPlaybackDialog.TryShow(
                    owner == 0 ? _container : owner,
                    _viewModel,
                    _interaction);
            }
            else if (command == GenesisChannelContextMenuCommand.CopySource)
            {
                var source = actions.SourceToCopy;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    _ = GenesisChannelContextMenuNative.CopySourceToClipboard(
                        _container,
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

        private void OpenRecordingFileMenu(
            GenesisRecordingFileContextTarget target,
            int listViewRow)
        {
            var owner = GetAncestor(_container, GetAncestorRoot);
            var menuOwner = owner == 0 ? _container : owner;
            _ = SetForegroundWindow(menuOwner);
            var fileExists = File.Exists(target.FilePath);
            var command = listViewRow >= 0
                ? GenesisChannelContextMenuNative.ShowRecordingFileForRow(
                    menuOwner,
                    _listView,
                    listViewRow,
                    fileExists)
                : GenesisChannelContextMenuNative.ShowRecordingFile(
                    menuOwner,
                    fileExists);
            if (command == GenesisRecordingFileContextCommand.None)
            {
                return;
            }

            _ = ObserveCommandAsync(new ValueTask(
                GenesisRecordingFileContextActions.ExecuteAsync(
                    target,
                    command,
                    new SystemGenesisRecordingFileContextEffects(menuOwner),
                    RefreshRecordingViewsAfterDeleteAsync)));
        }

        private async Task RefreshRecordingViewsAfterDeleteAsync()
        {
            if (_guiNextTabs is null)
            {
                return;
            }

            if (_guiNextTabs.CanRefreshRecordingLibrary)
            {
                await _guiNextTabs.RefreshRecordingLibraryAsync().ConfigureAwait(false);
            }

            if (_guiNextTabs.SelectedTab == GenesisGuiNextTab.Recording &&
                _guiNextTabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.History)
            {
                await _guiNextTabs.RefreshRecordingHistoryAsync().ConfigureAwait(false);
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
    private readonly record struct WindowRectangle(
        int Left,
        int Top,
        int Right,
        int Bottom);

}

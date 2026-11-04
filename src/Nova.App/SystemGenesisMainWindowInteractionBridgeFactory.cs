using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed class SystemGenesisMainWindowInteractionBridgeFactory :
    IGenesisMainWindowInteractionBridgeFactory
{
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowMessageNotify = 0x004E;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageApplication = 0x8000;
    private const uint WindowMessageRefresh = WindowMessageApplication + 0x715;
    private const int ButtonNotificationClicked = 0;
    private const int EditNotificationChange = 0x0300;
    private const int NotifyDoubleClick = -3;
    private const int ListViewNotifyItemChanged = -101;
    private const int ListViewNotifyColumnClick = -108;
    private const uint ListViewMessageFirst = 0x1000;
    private const uint ListViewMessageGetNextItem = ListViewMessageFirst + 12;
    private const int ListViewNextItemSelected = 0x0002;
    private static readonly ConcurrentDictionary<nint, BridgeState> StatesByContainer = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private const nuint OwnerSubclassIdentifier = 0x7144336;
    private static readonly SubclassProcedure OwnerProcedure = ProcessOwnerMessage;
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    private static nint ProcessOwnerMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WindowMessageNcDestroy)
        {
            _ = RemoveWindowSubclass(window, OwnerProcedure, id);
        }
        else if (message == WindowMessageNotify && lParam != 0 &&
            StatesByContainer.TryGetValue(unchecked((nint)data), out var state))
        {
            // List-view caches its original notification parent. SetParent only
            // changes the HWND hierarchy; real mouse notifications still arrive
            // here. ProcessNotify checks the sender and actual hit row.
            state.ProcessNotify(lParam);
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public static SystemGenesisMainWindowInteractionBridgeFactory Instance { get; } = new();

    internal static TimeSpan GuiNextStatusRefreshInterval { get; } = TimeSpan.FromSeconds(1);

    internal static nint GetStatusWindow(nint container) =>
        StatesByContainer.TryGetValue(container, out var state) ? state.StatusWindow : 0;

    private SystemGenesisMainWindowInteractionBridgeFactory()
    {
    }

    internal static IReadOnlyList<GenesisGuiNextNativeTabRow> CreateChannelsRowsForNativeList(
        IReadOnlyList<GenesisChannelItem> channels,
        GenesisGuiNextTabsViewModel? guiNextTabs)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (guiNextTabs is null)
        {
            return GenesisGuiNextNativeTabProjection.CreateLiveChannels(channels);
        }

        var visibleKeys = guiNextTabs.VisibleChannels
            .Select(static channel => GenesisChannelIdentity.GetStableKey(channel))
            .ToHashSet(StringComparer.Ordinal);
        var rows = new List<GenesisGuiNextNativeTabRow>(
            guiNextTabs.VisibleChannels.Count + guiNextTabs.VisibleNotices.Count);
        rows.AddRange(
            GenesisGuiNextNativeTabProjection.CreateLiveChannels(
                channels
                    .Where(channel => visibleKeys.Contains(
                        GenesisChannelIdentity.GetStableKey(channel)))
                    .ToArray()));
        rows.AddRange(
            GenesisGuiNextNativeTabProjection.CreateNoticeRows(guiNextTabs.VisibleNotices));
        return Array.AsReadOnly(rows.ToArray());
    }

    internal static int FindSelectedRow(
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows,
        GenesisChannelItem selectedChannel)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selectedChannel);
        for (var index = 0; index < rows.Count; index++)
        {
            var interactiveChannel = rows[index].InteractiveChannel;
            if (interactiveChannel is not null &&
                GenesisChannelIdentity.Matches(interactiveChannel, selectedChannel))
            {
                return index;
            }
        }

        return -1;
    }

    internal static string CreateStatusText(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession? channelCatalogSession,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return channelCatalogSession?.GuiNextTabs is null
            ? GenesisStatusPresentation.Format(viewModel)
            : GenesisStatusPresentation.Format(
                viewModel,
                channelCatalogSession.Diagnostics,
                now);
    }

    internal static bool RequiresStatusClock(
        GenesisChannelCatalogSession? channelCatalogSession) =>
        channelCatalogSession?.GuiNextTabs is not null;

    public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (windowToken == 0)
        {
            return null;
        }

        var parent = (nint)windowToken;
        var sourceLabel = FindWindowExW(parent, 0, "STATIC", "Source");
        var sourceEdit = FindWindowExW(parent, 0, "EDIT", null);
        var playButton = FindWindowExW(parent, 0, "BUTTON", "Play");
        var stopButton = FindWindowExW(parent, 0, "BUTTON", "Stop");
        var channelList = FindWindowExW(parent, 0, "SysListView32", null);
        var status = FindWindowExW(parent, sourceLabel, "STATIC", null);
        if (sourceLabel == 0 ||
            sourceEdit == 0 ||
            playButton == 0 ||
            stopButton == 0 ||
            channelList == 0 ||
            status == 0 ||
            !GetClientRect(parent, out var clientRectangle))
        {
            return null;
        }

        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisMessage-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            BackgroundBrush = GenesisNativeWindowClassPresentation.WindowBackgroundBrush,
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
            0,
            0,
            Math.Max(1, clientRectangle.Right - clientRectangle.Left),
            Math.Max(1, clientRectangle.Bottom - clientRectangle.Top),
            parent,
            0,
            instance,
            0);
        if (container == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var controls = new[]
        {
            sourceLabel,
            sourceEdit,
            playButton,
            stopButton,
            channelList,
            status,
        };
        var reparented = new List<nint>(controls.Length);
        foreach (var control in controls)
        {
            if (SetParent(control, container) == 0)
            {
                RestoreParents(reparented, parent);
                _ = DestroyWindow(container);
                _ = UnregisterClassW(className, instance);
                return null;
            }

            reparented.Add(control);
        }

        var channelCatalogSession = GenesisChannelRefreshInteractionScope.Find(viewModel);
        var guiNextTabs = channelCatalogSession?.GuiNextTabs;
        var state = new BridgeState(
            container,
            viewModel,
            guiNextTabs,
            channelCatalogSession,
            sourceEdit,
            playButton,
            stopButton,
            channelList,
            status);
        if (!StatesByContainer.TryAdd(container, state))
        {
            RestoreParents(controls, parent);
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        if (!SetWindowSubclass(parent, OwnerProcedure, OwnerSubclassIdentifier, unchecked((nuint)container)))
        {
            _ = StatesByContainer.TryRemove(container, out _);
            RestoreParents(controls, parent);
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        state.Attach();
        state.RefreshControls();
        return new RegisteredContainerLifetime(
            parent,
            container,
            className,
            instance,
            controls);
    }

    private static void RestoreParents(IEnumerable<nint> controls, nint parent)
    {
        foreach (var control in controls)
        {
            if (control != 0 && IsWindow(control))
            {
                _ = SetParent(control, parent);
            }
        }
    }

    private static string ReadWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var characterCapacity = checked(length + 1);
        var byteCapacity = checked(characterCapacity * sizeof(char));
        var buffer = Marshal.AllocHGlobal(byteCapacity);
        try
        {
            var copied = GetWindowTextW(window, buffer, characterCapacity);
            return copied <= 0
                ? string.Empty
                : Marshal.PtrToStringUni(buffer, copied);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageNcDestroy)
        {
            if (StatesByContainer.TryRemove(window, out var removedState))
            {
                removedState.Dispose();
            }

            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (StatesByContainer.TryGetValue(window, out var state))
        {
            if (message == WindowMessageCommand)
            {
                state.ProcessCommand(wParam, lParam);
            }
            else if (message == WindowMessageNotify)
            {
                state.ProcessNotify(lParam);
            }
            else if (message == WindowMessageRefresh)
            {
                state.RefreshControls();
                return 0;
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class BridgeState : IDisposable
    {
        internal nint StatusWindow => _status;
        private readonly nint _container;
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisGuiNextTabsViewModel? _guiNextTabs;
        private readonly GenesisChannelCatalogSession? _channelCatalogSession;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly nint _sourceEdit;
        private readonly nint _playButton;
        private readonly nint _stopButton;
        private readonly nint _channelList;
        private readonly nint _status;
        private readonly GenesisSourceTextProjection _sourceTextProjection = new();
        private GenesisGuiNextNativeRowRebuildIdentity? _renderedRowIdentity;
        private object? _renderedChannelFilterRows;
        private object? _renderedNoticeFilterRows;
        private IReadOnlyList<GenesisNativeListColumnPresentation>? _renderedColumns =
            GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Channels,
                GenesisGuiNextRecordingPageMode.ActiveRecordings);
        private IReadOnlyList<GenesisGuiNextNativeTabRow> _renderedRows =
            Array.AsReadOnly(Array.Empty<GenesisGuiNextNativeTabRow>());
        private Timer? _statusRefreshTimer;
        private Task _pendingCommand = Task.CompletedTask;
        private int _selectionChangesSuppressed;
        private int _detached;

        public BridgeState(
            nint container,
            GenesisMainViewModel viewModel,
            GenesisGuiNextTabsViewModel? guiNextTabs,
            GenesisChannelCatalogSession? channelCatalogSession,
            nint sourceEdit,
            nint playButton,
            nint stopButton,
            nint channelList,
            nint status)
        {
            _container = container;
            _viewModel = viewModel;
            _guiNextTabs = guiNextTabs;
            _channelCatalogSession = channelCatalogSession;
            _interaction = new(viewModel);
            _sourceEdit = sourceEdit;
            _playButton = playButton;
            _stopButton = stopButton;
            _channelList = channelList;
            _status = status;
        }

        public void Attach()
        {
            GenesisChannelListRenderedChannels.SetRows(_channelList, _renderedRows);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_guiNextTabs is not null)
            {
                _guiNextTabs.PropertyChanged += OnGuiNextTabsPropertyChanged;
                if (_channelCatalogSession is not null)
                {
                    _channelCatalogSession.DiagnosticsChanged += OnDiagnosticsChanged;
                }

                if (RequiresStatusClock(_channelCatalogSession))
                {
                    _statusRefreshTimer = new Timer(
                        static state => ((BridgeState)state!).ObserveStatusTick(),
                        this,
                        GuiNextStatusRefreshInterval,
                        GuiNextStatusRefreshInterval);
                }
            }
        }

        private void ObserveStatusTick()
        {
            if (Volatile.Read(ref _detached) != 0) return;
            if (_guiNextTabs?.IsRecordingHistoryPage == true)
                _ = _guiNextTabs.RefreshRecordingHistoryAsync();
            PostRefresh();
        }

        public void Detach()
        {
            if (Interlocked.Exchange(ref _detached, 1) == 0)
            {
                var statusRefreshTimer = Interlocked.Exchange(
                    ref _statusRefreshTimer,
                    null);
                statusRefreshTimer?.Dispose();
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                if (_guiNextTabs is not null)
                {
                    _guiNextTabs.PropertyChanged -= OnGuiNextTabsPropertyChanged;
                    if (_channelCatalogSession is not null)
                    {
                        _channelCatalogSession.DiagnosticsChanged -= OnDiagnosticsChanged;
                    }
                }

                GenesisChannelListRenderedChannels.Remove(_channelList);
            }
        }

        public void Dispose()
        {
            Detach();
            GC.SuppressFinalize(this);
        }

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (lParam == _sourceEdit && notificationCode == EditNotificationChange)
            {
                if (_interaction.IsRecordingHistoryView || _interaction.IsPublishingView) return;
                var sourceText = ReadWindowText(_sourceEdit);
                _sourceTextProjection.ObserveNativeEdit(sourceText);
                _viewModel.SourceText = sourceText;
                return;
            }

            if (notificationCode != ButtonNotificationClicked || !_pendingCommand.IsCompleted)
            {
                return;
            }

            if (lParam == _playButton)
            {
                _pendingCommand = ObserveCommandAsync(
                    _interaction.PlaySourceAsync(ReadWindowText(_sourceEdit)));
            }
            else if (lParam == _stopButton)
            {
                _pendingCommand = ObserveCommandAsync(_interaction.StopAsync());
            }
        }

        public void ProcessNotify(nint lParam)
        {
            if (lParam == 0)
            {
                return;
            }

            var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
            if (header.WindowFrom != _channelList)
            {
                return;
            }

            var code = unchecked((int)header.Code);
            if (code == ListViewNotifyColumnClick)
            {
                if (_guiNextTabs is null ||
                    _guiNextTabs.SelectedTab == GenesisGuiNextTab.Channels)
                {
                    var notification = Marshal.PtrToStructure<ListViewNotify>(lParam);
                    _ = _interaction.SortChannelColumn(notification.SubItem);
                }

                return;
            }

            if (code == ListViewNotifyItemChanged)
            {
                var renderedRows = _renderedRows;
                _ = GenesisChannelListNativeSelection.SynchronizeManagedSelection(
                    GetSelectedChannelIndex(),
                    Volatile.Read(ref _selectionChangesSuppressed) != 0,
                    row => SynchronizeSelectedRow(renderedRows, row),
                    () => _viewModel.SelectedChannel = null);
                return;
            }

            if (code != NotifyDoubleClick || !_pendingCommand.IsCompleted)
            {
                return;
            }

            // NMITEMACTIVATE shares this prefix with NMLISTVIEW. iItem is the
            // actual double-click hit (-1 for blank space), not the selected row.
            var activation = Marshal.PtrToStructure<ListViewNotify>(lParam);
            _pendingCommand = ObserveActivationAsync(GenesisChannelListActivation.ActivateAsync(
                code, activation.Item, _renderedRows, _interaction,
                _pendingCommand.IsCompleted && Volatile.Read(ref _selectionChangesSuppressed) == 0));
        }

        public void RefreshControls()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            var selectedTab = _guiNextTabs?.SelectedTab ?? GenesisGuiNextTab.Channels;
            if (selectedTab == GenesisGuiNextTab.Publishing) _guiNextTabs?.RefreshPublishingStatus();
            if (_guiNextTabs is not null && _channelCatalogSession is not null)
                _guiNextTabs.UpdateLogDiagnostics(_channelCatalogSession.Diagnostics, _viewModel.IsChannelCatalogRefreshing);
            var recordingPageMode = _guiNextTabs?.RecordingPageMode ??
                GenesisGuiNextRecordingPageMode.ActiveRecordings;
            var sourceRows = ResolveSourceRows(selectedTab, recordingPageMode);
            var rowIdentity = GenesisGuiNextNativeRowRebuildIdentity.Create(
                sourceRows,
                selectedTab,
                recordingPageMode,
                _guiNextTabs?.SuppressionRows);
            var channelFilterRows = selectedTab == GenesisGuiNextTab.Channels
                ? _guiNextTabs?.VisibleChannels
                : null;
            var noticeFilterRows = selectedTab == GenesisGuiNextTab.Channels
                ? _guiNextTabs?.VisibleNotices
                : null;
            var channelsFilterChanged =
                selectedTab == GenesisGuiNextTab.Channels &&
                (!ReferenceEquals(_renderedChannelFilterRows, channelFilterRows) ||
                    !ReferenceEquals(_renderedNoticeFilterRows, noticeFilterRows));
            if (rowIdentity.RequiresRebuild(_renderedRowIdentity) || channelsFilterChanged)
            {
                IReadOnlyList<GenesisGuiNextNativeTabRow> rows;
                if (_guiNextTabs is null)
                {
                    rows = CreateChannelsRowsForNativeList(
                        _viewModel.Channels,
                        guiNextTabs: null);
                }
                else if (selectedTab == GenesisGuiNextTab.Channels)
                {
                    rows = CreateChannelsRowsForNativeList(
                        _viewModel.Channels,
                        _guiNextTabs);
                }
                else
                {
                    rows = GenesisGuiNextNativeTabProjection.Create(
                        _guiNextTabs,
                        selectedTab);
                }

                var newColumns = GenesisGuiNextRecordingListPresentation.Columns(selectedTab, recordingPageMode);
                if (!ReferenceEquals(_renderedColumns, newColumns))
                {
                    Interlocked.Exchange(ref _selectionChangesSuppressed, 1);
                    var columnsReplaced = false;
                    try
                    {
                        columnsReplaced = GenesisGuiNextRecordingListPresentation.TryReplaceColumns(
                            () => GenesisMainWindowNativeSurface.ReplaceRows(_channelList, Array.Empty<string[]>()),
                            () => GenesisMainWindowNativeSurface.ReplaceColumns(
                                _channelList, GenesisMainWindowNativeSurface.GetColumnCount(_channelList), newColumns),
                            () =>
                            {
                                _ = GenesisMainWindowNativeSurface.ReplaceColumns(
                                _channelList, GenesisMainWindowNativeSurface.GetColumnCount(_channelList),
                                Array.Empty<GenesisNativeListColumnPresentation>());
                            });
                    }
                    finally
                    {
                        Volatile.Write(ref _selectionChangesSuppressed, 0);
                    }
                    if (!columnsReplaced)
                    {
                        _renderedColumns = null;
                        _renderedRows = [];
                        _renderedRowIdentity = null;
                        GenesisChannelListRenderedChannels.SetRows(_channelList, _renderedRows);
                        _viewModel.SelectedChannel = null;
                        GenesisGuiNextAppShellBridge.SynchronizeRecordingRows(_channelList);
                        return;
                    }

                    _renderedColumns = newColumns;
                }

                var rowValues = rows.Select(GenesisGuiNextRecordingListPresentation.Values).ToArray();
                Interlocked.Exchange(ref _selectionChangesSuppressed, 1);
                try
                {
                    var rowsReplaced = GenesisMainWindowNativeSurface.ReplaceRows(
                        _channelList,
                        rowValues);
                    _renderedRowIdentity = rowsReplaced ? rowIdentity : null;
                    _renderedChannelFilterRows = channelFilterRows;
                    _renderedNoticeFilterRows = noticeFilterRows;
                    _renderedRows = rowsReplaced
                        ? rows
                        : Array.AsReadOnly(Array.Empty<GenesisGuiNextNativeTabRow>());
                    if (_guiNextTabs is { RecordingRuleSelectionAfterSave: { } ruleId } &&
                        selectedTab == GenesisGuiNextTab.Recording &&
                        recordingPageMode == GenesisGuiNextRecordingPageMode.Rules)
                    {
                        GenesisChannelListRenderedChannels.AnchorRecordingRule(_channelList, ruleId);
                        _guiNextTabs.ClearRecordingRuleSelectionAfterSave();
                    }
                    GenesisChannelListRenderedChannels.SetRows(
                        _channelList,
                        _renderedRows,
                        historyMode: _guiNextTabs?.IsRecordingHistoryPage == true);
                    if (rowsReplaced)
                    {
                        RestoreSelectedChannel();
                    }
                    else
                    {
                        _viewModel.SelectedChannel = null;
                    }
                    GenesisGuiNextAppShellBridge.SynchronizeRecordingRows(_channelList);
                }
                finally
                {
                    Volatile.Write(ref _selectionChangesSuppressed, 0);
                }
            }

            var sourceText = _sourceTextProjection.Project(_viewModel.SourceText);
            if (sourceText is not null)
            {
                _ = SetWindowTextW(_sourceEdit, sourceText);
            }

            _ = EnableWindow(_playButton, !_interaction.IsRecordingHistoryView && !_interaction.IsPublishingView && _viewModel.CanPlay);
            _ = EnableWindow(_stopButton, !_interaction.IsRecordingHistoryView && !_interaction.IsPublishingView && _viewModel.CanStop);
            var statusText = CreateStatusText(
                _viewModel,
                _channelCatalogSession,
                DateTimeOffset.UtcNow);
            _ = SetWindowTextW(_status, statusText);
        }

        private object ResolveSourceRows(
            GenesisGuiNextTab selectedTab,
            GenesisGuiNextRecordingPageMode recordingPageMode)
        {
            if (_guiNextTabs is null || selectedTab == GenesisGuiNextTab.Channels)
            {
                return _viewModel.Channels;
            }

            return selectedTab switch
            {
                GenesisGuiNextTab.New => _guiNextTabs.VisibleNewChannels,
                GenesisGuiNextTab.Favorites => _guiNextTabs.VisibleFavoriteChannels,
                GenesisGuiNextTab.Playing => _guiNextTabs.Playing,
                GenesisGuiNextTab.Publishing => _guiNextTabs.PublishingRows,
                GenesisGuiNextTab.Logs => _guiNextTabs.LogRows,
                GenesisGuiNextTab.Recording => recordingPageMode switch
                {
                    GenesisGuiNextRecordingPageMode.Rules => _guiNextTabs.RecordingRuleRows,
                    GenesisGuiNextRecordingPageMode.Library => _guiNextTabs.RecordingLibraryRows,
                    GenesisGuiNextRecordingPageMode.History => _guiNextTabs.RecordingHistoryRows,
                    _ => _guiNextTabs.ActiveRecordings,
                },
                _ => _viewModel.Channels,
            };
        }

        private bool SynchronizeSelectedRow(
            IReadOnlyList<GenesisGuiNextNativeTabRow> rows,
            int row)
        {
            if (row < 0 || row >= rows.Count)
            {
                return false;
            }

            var interactiveChannel = rows[row].InteractiveChannel;
            if (interactiveChannel is null)
            {
                if (rows[row].ActiveRecording is null)
                {
                    _viewModel.SelectedChannel = null;
                }

                return true;
            }

            return _interaction.SelectChannel(interactiveChannel);
        }

        private void RestoreSelectedChannel()
        {
            var selectedChannel = _viewModel.SelectedChannel;
            if (selectedChannel is null)
            {
                return;
            }

            var selectedRow = FindSelectedRow(_renderedRows, selectedChannel);
            if (selectedRow >= 0)
            {
                _ = GenesisChannelListNativeSelection.RestoreRow(
                    _channelList,
                    selectedRow,
                    () => _viewModel.SelectedChannel = null);
                return;
            }

            if (_guiNextTabs is not null &&
                _guiNextTabs.SelectedTab != GenesisGuiNextTab.Recording)
            {
                _viewModel.SelectedChannel = null;
            }
        }

        private int GetSelectedChannelIndex()
        {
            var selected = SendMessageW(
                _channelList,
                ListViewMessageGetNextItem,
                nuint.MaxValue,
                (nint)ListViewNextItemSelected);
            return selected.ToInt32();
        }

        private void OnViewModelPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            PostRefresh();
        }

        private void OnDiagnosticsChanged(object? sender, EventArgs eventArgs)
        {
            PostRefresh();
        }

        private void PostRefresh()
        {
            if (Volatile.Read(ref _detached) == 0)
            {
                _ = PostMessageW(_container, WindowMessageRefresh, 0, 0);
            }
        }

        private void OnGuiNextTabsPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            if (eventArgs.PropertyName is
                    nameof(GenesisGuiNextTabsViewModel.SelectedTab) or
                    nameof(GenesisGuiNextTabsViewModel.PublishingRows) or
                    nameof(GenesisGuiNextTabsViewModel.LogRows) or
                    nameof(GenesisGuiNextTabsViewModel.SearchText) or
                    nameof(GenesisGuiNextTabsViewModel.VisibleChannels) or
                    nameof(GenesisGuiNextTabsViewModel.VisibleNotices) or
                    nameof(GenesisGuiNextTabsViewModel.VisibleNewChannels) or
                    nameof(GenesisGuiNextTabsViewModel.VisibleFavoriteChannels) or
                    nameof(GenesisGuiNextTabsViewModel.Playing) or
                    nameof(GenesisGuiNextTabsViewModel.ActiveRecordings) or
                    nameof(GenesisGuiNextTabsViewModel.RecordingPageMode) or
                    nameof(GenesisGuiNextTabsViewModel.RecordingRuleRows) or
                    nameof(GenesisGuiNextTabsViewModel.RecordingHistoryRows) or
                nameof(GenesisGuiNextTabsViewModel.RecordingHistoryStatus) or
                nameof(GenesisGuiNextTabsViewModel.RecordingLibraryRows) or
                    nameof(GenesisGuiNextTabsViewModel.IsRecordingLibraryRefreshing) or
                    nameof(GenesisGuiNextTabsViewModel.RecordingRulesStatus) or
                    nameof(GenesisGuiNextTabsViewModel.IsRecordingRulesLoading))
            {
                _ = PostMessageW(_container, WindowMessageRefresh, 0, 0);
            }
        }

        private static async Task ObserveCommandAsync(ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch
            {
                // Command failures are converted to bounded GUI state by the view model.
            }
        }

        private static async Task ObserveActivationAsync(ValueTask<bool> operation)
        {
            try
            {
                _ = await operation.ConfigureAwait(false);
            }
            catch
            {
                // Activation failures are converted to bounded GUI state by the view model.
            }
        }
    }

    private sealed class RegisteredContainerLifetime : IAsyncDisposable
    {
        private readonly nint _parent;
        private readonly nint _container;
        private readonly string _className;
        private readonly nint _instance;
        private readonly nint[] _controls;
        private int _disposed;

        public RegisteredContainerLifetime(
            nint parent,
            nint container,
            string className,
            nint instance,
            nint[] controls)
        {
            _parent = parent;
            _container = container;
            _className = className;
            _instance = instance;
            _controls = controls;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                if (StatesByContainer.TryRemove(_container, out var state))
                {
                    state.Dispose();
                }

                if (_parent != 0 && IsWindow(_parent))
                {
                    _ = RemoveWindowSubclass(_parent, OwnerProcedure, OwnerSubclassIdentifier);
                    RestoreParents(_controls, _parent);
                }

                if (_container != 0 && IsWindow(_container))
                {
                    _ = DestroyWindow(_container);
                }

                _ = UnregisterClassW(_className, _instance);
            }

            return ValueTask.CompletedTask;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyHeader
    {
        public nint WindowFrom;
        public nuint IdFrom;
        public uint Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListViewNotify
    {
        public NotifyHeader Header;
        public int Item;
        public int SubItem;
        public uint NewState;
        public uint OldState;
        public uint Changed;
        public WindowPoint Action;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(
        nint window,
        nint text,
        int maximumCharacters);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint window, string text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}

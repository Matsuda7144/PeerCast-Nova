using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed class GenesisChannelSearchBoxBridge : IAsyncDisposable
{
    internal const int YellowPageFilterWidth = 176;
    internal const int YellowPageFilterGap = 8;
    internal const int MinimumSearchEditWidth = 120;
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowStyleVerticalScroll = 0x00200000;
    private const uint WindowExtendedStyleClientEdge = 0x00000200;
    private const uint EditStyleAutoHorizontalScroll = 0x0080;
    private const uint ComboBoxStyleDropDownList = 0x0003;
    private const uint EditMessageSetLimitText = 0x00C5;
    private const uint ComboBoxMessageAddString = 0x0143;
    private const uint ComboBoxMessageGetCurrentSelection = 0x0147;
    private const uint ComboBoxMessageResetContent = 0x014B;
    private const uint ComboBoxMessageSetCurrentSelection = 0x014E;
    private const int EditNotificationChange = 0x0300;
    private const int ComboBoxNotificationSelectionChanged = 1;
    private const int LabelWidth = 52;
    internal const int MinimumWidth = LabelWidth + MinimumSearchEditWidth + YellowPageFilterGap + YellowPageFilterWidth;
    private const int SearchCharacterLimit = 256;
    private const int YellowPageDropDownHeight = 240;
    private readonly nint _label;
    private readonly nint _edit;
    private readonly nint _yellowPageFilter;
    private readonly SearchState _state;
    internal bool IsYellowPageFilter(nint window) => window == _yellowPageFilter;
    private readonly bool _modernAppearance;
    internal nint LabelWindow => _label;
    internal nint EditWindow => _edit;
    internal nint FilterWindow => _yellowPageFilter;
    private int _disposed;

    private GenesisChannelSearchBoxBridge(
        nint label,
        nint edit,
        nint yellowPageFilter,
        SearchState state,
        bool modernAppearance)
    {
        _modernAppearance = modernAppearance;
        _label = label;
        _edit = edit;
        _yellowPageFilter = yellowPageFilter;
        _state = state;
    }

    public static GenesisChannelSearchBoxBridge? TryCreate(
        nint statusBar,
        int left,
        int width,
        int height,
        GenesisGuiNextTabsViewModel tabs,
        bool modernAppearance = false)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (statusBar == 0 ||
            !TryResolveControlLayout(width, out var editWidth, out var yellowPageFilterLeft, modernAppearance))
        {
            return null;
        }

        var labelWidth = modernAppearance ? GenesisGuiNextTopBarLayout.SearchLabelWidth : LabelWidth;
        var label = CreateWindowExW(
            0,
            "STATIC",
            "検索",
            WindowStyleChild | WindowStyleVisible,
            left,
            0,
            labelWidth - 4,
            height,
            statusBar,
            0,
            0,
            0);
        var edit = CreateWindowExW(
            modernAppearance ? 0u : WindowExtendedStyleClientEdge,
            "EDIT",
            tabs.SearchText,
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                EditStyleAutoHorizontalScroll | (modernAppearance ? 0x00800000u : 0u), // WS_BORDER
            left + labelWidth,
            2,
            editWidth,
            Math.Max(1, height - 4),
            statusBar,
            0,
            0,
            0);
        var yellowPageFilter = CreateWindowExW(
            0,
            "COMBOBOX",
            string.Empty,
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                WindowStyleVerticalScroll |
                ComboBoxStyleDropDownList | (modernAppearance ? 0x0010u | 0x0200u : 0u), // CBS_OWNERDRAWFIXED | CBS_HASSTRINGS
            left + yellowPageFilterLeft,
            2,
            YellowPageFilterWidth,
            YellowPageDropDownHeight,
            statusBar,
            0,
            0,
            0);
        if (label == 0 || edit == 0 || yellowPageFilter == 0)
        {
            DestroyWindowIfPresent(yellowPageFilter);
            DestroyWindowIfPresent(edit);
            DestroyWindowIfPresent(label);
            return null;
        }

        ApplyDefaultFont(label);
        ApplyDefaultFont(edit);
        ApplyDefaultFont(yellowPageFilter);
        _ = SetWindowTheme(edit, " ", " ");
        _ = SetWindowTheme(yellowPageFilter, " ", " ");
        _ = SendMessageW(edit, EditMessageSetLimitText, SearchCharacterLimit, 0);
        var state = new SearchState(statusBar, edit, yellowPageFilter, tabs);
        state.Attach();
        state.SynchronizeYellowPageFilter();
        return new(label, edit, yellowPageFilter, state, modernAppearance);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _state.Detach();
            DestroyWindowIfPresent(_yellowPageFilter);
            DestroyWindowIfPresent(_edit);
            DestroyWindowIfPresent(_label);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void ProcessCommand(nuint wParam, nint lParam) =>
        _state.ProcessCommand(wParam, lParam);

    internal void SynchronizeYellowPageFilter() =>
        _state.SynchronizeYellowPageFilter();

    internal void UpdateItemHeight(uint dpi)
    {
        _ = SendMessageW(_yellowPageFilter, 0x0153, nuint.MaxValue,
            GenesisMainWindowLayout.Scale(_modernAppearance ? GenesisGuiNextTopBarLayout.Height - 6 : 20, dpi)); // selection field
        _ = SendMessageW(_yellowPageFilter, 0x0153, 0, GenesisMainWindowLayout.Scale(26, dpi));
        if (_modernAppearance)
        {
            var margin = GenesisMainWindowLayout.Scale(8, dpi);
            _ = SendMessageW(_edit, 0x00D3, 3, margin | (margin << 16)); // EM_SETMARGINS; keep native EDIT/IME.
        }
    }

    internal void ApplyLayout(int left, int width, int height, uint dpi)
    {
        if (!TryResolveControlLayout(width, out var editWidth, out var filterLeft, _modernAppearance)) return;
        int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
        var labelWidth = _modernAppearance ? GenesisGuiNextTopBarLayout.SearchLabelWidth : LabelWidth;
        _ = MoveWindow(_label, S(left), 0, S(labelWidth - 4), S(height), true);
        _ = MoveWindow(_edit, S(left + labelWidth), S(2), S(editWidth), S(height - 4), true);
        _ = MoveWindow(_yellowPageFilter, S(left + filterLeft), S(2), S(YellowPageFilterWidth), S(YellowPageDropDownHeight), true);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);

    public bool Focus()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _edit == 0 ||
            !IsWindow(_edit))
        {
            return false;
        }

        _ = SetFocus(_edit);
        return GetFocus() == _edit;
    }

    internal static bool TryResolveControlLayout(
        int totalWidth,
        out int editWidth,
        out int yellowPageFilterLeft,
        bool modernAppearance = false)
    {
        if (modernAppearance)
            return GenesisGuiNextTopBarLayout.TryResolveSearch(totalWidth, out editWidth, out yellowPageFilterLeft);
        yellowPageFilterLeft = totalWidth - YellowPageFilterWidth;
        editWidth = yellowPageFilterLeft - YellowPageFilterGap - LabelWidth;
        if (editWidth < MinimumSearchEditWidth || yellowPageFilterLeft < 0)
        {
            editWidth = 0;
            yellowPageFilterLeft = 0;
            return false;
        }

        return true;
    }

    internal static int ResolveYellowPageSelectionIndex(
        IReadOnlyList<GenesisGuiNextYellowPageFilterOption> options,
        string? selectedYellowPageId)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(selectedYellowPageId))
        {
            return 0;
        }

        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(
                options[index].Id,
                selectedYellowPageId,
                StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }

    internal static bool TryResolveYellowPageId(
        IReadOnlyList<GenesisGuiNextYellowPageFilterOption> options,
        int nativeIndex,
        out string? yellowPageId)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (nativeIndex == 0)
        {
            yellowPageId = null;
            return true;
        }

        var optionIndex = nativeIndex - 1;
        if (optionIndex >= 0 && optionIndex < options.Count)
        {
            yellowPageId = options[optionIndex].Id;
            return true;
        }

        yellowPageId = null;
        return false;
    }

    private static void ApplyDefaultFont(nint control) => _ = GenesisWindowUiFont.Apply(control);

    private static string ReadWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var characterCapacity = checked(length + 1);
        var buffer = Marshal.AllocHGlobal(checked(characterCapacity * sizeof(char)));
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

    private static bool AddComboBoxItem(nint comboBox, string text)
    {
        var nativeText = Marshal.StringToHGlobalUni(text);
        try
        {
            return SendMessageW(
                comboBox,
                ComboBoxMessageAddString,
                0,
                nativeText).ToInt32() >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(nativeText);
        }
    }

    private static void DestroyWindowIfPresent(nint window)
    {
        if (window != 0 && IsWindow(window))
        {
            _ = DestroyWindow(window);
        }
    }

    private sealed class SearchState
    {
        private readonly nint _statusBar;
        private readonly nint _edit;
        private readonly nint _yellowPageFilter;
        private readonly GenesisGuiNextTabsViewModel _tabs;
        private int _detached;

        public SearchState(
            nint statusBar,
            nint edit,
            nint yellowPageFilter,
            GenesisGuiNextTabsViewModel tabs)
        {
            _statusBar = statusBar;
            _edit = edit;
            _yellowPageFilter = yellowPageFilter;
            _tabs = tabs;
        }

        public void Attach() =>
            _tabs.PropertyChanged += OnTabsPropertyChanged;

        public void Detach()
        {
            if (Interlocked.Exchange(ref _detached, 1) == 0)
            {
                _tabs.PropertyChanged -= OnTabsPropertyChanged;
            }
        }

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode == EditNotificationChange && lParam == _edit)
            {
                _tabs.SearchText = ReadWindowText(_edit);
                return;
            }

            if (notificationCode != ComboBoxNotificationSelectionChanged ||
                lParam != _yellowPageFilter)
            {
                return;
            }

            var nativeIndex = SendMessageW(
                _yellowPageFilter,
                ComboBoxMessageGetCurrentSelection,
                0,
                0).ToInt32();
            if (TryResolveYellowPageId(
                    _tabs.YellowPageFilters,
                    nativeIndex,
                    out var yellowPageId))
            {
                _tabs.SelectedYellowPageId = yellowPageId;
            }
        }

        public void SynchronizeYellowPageFilter()
        {
            if (Volatile.Read(ref _detached) != 0)
            {
                return;
            }

            _ = SendMessageW(
                _yellowPageFilter,
                ComboBoxMessageResetContent,
                0,
                0);
            if (!AddComboBoxItem(_yellowPageFilter, "YP: All"))
            {
                return;
            }

            foreach (var option in _tabs.YellowPageFilters)
            {
                if (!AddComboBoxItem(
                        _yellowPageFilter,
                        $"YP: {option.DisplayName}"))
                {
                    return;
                }
            }

            var selectedIndex = ResolveYellowPageSelectionIndex(
                _tabs.YellowPageFilters,
                _tabs.SelectedYellowPageId);
            _ = SendMessageW(
                _yellowPageFilter,
                ComboBoxMessageSetCurrentSelection,
                (nuint)selectedIndex,
                0);
            _ = EnableWindow(
                _yellowPageFilter,
                _tabs.YellowPageFilters.Count > 0);
        }

        private void OnTabsPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            _ = sender;
            if (Volatile.Read(ref _detached) != 0 ||
                (eventArgs.PropertyName != nameof(GenesisGuiNextTabsViewModel.YellowPageFilters) &&
                    eventArgs.PropertyName != nameof(GenesisGuiNextTabsViewModel.SelectedYellowPageId)))
            {
                return;
            }

            _ = PostMessageW(
                _statusBar,
                GenesisGuiNextAppShellBridge.WindowMessageSynchronizeYellowPageFilter,
                0,
                0);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, nint text, int maximumCharacters);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint window, string? subAppName, string? subIdList);

}

using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal interface IGenesisMainWindowNativeSurfaceFactory
{
    IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel);
}

internal sealed class SystemGenesisMainWindowSurfaceTarget :
    IGenesisMainWindowSurfaceTarget
{
    private readonly IGenesisMainWindowNativeSurfaceFactory _factory;
    private readonly Func<bool> _isSupportedPlatform;

    public SystemGenesisMainWindowSurfaceTarget()
        : this(
            SystemGenesisMainWindowNativeSurfaceFactory.Instance,
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal SystemGenesisMainWindowSurfaceTarget(
        IGenesisMainWindowNativeSurfaceFactory factory,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _factory = factory;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public ValueTask<IAsyncDisposable?> OpenAsync(
        long windowToken,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (windowToken == 0 ||
            cancellationToken.IsCancellationRequested ||
            !_isSupportedPlatform())
        {
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }

        return ValueTask.FromResult(_factory.Create(windowToken, viewModel));
    }
}

internal sealed class SystemGenesisMainWindowNativeSurfaceFactory :
    IGenesisMainWindowNativeSurfaceFactory
{
    public static SystemGenesisMainWindowNativeSurfaceFactory Instance { get; } = new();

    private SystemGenesisMainWindowNativeSurfaceFactory()
    {
    }

    public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel) =>
        GenesisMainWindowNativeSurface.TryCreate((nint)windowToken, viewModel);
}

internal sealed class GenesisMainWindowNativeSurface : IAsyncDisposable
{
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowExtendedStyleClientEdge = 0x00000200;
    private const uint EditStyleAutoHorizontalScroll = 0x0080;
    private const uint EditMessageSetLimitText = 0x00C5;
    private const uint ListViewStyleReport = 0x0001;
    private const uint ListViewStyleSingleSelect = 0x0004;
    private const uint ListViewStyleShowSelectionAlways = 0x0008;
    private const uint ListViewExtendedStyleFullRowSelect = 0x00000020;
    private const uint ListViewExtendedStyleDoubleBuffer = 0x00010000;
    private const uint ListViewMessageFirst = 0x1000;
    private const uint ListViewMessageGetHeader = ListViewMessageFirst + 31;
    private const uint ListViewMessageDeleteColumn = ListViewMessageFirst + 28;
    private const uint ListViewMessageDeleteAllItems = ListViewMessageFirst + 9;
    private const uint ListViewMessageInsertItemW = ListViewMessageFirst + 77;
    private const uint ListViewMessageInsertColumnW = ListViewMessageFirst + 97;
    private const uint ListViewMessageSetItemTextW = ListViewMessageFirst + 116;
    private const uint ListViewMessageSetExtendedStyle = ListViewMessageFirst + 54;
    private const uint ListViewItemMaskText = 0x0001;
    private const uint ListViewColumnMaskFormat = 0x0001;
    private const uint ListViewColumnMaskWidth = 0x0002;
    private const uint ListViewColumnMaskText = 0x0004;
    private const int ListViewColumnFormatLeft = 0;
    private const int ListViewColumnFormatRight = 1;
    private const uint WindowMessageClose = 0x0010;
    private const uint CommonControlsListViewClasses = 0x00000001;
    private readonly nint[] _controls;
    private int _disposed;

    private GenesisMainWindowNativeSurface(nint[] controls)
    {
        _controls = controls;
    }

    public static GenesisMainWindowNativeSurface? TryCreate(
        nint parent,
        GenesisMainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (parent == 0 || !GetClientRect(parent, out var clientRectangle))
        {
            return null;
        }

        var commonControls = new InitCommonControls
        {
            Size = (uint)Marshal.SizeOf<InitCommonControls>(),
            Classes = CommonControlsListViewClasses,
        };
        if (!InitCommonControlsEx(ref commonControls))
        {
            return null;
        }

        var width = Math.Max(400, clientRectangle.Right - clientRectangle.Left);
        var height = Math.Max(240, clientRectangle.Bottom - clientRectangle.Top);
        const int margin = 12;
        const int sourceLabelWidth = 52;
        const int commandWidth = 72;
        const int commandGap = 8;
        const int sourceHeight = 24;
        const int sourceTop = 10;
        const int listTop = 46;
        const int sidebarWidth = GenesisGuiNextAppShellPlacement.SidebarWidth;
        const int sidebarGap = GenesisGuiNextAppShellPlacement.SidebarGap;
        const int statusHeight = 24;
        var statusTop = Math.Max(listTop + 100, height - margin - statusHeight);
        var listHeight = Math.Max(100, statusTop - listTop - 8);
        var listLeft = margin + sidebarWidth + sidebarGap;
        var editLeft = margin + sourceLabelWidth;
        var stopLeft = width - margin - commandWidth;
        var playLeft = stopLeft - commandGap - commandWidth;
        var editWidth = Math.Max(120, playLeft - commandGap - editLeft);
        var listWidth = Math.Max(200, width - listLeft - margin);

        var controls = new List<nint>(6);
        nint Create(
            uint extendedStyle,
            string className,
            string text,
            uint style,
            int left,
            int top,
            int controlWidth,
            int controlHeight)
        {
            var control = CreateWindowExW(
                extendedStyle,
                className,
                text,
                style | WindowStyleChild | WindowStyleVisible,
                left,
                top,
                controlWidth,
                controlHeight,
                parent,
                0,
                0,
                0);
            if (control != 0)
            {
                controls.Add(control);
            }

            return control;
        }

        var sourceLabel = Create(
            0,
            "STATIC",
            "Source",
            0,
            margin,
            sourceTop + 4,
            sourceLabelWidth - 4,
            sourceHeight);
        var sourceEdit = Create(
            WindowExtendedStyleClientEdge,
            "EDIT",
            viewModel.SourceText,
            WindowStyleTabStop | EditStyleAutoHorizontalScroll,
            editLeft,
            sourceTop,
            editWidth,
            sourceHeight);
        var playButton = Create(
            0,
            "BUTTON",
            "Play",
            WindowStyleTabStop,
            playLeft,
            sourceTop,
            commandWidth,
            sourceHeight);
        var stopButton = Create(
            0,
            "BUTTON",
            "Stop",
            WindowStyleTabStop,
            stopLeft,
            sourceTop,
            commandWidth,
            sourceHeight);
        var channelList = Create(
            WindowExtendedStyleClientEdge,
            "SysListView32",
            string.Empty,
            WindowStyleTabStop |
                ListViewStyleReport |
            ListViewStyleSingleSelect |
                ListViewStyleShowSelectionAlways,
            listLeft,
            listTop,
            listWidth,
            listHeight);
        var status = Create(
            0,
            "STATIC",
            viewModel.StatusText,
            0,
            listLeft,
            statusTop + 4,
            listWidth,
            statusHeight);

        if (sourceLabel == 0 ||
            sourceEdit == 0 ||
            playButton == 0 ||
            stopButton == 0 ||
            channelList == 0 ||
            status == 0)
        {
            CloseControls(controls);
            return null;
        }

        _ = LimitSourceTextInput(sourceEdit, SendMessageW);
        ApplyDefaultFont(controls);
        _ = EnableWindow(playButton, viewModel.CanPlay);
        _ = EnableWindow(stopButton, viewModel.CanStop);

        _ = SendMessageW(
            channelList,
            ListViewMessageSetExtendedStyle,
            0,
            (nint)((viewModel.UsesModernAppearance ? 0u : 0x00000001u) |
                ListViewExtendedStyleFullRowSelect |
                ListViewExtendedStyleDoubleBuffer));
        GenesisGuiNextAppShellBridge.ApplyListViewColors(channelList, viewModel.UsesModernAppearance);
        _ = GenesisWindowUiFont.Apply(
            SendMessageW(channelList, ListViewMessageGetHeader, 0, 0));

        if (!AddColumns(channelList) || !ReplaceRows(channelList, viewModel.Channels))
        {
            CloseControls(controls);
            return null;
        }

        _ = FocusInitialControl(sourceEdit, SetFocus);
        return new(controls.ToArray());
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CloseControls(_controls);
        }

        return ValueTask.CompletedTask;
    }

    internal static bool ReplaceRows(
        nint listView,
        IReadOnlyList<GenesisChannelItem> channels) =>
        ReplaceRows(
            listView,
            channels,
            ClearRows,
            AddRows);

    internal static bool ReplaceRows(
        nint listView,
        IReadOnlyList<GenesisChannelItem> channels,
        Func<nint, bool> clearRows,
        Func<nint, IReadOnlyList<GenesisChannelItem>, bool> addRows)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(clearRows);
        ArgumentNullException.ThrowIfNull(addRows);
        if (listView == 0 || !clearRows(listView))
        {
            return false;
        }

        if (addRows(listView, channels))
        {
            return true;
        }

        _ = clearRows(listView);
        return false;
    }

    internal static bool ReplaceColumns(
        nint listView,
        int existingColumnCount,
        IReadOnlyList<GenesisNativeListColumnPresentation> columns) =>
        ReplaceColumns(
            listView,
            existingColumnCount,
            columns,
            static (window, index) => SendMessageW(
                window,
                ListViewMessageDeleteColumn,
                (nuint)index,
                0) != 0,
            AddColumns);

    internal static int GetColumnCount(nint listView)
    {
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        return header == 0 ? 0 : Math.Max(0, SendMessageW(header, 0x1200, 0, 0).ToInt32()); // HDM_GETITEMCOUNT
    }

    internal static bool ReplaceColumns(
        nint listView,
        int existingColumnCount,
        IReadOnlyList<GenesisNativeListColumnPresentation> columns,
        Func<nint, int, bool> deleteColumn,
        Func<nint, IReadOnlyList<GenesisNativeListColumnPresentation>, bool> addColumns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(deleteColumn);
        ArgumentNullException.ThrowIfNull(addColumns);
        if (listView == 0 || existingColumnCount < 0)
        {
            return false;
        }

        for (var index = existingColumnCount - 1; index >= 0; index--)
        {
            if (!deleteColumn(listView, index))
            {
                return false;
            }
        }

        return addColumns(listView, columns);
    }

    internal static bool ReplaceRows(
        nint listView,
        IReadOnlyList<string[]> rowValues) =>
        ReplaceRows(
            listView,
            rowValues,
            ClearRows,
            AddRows);

    internal static bool ReplaceRows(
        nint listView,
        IReadOnlyList<string[]> rowValues,
        Func<nint, bool> clearRows,
        Func<nint, IReadOnlyList<string[]>, bool> addRows)
    {
        ArgumentNullException.ThrowIfNull(rowValues);
        ArgumentNullException.ThrowIfNull(clearRows);
        ArgumentNullException.ThrowIfNull(addRows);
        if (listView == 0 || !clearRows(listView))
        {
            return false;
        }

        if (addRows(listView, rowValues))
        {
            return true;
        }

        _ = clearRows(listView);
        return false;
    }

    internal static bool LimitSourceTextInput(
        nint sourceEdit,
        Func<nint, uint, nuint, nint, nint> sendMessage)
    {
        ArgumentNullException.ThrowIfNull(sendMessage);
        if (sourceEdit == 0)
        {
            return false;
        }

        _ = sendMessage(
            sourceEdit,
            EditMessageSetLimitText,
            (nuint)GenesisMainViewModel.MaximumSourceCharacters,
            0);
        return true;
    }

    internal static bool FocusInitialControl(
        nint control,
        Func<nint, nint> setFocus)
    {
        ArgumentNullException.ThrowIfNull(setFocus);
        if (control == 0)
        {
            return false;
        }

        _ = setFocus(control);
        return true;
    }

    internal static bool IsSuccessfulItemTextUpdate(int nativeResult) =>
        nativeResult != 0;

    private static bool ClearRows(nint listView) =>
        SendMessageW(listView, ListViewMessageDeleteAllItems, 0, 0) != 0;

    private static bool AddColumns(nint listView)
    {
        for (var index = 0; index < GenesisChannelListPresentation.Columns.Count; index++)
        {
            var column = GenesisChannelListPresentation.Columns[index];
            var text = Marshal.StringToHGlobalUni(column.Header);
            try
            {
                var nativeColumn = new ListViewColumn
                {
                    Mask = ListViewColumnMaskFormat |
                        ListViewColumnMaskWidth |
                        ListViewColumnMaskText,
                    Format = column.Alignment == GenesisChannelColumnAlignment.Right
                        ? ListViewColumnFormatRight
                        : ListViewColumnFormatLeft,
                    Width = column.PreferredWidth,
                    Text = text,
                };
                if (SendMessageColumn(
                        listView,
                        ListViewMessageInsertColumnW,
                        (nuint)index,
                        ref nativeColumn) == -1)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        return true;
    }

    private static bool AddColumns(
        nint listView,
        IReadOnlyList<GenesisNativeListColumnPresentation> columns)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var text = Marshal.StringToHGlobalUni(column.Header);
            try
            {
                var nativeColumn = new ListViewColumn
                {
                    Mask = ListViewColumnMaskFormat |
                        ListViewColumnMaskWidth |
                        ListViewColumnMaskText,
                    Format = column.Alignment == GenesisChannelColumnAlignment.Right
                        ? ListViewColumnFormatRight
                        : ListViewColumnFormatLeft,
                    Width = column.PreferredWidth,
                    Text = text,
                };
                if (SendMessageColumn(
                        listView,
                        ListViewMessageInsertColumnW,
                        (nuint)index,
                        ref nativeColumn) == -1)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        return true;
    }

    private static bool AddRows(
        nint listView,
        IReadOnlyList<GenesisChannelItem> channels)
    {
        for (var row = 0; row < channels.Count; row++)
        {
            var channel = channels[row];
            var values = GenesisChannelListPresentation.CreateRowValues(channel);

            if (!InsertItem(listView, row, values[0]))
            {
                return false;
            }

            for (var column = 1; column < values.Length; column++)
            {
                if (!SetItemText(listView, row, column, values[column]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool AddRows(
        nint listView,
        IReadOnlyList<string[]> rowValues)
    {
        for (var row = 0; row < rowValues.Count; row++)
        {
            var values = rowValues[row];
            if (values.Length == 0 || !InsertItem(listView, row, values[0]))
            {
                return false;
            }

            for (var column = 1; column < values.Length; column++)
            {
                if (!SetItemText(listView, row, column, values[column]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool InsertItem(nint listView, int row, string value)
    {
        var text = Marshal.StringToHGlobalUni(value);
        try
        {
            var item = new ListViewItem
            {
                Mask = ListViewItemMaskText,
                Item = row,
                SubItem = 0,
                Text = text,
            };
            return SendMessageItem(
                listView,
                ListViewMessageInsertItemW,
                0,
                ref item) != -1;
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    private static bool SetItemText(
        nint listView,
        int row,
        int column,
        string value)
    {
        var text = Marshal.StringToHGlobalUni(value);
        try
        {
            var item = new ListViewItem
            {
                SubItem = column,
                Text = text,
            };
            return IsSuccessfulItemTextUpdate(SendMessageItem(
                listView,
                ListViewMessageSetItemTextW,
                (nuint)row,
                ref item));
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    private static void ApplyDefaultFont(IEnumerable<nint> controls)
    {
        foreach (var control in controls)
        {
            _ = GenesisWindowUiFont.Apply(control);
        }
    }

    private static void CloseControls(IEnumerable<nint> controls)
    {
        foreach (var control in controls.Reverse())
        {
            if (control != 0)
            {
                _ = PostMessageW(control, WindowMessageClose, 0, 0);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ListViewColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public nint Text;
        public int MaximumTextCharacters;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinimumWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int MaximumTextCharacters;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public nint ColumnIndices;
        public nint ColumnFormats;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControls
    {
        public uint Size;
        public uint Classes;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern int SendMessageColumn(
        nint window,
        uint message,
        nuint wParam,
        ref ListViewColumn column);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern int SendMessageItem(
        nint window,
        uint message,
        nuint wParam,
        ref ListViewItem item);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitCommonControlsEx(ref InitCommonControls controls);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

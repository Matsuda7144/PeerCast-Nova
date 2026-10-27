using System.Runtime.InteropServices;
using System.Text;

namespace PeerCast.Nova.App;

internal enum GenesisChannelContextMenuCommand
{
    None = 0,
    Play = 1,
    CopySource = 2,
    Refresh = 3,
    Favorite = 4,
    ManualSourceUrl = 5,
}

internal static class GenesisChannelContextMenuNative
{
    private const uint ListViewMessageFirst = 0x1000;
    private const uint ListViewMessageGetNextItem = ListViewMessageFirst + 12;
    private const uint ListViewMessageGetItemRect = ListViewMessageFirst + 14;
    private const uint ListViewMessageHitTest = ListViewMessageFirst + 18;
    private const int ListViewNextItemSelected = 0x0002;
    private const uint TrackPopupMenuReturnCommand = 0x0100;
    private const uint TrackPopupMenuRightButton = 0x0002;
    private const uint MenuFlagString = 0x0000;
    private const uint MenuFlagSeparator = 0x0800;
    private const uint GlobalMemoryMoveable = 0x0002;
    private const uint ClipboardFormatUnicodeText = 13;

    public static int GetRowAtCursor(nint listView)
    {
        if (listView == 0)
        {
            GenesisChannelContextMenuDiagnostics.Write("hit-test", "invalid-list-view");
            return -1;
        }

        if (!GetCursorPos(out var cursor))
        {
            GenesisChannelContextMenuDiagnostics.Write(
                "hit-test",
                $"cursor-failed error={Marshal.GetLastWin32Error()}");
            return -1;
        }

        var clientPoint = cursor;
        if (!ScreenToClient(listView, ref clientPoint))
        {
            GenesisChannelContextMenuDiagnostics.Write(
                "hit-test",
                $"screen-to-client-failed error={Marshal.GetLastWin32Error()}");
            return -1;
        }

        var hit = new ListViewHitTestInfo
        {
            Point = clientPoint,
        };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewHitTestInfo>());
        try
        {
            Marshal.StructureToPtr(hit, buffer, fDeleteOld: false);
            var result = SendMessageW(listView, ListViewMessageHitTest, 0, buffer);
            if (result.ToInt32() < 0)
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "hit-test",
                    $"miss result={result.ToInt32()} x={clientPoint.X} y={clientPoint.Y}");
                return -1;
            }

            hit = Marshal.PtrToStructure<ListViewHitTestInfo>(buffer);
            GenesisChannelContextMenuDiagnostics.Write(
                "hit-test",
                $"row={hit.Item} sub={hit.SubItem} flags=0x{hit.Flags:X} x={clientPoint.X} y={clientPoint.Y}");
            return hit.Item;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static GenesisChannelContextMenuCommand Show(nint owner) =>
        Show(owner, canPlay: true, favoriteText: null);

    internal static GenesisChannelContextMenuCommand Show(
        nint owner,
        bool canPlay,
        string? favoriteText)
    {
        if (owner == 0)
        {
            GenesisChannelContextMenuDiagnostics.Write("show", "invalid-owner");
            return GenesisChannelContextMenuCommand.None;
        }

        if (!GetCursorPos(out var cursor))
        {
            GenesisChannelContextMenuDiagnostics.Write(
                "show",
                $"cursor-failed error={Marshal.GetLastWin32Error()}");
            return FailMenuOpen(owner);
        }

        GenesisChannelContextMenuDiagnostics.Write(
            "show",
            $"owner=0x{owner:X} canPlay={canPlay} favorite={(!string.IsNullOrWhiteSpace(favoriteText))} x={cursor.X} y={cursor.Y}");
        return ShowAt(owner, cursor, canPlay, favoriteText);
    }

    public static GenesisChannelContextMenuCommand ShowForRow(
        nint owner,
        nint listView,
        int row)
    {
        if (owner == 0 || listView == 0 || row < 0)
        {
            return GenesisChannelContextMenuCommand.None;
        }

        return ShowResolvedRow(
            owner,
            listView,
            ResolveKeyboardMenuRow(listView, row));
    }

    internal static GenesisChannelContextMenuCommand ShowResolvedRow(
        nint owner,
        nint listView,
        int row) =>
        ShowResolvedRow(
            owner,
            listView,
            row,
            canPlay: true,
            favoriteText: null);

    internal static GenesisChannelContextMenuCommand ShowResolvedRow(
        nint owner,
        nint listView,
        int row,
        bool canPlay,
        string? favoriteText)
    {
        if (owner == 0 || listView == 0 || row < 0)
        {
            return GenesisChannelContextMenuCommand.None;
        }

        var rectangle = new WindowRectangle();
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WindowRectangle>());
        try
        {
            Marshal.StructureToPtr(rectangle, buffer, fDeleteOld: false);
            if (SendMessageW(
                    listView,
                    ListViewMessageGetItemRect,
                    (nuint)row,
                    buffer) == 0)
            {
                return FailMenuOpen(owner);
            }

            rectangle = Marshal.PtrToStructure<WindowRectangle>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var anchor = new WindowPoint
        {
            X = rectangle.Left,
            Y = rectangle.Bottom,
        };
        if (!ClientToScreen(listView, ref anchor))
        {
            return FailMenuOpen(owner);
        }

        return ShowAt(owner, anchor, canPlay, favoriteText);
    }

    internal static int ResolveKeyboardMenuRow(
        nint listView,
        int requestedRow) =>
        ResolveKeyboardMenuRow(listView, requestedRow, GetSelectedRow);

    internal static int ResolveKeyboardMenuRow(
        nint listView,
        int requestedRow,
        Func<nint, int> getSelectedRow)
    {
        ArgumentNullException.ThrowIfNull(getSelectedRow);
        if (listView == 0 || requestedRow < 0)
        {
            return -1;
        }

        var selectedRow = getSelectedRow(listView);
        return selectedRow >= 0 ? selectedRow : requestedRow;
    }

    public static bool CopySourceToClipboard(nint owner, string source, Func<bool>? canCopy = null)
    {
        if (owner == 0 || string.IsNullOrWhiteSpace(source) || (canCopy is not null && !canCopy()))
        {
            return false;
        }

        var bytes = Encoding.Unicode.GetBytes(source + '\0');
        var memory = GlobalAlloc(GlobalMemoryMoveable, (nuint)bytes.Length);
        if (memory == 0)
        {
            SystemGenesisGuiNextChannelContextFeedback.ReportClipboardCopyFailure(owner);
            return false;
        }

        var transferred = false;
        try
        {
            var destination = GlobalLock(memory);
            if (destination == 0)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            if (!OpenClipboard(owner))
            {
                return false;
            }

            try
            {
                if ((canCopy is not null && !canCopy()) || !EmptyClipboard())
                {
                    return false;
                }

                if (canCopy is not null && !canCopy()) return false;
                transferred = SetClipboardData(ClipboardFormatUnicodeText, memory) != 0;
                return transferred;
            }
            finally
            {
                _ = CloseClipboard();
            }
        }
        finally
        {
            if (!transferred)
            {
                _ = GlobalFree(memory);
                SystemGenesisGuiNextChannelContextFeedback.ReportClipboardCopyFailure(owner);
            }
        }
    }

    private static int GetSelectedRow(nint listView) =>
        SendMessageW(
            listView,
            ListViewMessageGetNextItem,
            nuint.MaxValue,
            (nint)ListViewNextItemSelected).ToInt32();

    private static GenesisChannelContextMenuCommand ShowAt(
        nint owner,
        WindowPoint anchor,
        bool canPlay,
        string? favoriteText)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            GenesisChannelContextMenuDiagnostics.Write(
                "popup",
                $"create-failed error={Marshal.GetLastWin32Error()}");
            return FailMenuOpen(owner);
        }

        try
        {
            if ((canPlay &&
                    !AppendMenuW(
                        menu,
                        MenuFlagString,
                        (nuint)GenesisChannelContextMenuCommand.Play,
                        "Play\tEnter")) ||
                !AppendMenuW(
                    menu,
                    MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.CopySource,
                    "Copy Source\tCtrl+C"))
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "popup",
                    $"append-primary-failed error={Marshal.GetLastWin32Error()}");
                return FailMenuOpen(owner);
            }

            if (!string.IsNullOrWhiteSpace(favoriteText) &&
                !AppendMenuW(
                    menu,
                    MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.Favorite,
                    favoriteText))
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "popup",
                    $"append-favorite-failed error={Marshal.GetLastWin32Error()}");
                return FailMenuOpen(owner);
            }

            if (!AppendMenuW(
                    menu,
                    MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.Refresh,
                    "Refresh\tF5"))
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "popup",
                    $"append-refresh-failed error={Marshal.GetLastWin32Error()}");
                return FailMenuOpen(owner);
            }

            if (!AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty) ||
                !AppendMenuW(
                    menu,
                    MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.ManualSourceUrl,
                    "URLを指定して再生…"))
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "popup",
                    $"append-manual-source-failed error={Marshal.GetLastWin32Error()}");
                return FailMenuOpen(owner);
            }

            GenesisChannelContextMenuDiagnostics.Write(
                "popup",
                $"track-enter owner=0x{owner:X} x={anchor.X} y={anchor.Y} canPlay={canPlay}");
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var command = TrackPopupMenuEx(
                menu,
                TrackPopupMenuReturnCommand | TrackPopupMenuRightButton,
                anchor.X,
                anchor.Y,
                owner,
                0);
            var lastError = Marshal.GetLastWin32Error();
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            GenesisChannelContextMenuDiagnostics.Write(
                "popup",
                $"track-exit raw={command} elapsedMs={elapsed.TotalMilliseconds:F1} error={lastError}");
            return Enum.IsDefined(typeof(GenesisChannelContextMenuCommand), (int)command)
                ? (GenesisChannelContextMenuCommand)command
                : GenesisChannelContextMenuCommand.None;
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static GenesisChannelContextMenuCommand FailMenuOpen(nint owner)
    {
        SystemGenesisGuiNextChannelContextFeedback.ReportMenuOpenFailure(owner);
        return GenesisChannelContextMenuCommand.None;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public int X;
        public int Y;
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
    private struct ListViewHitTestInfo
    {
        public WindowPoint Point;
        public uint Flags;
        public int Item;
        public int SubItem;
        public int Group;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out WindowPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref WindowPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref WindowPoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(
        nint menu,
        uint flags,
        nuint identifier,
        string text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        nint menu,
        uint flags,
        int x,
        int y,
        nint owner,
        nint parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}

using System.Runtime.InteropServices;
using System.Text;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal enum GenesisChannelContextMenuCommand
{
    None = 0,
    Play = 1,
    CopySource = 2,
    Refresh = 3,
    Favorite = 4,
    ManualSourceUrl = 5,
    OpenContact = 6,
    CopyContact = 7,
    Recording = 8,
    OpenRecordingDirectory = 9,
    CopyDetails = 10,
    PlayInNewWindow = 11,
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
    private const uint MenuFlagGrayed = 0x0001;
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

        var hit = new Win32ListViewHitTest
        {
            Point = clientPoint,
        };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<Win32ListViewHitTest>());
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

            hit = Marshal.PtrToStructure<Win32ListViewHitTest>(buffer);
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
        string? favoriteText,
        GenesisChannelContextTarget? actions = null)
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
        return ShowAt(owner, cursor, canPlay, favoriteText, actions);
    }

    internal static GenesisRecordingFileContextCommand ShowRecordingFile(
        nint owner,
        bool fileExists)
    {
        if (owner == 0 || !GetCursorPos(out var cursor))
        {
            return GenesisRecordingFileContextCommand.None;
        }

        return ShowRecordingFileAt(owner, cursor, fileExists);
    }

    internal static GenesisRecordingFileContextCommand ShowRecordingFileForRow(
        nint owner,
        nint listView,
        int row,
        bool fileExists)
    {
        if (owner == 0 || listView == 0 || row < 0)
        {
            return GenesisRecordingFileContextCommand.None;
        }

        return TryGetRowAnchor(listView, row, out var anchor)
            ? ShowRecordingFileAt(owner, anchor, fileExists)
            : GenesisRecordingFileContextCommand.None;
    }

    internal static GenesisChannelContextMenuCommand ShowEmptyList(
        nint owner,
        bool canRefresh)
    {
        if (owner == 0 || !GetCursorPos(out var cursor))
        {
            return GenesisChannelContextMenuCommand.None;
        }

        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return FailMenuOpen(owner);
        }

        try
        {
            if (!AppendMenuW(
                    menu,
                    canRefresh ? MenuFlagString : MenuFlagGrayed,
                    (nuint)GenesisChannelContextMenuCommand.Refresh,
                    "更新\tF5") ||
                !AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty) ||
                !AppendMenuW(
                    menu,
                    MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.ManualSourceUrl,
                    "URLを指定して再生…"))
            {
                return FailMenuOpen(owner);
            }

            var selected = TrackPopupMenuEx(
                menu,
                TrackPopupMenuReturnCommand | TrackPopupMenuRightButton,
                cursor.X,
                cursor.Y,
                owner,
                0);
            return selected switch
            {
                (int)GenesisChannelContextMenuCommand.Refresh => GenesisChannelContextMenuCommand.Refresh,
                (int)GenesisChannelContextMenuCommand.ManualSourceUrl => GenesisChannelContextMenuCommand.ManualSourceUrl,
                _ => GenesisChannelContextMenuCommand.None,
            };
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
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
        string? favoriteText,
        GenesisChannelContextTarget? actions = null)
    {
        if (owner == 0 || listView == 0 || row < 0)
        {
            return GenesisChannelContextMenuCommand.None;
        }

        if (!TryGetRowAnchor(listView, row, out var anchor))
        {
            return FailMenuOpen(owner);
        }

        return ShowAt(owner, anchor, canPlay, favoriteText, actions);
    }

    private static bool TryGetRowAnchor(nint listView, int row, out Win32Point anchor)
    {
        anchor = default;
        var rectangle = new Win32Rect();
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<Win32Rect>());
        try
        {
            Marshal.StructureToPtr(rectangle, buffer, fDeleteOld: false);
            if (SendMessageW(
                    listView,
                    ListViewMessageGetItemRect,
                    (nuint)row,
                    buffer) == 0)
            {
                return false;
            }

            rectangle = Marshal.PtrToStructure<Win32Rect>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        anchor = new Win32Point
        {
            X = rectangle.Left,
            Y = rectangle.Bottom,
        };
        return ClientToScreen(listView, ref anchor);
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

    public static bool CopySourceToClipboard(nint owner, string source, Func<bool>? canCopy = null,
        Action? reportFailure = null)
    {
        if (owner == 0 || string.IsNullOrWhiteSpace(source) || (canCopy is not null && !canCopy()))
        {
            return false;
        }

        var bytes = Encoding.Unicode.GetBytes(source + '\0');
        var memory = GlobalAlloc(GlobalMemoryMoveable, (nuint)bytes.Length);
        if (memory == 0)
        {
            ReportFailure();
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
                ReportFailure();
            }
        }

        void ReportFailure()
        {
            if (reportFailure is not null) reportFailure();
            else SystemGenesisGuiNextChannelContextFeedback.ReportClipboardCopyFailure(owner);
        }
    }

    private static int GetSelectedRow(nint listView) =>
        SendMessageW(
            listView,
            ListViewMessageGetNextItem,
            nuint.MaxValue,
            (nint)ListViewNextItemSelected).ToInt32();

    private static GenesisRecordingFileContextCommand ShowRecordingFileAt(
        nint owner,
        Win32Point anchor,
        bool fileExists)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return GenesisRecordingFileContextCommand.None;
        }

        try
        {
            var availableFlag = fileExists ? MenuFlagString : MenuFlagGrayed;
            if (!AppendMenuW(
                    menu,
                    availableFlag,
                    (nuint)GenesisRecordingFileContextCommand.PlayFile,
                    "既定のプレイヤーで再生") ||
                !AppendMenuW(
                    menu,
                    availableFlag,
                    (nuint)GenesisRecordingFileContextCommand.RevealInExplorer,
                    "エクスプローラーで表示") ||
                !AppendMenuW(
                    menu,
                    MenuFlagString,
                    (nuint)GenesisRecordingFileContextCommand.CopyPath,
                    "ファイルパスをコピー") ||
                !AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty) ||
                !AppendMenuW(
                    menu,
                    availableFlag,
                    (nuint)GenesisRecordingFileContextCommand.DeleteFile,
                    "ファイルを削除…"))
            {
                return GenesisRecordingFileContextCommand.None;
            }

            var selected = TrackPopupMenuEx(
                menu,
                TrackPopupMenuReturnCommand | TrackPopupMenuRightButton,
                anchor.X,
                anchor.Y,
                owner,
                0);
            return Enum.IsDefined(typeof(GenesisRecordingFileContextCommand), (int)selected)
                ? (GenesisRecordingFileContextCommand)selected
                : GenesisRecordingFileContextCommand.None;
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static GenesisChannelContextMenuCommand ShowAt(
        nint owner,
        Win32Point anchor,
        bool canPlay,
        string? favoriteText,
        GenesisChannelContextTarget? actions)
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
                    (!AppendMenuW(
                        menu,
                        MenuFlagString,
                        (nuint)GenesisChannelContextMenuCommand.Play,
                        "再生\tEnter") ||
                     !AppendMenuW(
                        menu,
                        MenuFlagString,
                        (nuint)GenesisChannelContextMenuCommand.PlayInNewWindow,
                        "新しいウィンドウで再生"))) ||
                !AppendMenuW(menu, actions?.CanRecord == true ? MenuFlagString : MenuFlagGrayed,
                    (nuint)GenesisChannelContextMenuCommand.Recording, actions?.RecordingLabel ?? "録画開始") ||
                !AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty) ||
                !AppendMenuW(menu, actions?.ContactUrl is not null ? MenuFlagString : MenuFlagGrayed,
                    (nuint)GenesisChannelContextMenuCommand.OpenContact, "コンタクトURLを開く") ||
                !AppendMenuW(menu, actions?.ContactUrl is not null ? MenuFlagString : MenuFlagGrayed,
                    (nuint)GenesisChannelContextMenuCommand.CopyContact, "コンタクトURLをコピー") ||
                !AppendMenuW(
                    menu,
                    actions is not null && string.IsNullOrWhiteSpace(actions.SourceToCopy) ? MenuFlagGrayed : MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.CopySource,
                    "ソースURLをコピー\tCtrl+C") ||
                (actions?.DetailsText is { Length: > 0 } && !AppendMenuW(menu, MenuFlagString,
                    (nuint)GenesisChannelContextMenuCommand.CopyDetails, "情報をコピー")) ||
                !AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty))
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
                    "更新\tF5"))
            {
                GenesisChannelContextMenuDiagnostics.Write(
                    "popup",
                    $"append-refresh-failed error={Marshal.GetLastWin32Error()}");
                return FailMenuOpen(owner);
            }

            if (!AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty) ||
                !AppendMenuW(menu, actions?.RecordingDirectory is not null ? MenuFlagString : MenuFlagGrayed,
                    (nuint)GenesisChannelContextMenuCommand.OpenRecordingDirectory, "録画フォルダを開く") ||
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
}

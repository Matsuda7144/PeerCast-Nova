using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;
using NotifyIconData = PeerCast.Nova.App.Win32NativeMethods.Win32NotifyIconData;

namespace PeerCast.Nova.App;

internal sealed class GenesisSystemTrayPolicy
{
    private const uint WindowMessageSize = 0x0005;
    private const uint WindowMessageSystemCommand = 0x0112;
    private const nuint SystemCommandMask = 0xFFF0;
    private const nuint SystemCommandMinimize = 0xF020;
    private const nuint SizeMinimized = 1;

    public bool CloseToTrayEnabled { get; set; }
    public bool MinimizeToTrayEnabled { get; set; } = true;
    public bool IsExitRequested { get; private set; }
    public bool HasShownFirstHideNotice { get; private set; }

    public bool ShouldHideOnClose() => CloseToTrayEnabled && !IsExitRequested;

    public bool ShouldHideOnMinimize() => MinimizeToTrayEnabled && !IsExitRequested;

    public static bool IsMinimizeCommand(uint message, nuint wParam) =>
        message == WindowMessageSystemCommand && (wParam & SystemCommandMask) == SystemCommandMinimize;

    public static bool IsMinimizedSize(uint message, nuint wParam) =>
        message == WindowMessageSize && wParam == SizeMinimized;

    public bool TryConsumeFirstHideNotice()
    {
        if (HasShownFirstHideNotice)
        {
            return false;
        }

        HasShownFirstHideNotice = true;
        return true;
    }

    public void AllowExit() => IsExitRequested = true;

    public static string ResolveToggleVisibilityLabel(bool isWindowVisible) =>
        isWindowVisible ? "トレイに格納" : "PeerCast Nova を表示";
}

internal sealed class GenesisSystemTrayBridge : IAsyncDisposable
{
    private const uint TrayIconId = 7145;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageQueryEndSession = 0x0011;
    private const uint WindowMessageEndSession = 0x0016;
    private const uint WindowMessageContextMenu = 0x007B;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageLeftButtonDoubleClick = 0x0203;
    private const uint WindowMessageRightButtonUp = 0x0205;
    private const uint WindowMessageTrayCallback = 0x8000 + 0x745;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconFlagMessage = 0x00000001;
    private const uint NotifyIconFlagIcon = 0x00000002;
    private const uint NotifyIconFlagTip = 0x00000004;
    private const uint NotifyIconFlagInfo = 0x00000010;
    private const uint NotifyIconInfo = 0x00000001;
    private const uint NotifyIconNoSound = 0x00000010;
    private const uint NotifyIconRespectQuietTime = 0x00000080;
    private const int ApplicationIconResourceId = 32512;
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const int ShowWindowRestore = 9;
    private const uint MenuFlagString = 0x0000;
    private const uint MenuFlagGrayed = 0x0001;
    private const uint MenuFlagSeparator = 0x0800;
    private const uint TrackPopupReturnCommand = 0x0100;
    private const uint TrackPopupRightButton = 0x0002;
    private const uint MenuCmdToggleVisibility = 1;
    private const uint MenuCmdRefresh = 2;
    private const uint MenuCmdExit = 3;
    private const nuint TraySubclassId = 0x4E545259; // NTRY
    private static readonly uint TaskbarCreatedMessage = RegisterTaskbarCreatedMessage();
    private static readonly ConcurrentDictionary<nint, GenesisSystemTrayBridge> BridgesByWindow = new();
    private static readonly Win32SubclassProcedure SubclassDelegate = ProcessWindowMessage;

    private readonly nint _window;
    private readonly nint _icon;
    private readonly GenesisSystemTrayPolicy _policy = new();
    private bool _iconAdded;
    private int _disposed;

    private GenesisSystemTrayBridge(nint window, nint icon)
    {
        _window = window;
        _icon = icon;
    }

    public static GenesisSystemTrayBridge? TryCreate(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == 0 || !IsWindow(window))
        {
            return null;
        }

        var icon = LoadIconW(0, (nint)ApplicationIconResourceId);
        if (icon == 0)
        {
            return null;
        }

        var bridge = new GenesisSystemTrayBridge(window, icon);
        if (!BridgesByWindow.TryAdd(window, bridge))
        {
            return null;
        }

        if (!bridge.AddTrayIcon())
        {
            _ = BridgesByWindow.TryRemove(window, out _);
            return null;
        }

        if (!SetWindowSubclass(window, SubclassDelegate, TraySubclassId, 0))
        {
            bridge.DeleteTrayIcon();
            _ = BridgesByWindow.TryRemove(window, out _);
            return null;
        }

        return bridge;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _ = BridgesByWindow.TryRemove(_window, out _);
            DeleteTrayIcon();
            if (_window != 0 && IsWindow(_window))
            {
                _ = RemoveWindowSubclass(_window, SubclassDelegate, TraySubclassId);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static uint RegisterTaskbarCreatedMessage() =>
        OperatingSystem.IsWindows() ? RegisterWindowMessageW("TaskbarCreated") : 0;

    private bool AddTrayIcon()
    {
        var data = CreateBaseData();
        data.Flags = NotifyIconFlagMessage | NotifyIconFlagIcon | NotifyIconFlagTip;
        if (!Shell_NotifyIconW(NotifyIconAdd, ref data))
        {
            return false;
        }

        _iconAdded = true;
        return true;
    }

    private void DeleteTrayIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _window,
            Id = TrayIconId,
        };
        _ = Shell_NotifyIconW(NotifyIconDelete, ref data);
        _iconAdded = false;
    }

    private void ShowFirstHideBalloon()
    {
        var data = CreateBaseData();
        data.Flags = NotifyIconFlagInfo;
        data.InfoTitle = "PeerCast Nova";
        data.Info = "タスクトレイに格納しました。トレイアイコンをクリックすると再表示されます。";
        data.InfoFlags = NotifyIconInfo | NotifyIconNoSound | NotifyIconRespectQuietTime;
        _ = Shell_NotifyIconW(NotifyIconModify, ref data);
    }

    private void HideToTray()
    {
        _ = ShowWindow(_window, ShowWindowHide);
        if (_policy.TryConsumeFirstHideNotice())
        {
            ShowFirstHideBalloon();
        }
    }

    private NotifyIconData CreateBaseData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = TrayIconId,
        CallbackMessage = WindowMessageTrayCallback,
        Icon = _icon,
        ToolTip = "PeerCast Nova",
    };

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        _ = referenceData;
        if (message == WindowMessageNcDestroy)
        {
            if (BridgesByWindow.TryRemove(window, out var removed))
            {
                removed.DeleteTrayIcon();
            }

            _ = RemoveWindowSubclass(window, SubclassDelegate, subclassId);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        if (!BridgesByWindow.TryGetValue(window, out var bridge) ||
            Volatile.Read(ref bridge._disposed) != 0)
        {
            return DefSubclassProc(window, message, wParam, lParam);
        }

        if (message is WindowMessageQueryEndSession or WindowMessageEndSession)
        {
            bridge._policy.AllowExit();
            return DefSubclassProc(window, message, wParam, lParam);
        }

        if (message == WindowMessageClose && bridge._policy.ShouldHideOnClose())
        {
            bridge.HideToTray();
            return 0;
        }

        if ((GenesisSystemTrayPolicy.IsMinimizeCommand(message, wParam) ||
             GenesisSystemTrayPolicy.IsMinimizedSize(message, wParam)) &&
            bridge._policy.ShouldHideOnMinimize())
        {
            bridge.HideToTray();
            if (GenesisSystemTrayPolicy.IsMinimizeCommand(message, wParam))
            {
                return 0;
            }
        }

        if (message == WindowMessageTrayCallback && (uint)wParam == TrayIconId)
        {
            var eventCode = (uint)(lParam.ToInt64() & 0xFFFF);
            if (eventCode is WindowMessageLeftButtonUp or WindowMessageLeftButtonDoubleClick)
            {
                bridge.RestoreMainWindow();
                return 0;
            }

            if (eventCode is WindowMessageRightButtonUp or WindowMessageContextMenu)
            {
                bridge.ShowTrayContextMenu();
                return 0;
            }
        }

        if (TaskbarCreatedMessage != 0 && message == TaskbarCreatedMessage)
        {
            _ = bridge.AddTrayIcon();
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void RestoreMainWindow()
    {
        _ = ShowWindow(_window, IsIconic(_window) ? ShowWindowRestore : ShowWindowShow);
        _ = SetForegroundWindow(_window);
    }

    private void ShowTrayContextMenu()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            var visible = IsWindowVisible(_window);
            var canRefresh = GenesisChannelRefreshButtonBridge.CanRefreshForOwner(_window);
            if (!AppendMenuW(menu, MenuFlagString, MenuCmdToggleVisibility, GenesisSystemTrayPolicy.ResolveToggleVisibilityLabel(visible)) ||
                !AppendMenuW(menu, canRefresh ? MenuFlagString : MenuFlagGrayed, MenuCmdRefresh, "チャンネル一覧を更新") ||
                !AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty) ||
                !AppendMenuW(menu, MenuFlagString, MenuCmdExit, "終了"))
            {
                return;
            }

            _ = SetForegroundWindow(_window);
            var selected = TrackPopupMenuEx(
                menu,
                TrackPopupReturnCommand | TrackPopupRightButton,
                cursor.X,
                cursor.Y,
                _window,
                0);
            switch (selected)
            {
                case MenuCmdToggleVisibility:
                    if (visible)
                    {
                        HideToTray();
                    }
                    else
                    {
                        RestoreMainWindow();
                    }

                    break;
                case MenuCmdRefresh:
                    _ = GenesisChannelRefreshButtonBridge.TryRefreshForOwner(_window);
                    break;
                case MenuCmdExit:
                    _policy.AllowExit();
                    _ = PostMessageW(_window, WindowMessageClose, 0, 0);
                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }
}

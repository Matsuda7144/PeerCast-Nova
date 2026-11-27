using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed class GenesisGuiNextMainWindowKeyboardNavigation : IDisposable
{
    private const int HookGetMessage = 3;
    private const nuint PeekMessageRemove = 1;
    private const uint WindowMessageNull = 0x0000;
    private const uint WindowMessageKeyDown = 0x0100;
    private const nuint VirtualKeyTab = 0x09;
    private const nuint VirtualKeyF = 0x46;
    private const nuint VirtualKeyF5 = 0x74;
    private const nuint VirtualKeyF6 = 0x75;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyAlt = 0x12;
    private const int WindowLongStyle = -16;
    private const uint WindowStyleTabStop = 0x00010000;
    private readonly nint _owner;
    private readonly Win32HookProcedure _hookProcedure;
    private nint _hook;
    private int _disposed;

    private GenesisGuiNextMainWindowKeyboardNavigation(nint owner)
    {
        _owner = owner;
        _hookProcedure = ProcessHook;
    }

    public static GenesisGuiNextMainWindowKeyboardNavigation? Open(nint owner)
    {
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner))
        {
            return null;
        }

        var currentThreadId = GetCurrentThreadId();
        if (currentThreadId == 0 || GetWindowThreadProcessId(owner, out _) != currentThreadId)
        {
            return null;
        }

        var navigation = new GenesisGuiNextMainWindowKeyboardNavigation(owner);
        navigation._hook = SetWindowsHookExW(
            HookGetMessage,
            navigation._hookProcedure,
            0,
            currentThreadId);
        if (navigation._hook == 0)
        {
            return null;
        }

        return navigation;
    }

    internal static bool IsRemovedTabKeyDown(
        int hookCode,
        nuint removalAction,
        uint message,
        nuint virtualKey) =>
        hookCode >= 0 &&
        removalAction == PeekMessageRemove &&
        message == WindowMessageKeyDown &&
        virtualKey == VirtualKeyTab;

    internal static bool TryHandleRefreshKeyDown(
        int hookCode,
        nuint removalAction,
        uint message,
        nuint virtualKey,
        nint keyData,
        bool isActiveOwnerTarget,
        bool modifiersPressed,
        Func<bool>? requestRefresh)
    {
        if (hookCode < 0 ||
            removalAction != PeekMessageRemove ||
            message != WindowMessageKeyDown ||
            virtualKey != VirtualKeyF5 ||
            !isActiveOwnerTarget ||
            modifiersPressed ||
            requestRefresh is null)
        {
            return false;
        }

        // Only a registered route owns this gesture. Consume repeats and
        // rejected requests so child handlers cannot dispatch it again.
        if ((keyData.ToInt64() & (1L << 30)) == 0)
        {
            _ = requestRefresh();
        }

        return true;
    }

    internal static bool TryHandlePaneJumpKeyDown(
        int hookCode,
        nuint removalAction,
        uint message,
        nuint virtualKey,
        nint keyData,
        bool isActiveOwnerTarget,
        bool ctrlOrAltPressed,
        bool shiftPressed,
        Func<bool, bool>? jumpPane)
    {
        if (hookCode < 0 ||
            removalAction != PeekMessageRemove ||
            message != WindowMessageKeyDown ||
            virtualKey != VirtualKeyF6 ||
            !isActiveOwnerTarget ||
            ctrlOrAltPressed ||
            jumpPane is null)
        {
            return false;
        }

        if ((keyData.ToInt64() & (1L << 30)) != 0)
        {
            return true;
        }

        return jumpPane(shiftPressed);
    }

    internal static bool TryHandleSearchShortcutKeyDown(
        int hookCode,
        nuint removalAction,
        uint message,
        nuint virtualKey,
        nint keyData,
        bool isActiveOwnerTarget,
        bool ctrlPressed,
        bool shiftOrAltPressed,
        Func<bool>? focusSearch)
    {
        if (hookCode < 0 ||
            removalAction != PeekMessageRemove ||
            message != WindowMessageKeyDown ||
            virtualKey != VirtualKeyF ||
            !isActiveOwnerTarget ||
            !ctrlPressed ||
            shiftOrAltPressed ||
            focusSearch is null)
        {
            return false;
        }

        if ((keyData.ToInt64() & (1L << 30)) != 0)
        {
            return true;
        }

        return focusSearch();
    }

    internal static int ResolvePaneJumpIndex(
        IReadOnlyList<int> regions,
        int currentIndex,
        bool reverse)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count < 2)
        {
            return -1;
        }

        var regionStarts = new List<int>();
        var distinctRegions = new List<int>();
        for (var i = 0; i < regions.Count; i++)
        {
            if (distinctRegions.Count == 0 || distinctRegions[^1] != regions[i])
            {
                if (!distinctRegions.Contains(regions[i]))
                {
                    distinctRegions.Add(regions[i]);
                    regionStarts.Add(i);
                }
            }
        }

        if (regionStarts.Count < 2)
        {
            return -1;
        }

        var currentRegionSlot = -1;
        if (currentIndex >= 0 && currentIndex < regions.Count)
        {
            currentRegionSlot = distinctRegions.IndexOf(regions[currentIndex]);
        }

        var nextSlot = ResolveNextIndex(regionStarts.Count, currentRegionSlot, reverse);
        return nextSlot >= 0 ? regionStarts[nextSlot] : -1;
    }

    internal static int ResolveNextIndex(
        int count,
        int currentIndex,
        bool reverse)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (currentIndex < 0 || currentIndex >= count)
        {
            return reverse ? count - 1 : 0;
        }

        if (reverse)
        {
            return currentIndex == 0 ? count - 1 : currentIndex - 1;
        }

        return currentIndex == count - 1 ? 0 : currentIndex + 1;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var hook = _hook;
        _hook = 0;
        if (hook != 0)
        {
            _ = UnhookWindowsHookEx(hook);
        }
    }

    private nint ProcessHook(int code, nuint removalAction, nint messagePointer)
    {
        try
        {
            if (code >= 0 && Volatile.Read(ref _disposed) == 0 && messagePointer != 0)
            {
                var message = Marshal.PtrToStructure<Win32WindowMessage>(messagePointer);
                var refreshHandled = false;
                if (message.Message == WindowMessageKeyDown && message.WParam == VirtualKeyF5)
                {
                    refreshHandled = TryHandleRefreshKeyDown(
                        code,
                        removalAction,
                        message.Message,
                        message.WParam,
                        message.LParam,
                        IsMessageForOwner(message.Window) &&
                            IsWindowEnabled(_owner) &&
                            GetForegroundWindow() == _owner,
                        IsKeyPressed(VirtualKeyShift) ||
                            IsKeyPressed(VirtualKeyControl) ||
                            IsKeyPressed(VirtualKeyAlt),
                        GenesisChannelRefreshButtonBridge.FindRefreshRequestForOwner(_owner));
                }

                var isActiveTarget = message.Message == WindowMessageKeyDown &&
                    IsMessageForOwner(message.Window) &&
                    IsWindowEnabled(_owner) &&
                    GetForegroundWindow() == _owner;

                var searchShortcutHandled = message.Message == WindowMessageKeyDown &&
                    message.WParam == VirtualKeyF &&
                    TryHandleSearchShortcutKeyDown(
                        code,
                        removalAction,
                        message.Message,
                        message.WParam,
                        message.LParam,
                        isActiveTarget,
                        IsKeyPressed(VirtualKeyControl),
                        IsKeyPressed(VirtualKeyShift) || IsKeyPressed(VirtualKeyAlt),
                        () => GenesisGuiNextAppShellBridge.TryFocusSearchForOwner(_owner));

                var paneJumpHandled = message.Message == WindowMessageKeyDown &&
                    message.WParam == VirtualKeyF6 &&
                    TryHandlePaneJumpKeyDown(
                        code,
                        removalAction,
                        message.Message,
                        message.WParam,
                        message.LParam,
                        isActiveTarget,
                        IsKeyPressed(VirtualKeyControl) || IsKeyPressed(VirtualKeyAlt),
                        IsKeyPressed(VirtualKeyShift),
                        TryJumpPane);

                var inlineTabHandled = IsRemovedTabKeyDown(code, removalAction, message.Message, message.WParam) &&
                    IsMessageForOwner(message.Window) && GenesisGuiNextAppShellBridge.TryMovePublishingInlineTab(
                        _owner, GetFocus(), IsKeyPressed(VirtualKeyShift));
                if (refreshHandled || searchShortcutHandled || paneJumpHandled || inlineTabHandled || (IsRemovedTabKeyDown(
                        code,
                        removalAction,
                        message.Message,
                        message.WParam) &&
                    IsMessageForOwner(message.Window) &&
                    TryMoveFocus()))
                {
                    message.Message = WindowMessageNull;
                    message.WParam = 0;
                    message.LParam = 0;
                    Marshal.StructureToPtr(message, messagePointer, fDeleteOld: false);
                }
            }
        }
        catch
        {
            // Main-window keyboard navigation is auxiliary presentation. A hook
            // failure must not interrupt the host message loop.
        }

        return CallNextHookEx(0, code, removalAction, messagePointer);
    }

    private bool IsMessageForOwner(nint window) =>
        window != 0 &&
        (window == _owner || IsChild(_owner, window));

    private static bool IsKeyPressed(int virtualKey) =>
        (GetKeyState(virtualKey) & 0x8000) != 0;

    private bool TryMoveFocus()
    {
        var tabStops = CaptureTabStops();
        if (tabStops.Count == 0)
        {
            return false;
        }

        var focusedWindow = GetFocus();
        var currentIndex = -1;
        for (var index = 0; index < tabStops.Count; index++)
        {
            var candidate = tabStops[index].Window;
            if (candidate == focusedWindow ||
                (focusedWindow != 0 && IsChild(candidate, focusedWindow)))
            {
                currentIndex = index;
                break;
            }
        }

        var reverse = (GetKeyState(VirtualKeyShift) & 0x8000) != 0;
        var nextIndex = ResolveNextIndex(tabStops.Count, currentIndex, reverse);
        if (nextIndex < 0)
        {
            return false;
        }

        var target = tabStops[nextIndex].Window;
        var previous = SetFocus(target);
        return previous != 0 || GetFocus() == target;
    }

    private bool TryJumpPane(bool reverse)
    {
        var tabStops = CaptureTabStops();
        if (tabStops.Count < 2)
        {
            return false;
        }

        var focusedWindow = GetFocus();
        var currentIndex = -1;
        for (var index = 0; index < tabStops.Count; index++)
        {
            var candidate = tabStops[index].Window;
            if (candidate == focusedWindow ||
                (focusedWindow != 0 && IsChild(candidate, focusedWindow)))
            {
                currentIndex = index;
                break;
            }
        }

        var origin = default(Win32Point);
        _ = ClientToScreen(_owner, ref origin);
        _ = GetClientRect(_owner, out var ownerClient);
        var dpi = GenesisMainWindowResizeGuard.GetDpi(_owner);
        var sidebarSplitPixels = GenesisMainWindowLayout.Scale(
            GenesisMainWindowLayout.ContentLeft - 4,
            dpi);
        var topBarBottom = origin.Y + GenesisMainWindowLayout.Scale(GenesisGuiNextTopBarLayout.Height + 12, dpi);
        var detailsThreshold = origin.Y + ((ownerClient.Bottom - ownerClient.Top) * 2 / 3);

        var regions = new int[tabStops.Count];
        for (var i = 0; i < tabStops.Count; i++)
        {
            var stop = tabStops[i].Rectangle;
            if (stop.Right <= origin.X + sidebarSplitPixels)
            {
                regions[i] = 0; // 左サイドバー
            }
            else if (stop.Bottom <= topBarBottom)
            {
                regions[i] = 1; // 上部バー
            }
            else if (stop.Top >= detailsThreshold)
            {
                regions[i] = 3; // 下部詳細ペイン
            }
            else
            {
                regions[i] = 2; // 中央メイン領域
            }
        }

        var nextIndex = ResolvePaneJumpIndex(regions, currentIndex, reverse);
        if (nextIndex < 0)
        {
            return false;
        }

        var target = tabStops[nextIndex].Window;
        var previous = SetFocus(target);
        return previous != 0 || GetFocus() == target;
    }

    private List<TabStopWindow> CaptureTabStops()
    {
        var tabStops = new List<TabStopWindow>();
        Win32EnumWindowsProcedure callback = (window, _) =>
        {
            var style = unchecked((uint)GetWindowLongW(window, WindowLongStyle));
            if ((style & WindowStyleTabStop) != 0 &&
                IsWindowVisible(window) &&
                IsWindowEnabled(window) &&
                GetWindowRect(window, out var rectangle))
            {
                tabStops.Add(new(window, rectangle));
            }

            return true;
        };

        _ = EnumChildWindows(_owner, callback, 0);
        GC.KeepAlive(callback);

        var origin = default(Win32Point);
        _ = ClientToScreen(_owner, ref origin);
        var dpi = GenesisMainWindowResizeGuard.GetDpi(_owner);
        var sidebarSplitPixels = GenesisMainWindowLayout.Scale(
            GenesisMainWindowLayout.ContentLeft - 4,
            dpi);
        var rowBandHeight = Math.Max(1, GenesisMainWindowLayout.Scale(12, dpi));
        var bounds = new List<TabStopBounds>(tabStops.Count);
        foreach (var stop in tabStops)
        {
            bounds.Add(new(
                stop.Window,
                stop.Rectangle.Left,
                stop.Rectangle.Top,
                stop.Rectangle.Right,
                stop.Rectangle.Bottom));
        }

        SortTabStops(bounds, origin.X, origin.Y, sidebarSplitPixels, rowBandHeight);
        for (var i = 0; i < bounds.Count; i++)
        {
            var entry = bounds[i];
            tabStops[i] = new(
                entry.Window,
                new Win32Rect
                {
                    Left = entry.Left,
                    Top = entry.Top,
                    Right = entry.Right,
                    Bottom = entry.Bottom,
                });
        }

        return tabStops;
    }

    internal readonly record struct TabStopBounds(
        nint Window,
        int Left,
        int Top,
        int Right,
        int Bottom);

    internal static void SortTabStops(
        List<TabStopBounds> tabStops,
        int ownerClientLeft,
        int ownerClientTop,
        int sidebarSplitPixels,
        int rowBandHeight = 12)
    {
        ArgumentNullException.ThrowIfNull(tabStops);
        if (tabStops.Count < 2)
        {
            return;
        }

        var sidebarBoundary = ownerClientLeft + sidebarSplitPixels;
        var sidebarCount = 0;
        foreach (var stop in tabStops)
        {
            if (stop.Right <= sidebarBoundary)
            {
                sidebarCount++;
            }
        }

        // GUI Next 構成（左サイドバーのボタン群が 3 つ以上ある場合）では
        // 左サイドバーを先に巡回し、続けて右ペイン（上バー→一覧→詳細ボタン）を巡回する。
        var hasSidebar = sidebarSplitPixels > 0 && sidebarCount >= 3;
        var bandHeight = Math.Max(1, rowBandHeight);

        (int Region, int RowBand, int Left, int Top, int Bottom, int Right, long Window) KeyOf(
            TabStopBounds stop)
        {
            var inSidebar = hasSidebar && stop.Right <= sidebarBoundary;
            var region = inSidebar ? 0 : 1;
            var rowBand = inSidebar
                ? stop.Top
                : Math.Max(0, stop.Top - ownerClientTop) / bandHeight;
            return (region, rowBand, stop.Left, stop.Top, stop.Bottom, stop.Right, stop.Window.ToInt64());
        }

        tabStops.Sort((left, right) => KeyOf(left).CompareTo(KeyOf(right)));
    }

    private readonly record struct TabStopWindow(
        nint Window,
        Win32Rect Rectangle);
}

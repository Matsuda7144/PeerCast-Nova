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
    private const nuint VirtualKeyF5 = 0x74;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyAlt = 0x12;
    private const int WindowLongStyle = -16;
    private const uint WindowStyleTabStop = 0x00010000;
    private readonly nint _owner;
    private readonly HookProcedure _hookProcedure;
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
                var message = Marshal.PtrToStructure<NativeMessage>(messagePointer);
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

                var inlineTabHandled = IsRemovedTabKeyDown(code, removalAction, message.Message, message.WParam) &&
                    IsMessageForOwner(message.Window) && GenesisGuiNextAppShellBridge.TryMovePublishingInlineTab(
                        _owner, GetFocus(), IsKeyPressed(VirtualKeyShift));
                if (refreshHandled || inlineTabHandled || (IsRemovedTabKeyDown(
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

    private List<TabStopWindow> CaptureTabStops()
    {
        var tabStops = new List<TabStopWindow>();
        EnumWindowsProcedure callback = (window, _) =>
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

        var origin = default(NativePoint);
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
                new NativeRectangle
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
        NativeRectangle Rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint HookProcedure(int code, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsProcedure(nint window, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(
        int hookId,
        HookProcedure hookProcedure,
        nint module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook,
        int code,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        nint parent,
        EnumWindowsProcedure callback,
        nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

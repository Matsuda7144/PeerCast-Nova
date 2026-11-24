using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

/// <summary>
/// Nova.App 内で複数ウィンドウから重複宣言されていた基本的な Win32 P/Invoke を集約する共通モジュール。
/// </summary>
internal static class Win32NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int commandShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProcW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int objectIndex);
}

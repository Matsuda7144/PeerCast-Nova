using System.Runtime.InteropServices;

namespace PeerCast.Nova.Windows;

/// <summary>
/// Nova.Windows 内で複数ファイルから重複宣言されていた基本的な Win32 ウィンドウ・描画 P/Invoke を集約する共通モジュール。
/// </summary>
internal static class Win32WindowNativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
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
    internal struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintStruct
    {
        public nint DeviceContext;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Erase;
        public WindowRectangle PaintRect;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Restore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[]? Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public WindowPoint Point;
        public uint Private;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustWindowRectEx(
        ref WindowRectangle rectangle,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu,
        uint extendedStyle);

    [DllImport("user32.dll")]
    internal static extern int ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(
        nint window,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(
        nint window,
        nint rect,
        [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out WindowRectangle rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out WindowRectangle rect);

    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint window, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(nint window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    internal static extern int FillRect(nint hdc, ref WindowRectangle rect, nint brush);

    [DllImport("user32.dll")]
    internal static extern int FrameRect(nint hdc, ref WindowRectangle rect, nint brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawTextW(
        nint hdc,
        string text,
        int count,
        ref WindowRectangle rect,
        uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, uint message, nuint wParam, string lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessageW(
        out WindowMessage message,
        nint window,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref WindowMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessageW(ref WindowMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProcW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

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
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int objectIndex);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    internal static extern int SetTextColor(nint hdc, int color);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint ExtractIconExW(
        string fileName,
        int iconIndex,
        out nint largeIcon,
        out nint smallIcon,
        uint iconCount);
}

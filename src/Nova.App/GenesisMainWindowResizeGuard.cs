using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed class GenesisMainWindowResizeGuard : IAsyncDisposable
{
    internal const int WindowStyleThickFrame = 0x00040000;
    internal const int WindowStyleMaximizeBox = 0x00010000;
    private const int WindowStyleIndex = -16;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const nuint SubclassId = 0x4E52535A; // NRSZ; main surface only.
    private static readonly SubclassProcedure Procedure = ProcessMessage;
    private readonly nint _window;
    private readonly int _originalStyle;
    private readonly bool _styleChanged;
    private int _disposed;

    private GenesisMainWindowResizeGuard(
        nint window,
        int originalStyle,
        bool styleChanged)
    {
        _window = window;
        _originalStyle = originalStyle;
        _styleChanged = styleChanged;
    }

    public static IAsyncDisposable? Create(long windowToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NoopLifetime.Instance;
        }

        if (windowToken == 0)
        {
            return null;
        }

        return TryCreate((nint)windowToken);
    }

    internal static GenesisMainWindowResizeGuard? TryCreate(nint window)
    {
        if (window == 0 || !IsWindow(window))
        {
            return null;
        }

        var originalStyle = GetWindowLongW(window, WindowStyleIndex);
        if (originalStyle == 0)
        {
            return null;
        }

        var resizableStyle = ResolveResizableStyle(originalStyle);

        if (resizableStyle != originalStyle && SetWindowLongW(window, WindowStyleIndex, resizableStyle) == 0)
        {
            return null;
        }

        if (!RefreshFrame(window) || !SetWindowSubclass(window, Procedure, SubclassId, 0))
        {
            _ = SetWindowLongW(window, WindowStyleIndex, originalStyle);
            _ = RefreshFrame(window);
            return null;
        }

        return new(window, originalStyle, styleChanged: resizableStyle != originalStyle);
    }

    internal static int ResolveResizableStyle(int style) =>
        style | WindowStyleThickFrame | WindowStyleMaximizeBox;

    internal static uint GetDpi(nint window) => GenesisMainWindowLayout.NormalizeDpi(GetDpiForWindow(window));

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0082) // WM_NCDESTROY
        {
            _ = RemoveWindowSubclass(window, Procedure, id);
            return DefSubclassProc(window, message, wParam, lParam);
        }
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0024 && lParam != 0) // WM_GETMINMAXINFO
        {
            var dpi = GetDpi(window);
            var rectangle = new Rectangle
            {
                Right = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientWidth, dpi),
                Bottom = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientHeight, dpi),
            };
            if (AdjustWindowRectExForDpi(ref rectangle, (uint)GetWindowLongW(window, WindowStyleIndex),
                    false, (uint)GetWindowLongW(window, -20), dpi))
            {
                var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                limits.MinTrackSize.X = rectangle.Right - rectangle.Left;
                limits.MinTrackSize.Y = rectangle.Bottom - rectangle.Top;
                Marshal.StructureToPtr(limits, lParam, false);
            }
        }
        else if (message == 0x02E0 && lParam != 0) // WM_DPICHANGED: use Windows' suggested monitor bounds.
        {
            var rectangle = Marshal.PtrToStructure<Rectangle>(lParam);
            _ = SetWindowPos(window, 0, rectangle.Left, rectangle.Top,
                rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top,
                SetWindowPositionNoZOrder | SetWindowPositionNoActivate);
            GenesisGuiNextAppShellBridge.ResizeForOwner(window);
        }
        else if (message == 0x0005 && wParam != 1) // WM_SIZE, excluding SIZE_MINIMIZED.
        {
            GenesisGuiNextAppShellBridge.ResizeForOwner(window);
        }
        return result;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _window != 0 && IsWindow(_window))
        {
            _ = RemoveWindowSubclass(_window, Procedure, SubclassId);
            if (_styleChanged)
            {
                _ = SetWindowLongW(_window, WindowStyleIndex, _originalStyle);
                _ = RefreshFrame(_window);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool RefreshFrame(nint window) =>
        SetWindowPos(
            window,
            0,
            0,
            0,
            0,
            0,
            SetWindowPositionNoSize |
                SetWindowPositionNoMove |
                SetWindowPositionNoZOrder |
                SetWindowPositionNoActivate |
                SetWindowPositionFrameChanged);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref Rectangle rectangle, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

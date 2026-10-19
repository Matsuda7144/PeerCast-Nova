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

        var fixedStyle = ResolveFixedStyle(originalStyle);
        if (fixedStyle == originalStyle)
        {
            return new(window, originalStyle, styleChanged: false);
        }

        if (SetWindowLongW(window, WindowStyleIndex, fixedStyle) == 0)
        {
            return null;
        }

        if (!RefreshFrame(window))
        {
            _ = SetWindowLongW(window, WindowStyleIndex, originalStyle);
            _ = RefreshFrame(window);
            return null;
        }

        return new(window, originalStyle, styleChanged: true);
    }

    internal static int ResolveFixedStyle(int style) =>
        style & ~(WindowStyleThickFrame | WindowStyleMaximizeBox);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 &&
            _styleChanged &&
            _window != 0 &&
            IsWindow(_window))
        {
            _ = SetWindowLongW(_window, WindowStyleIndex, _originalStyle);
            _ = RefreshFrame(_window);
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

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

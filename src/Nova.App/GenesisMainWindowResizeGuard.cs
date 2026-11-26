using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed class GenesisMainWindowResizeGuard : IAsyncDisposable
{
    internal const int WindowStyleThickFrame = 0x00040000;
    internal const int WindowStyleMaximizeBox = 0x00010000;
    private const int WindowStyleIndex = -16;
    private const uint WindowMessageDestroyOrNcDestroy = 0x0082;
    private const uint WindowMessageExitSizeMove = 0x0232;
    private const uint SwShowNormal = 1;
    private const uint SwShowMaximized = 3;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const nuint SubclassId = 0x4E52535A; // NRSZ; main surface only.
    private static readonly SubclassProcedure Procedure = ProcessMessage;
    private static readonly ConcurrentDictionary<nint, GenesisMainWindowPlacementStore> PlacementStores = new();
    private static readonly GenesisMainWindowPlacementStore DefaultPlacementStore = new();
    private readonly nint _window;
    private readonly int _originalStyle;
    private readonly bool _styleChanged;
    private readonly GenesisSystemTrayBridge? _trayBridge;
    private int _disposed;

    private GenesisMainWindowResizeGuard(
        nint window,
        int originalStyle,
        bool styleChanged,
        GenesisSystemTrayBridge? trayBridge)
    {
        _window = window;
        _originalStyle = originalStyle;
        _styleChanged = styleChanged;
        _trayBridge = trayBridge;
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

        return TryCreate((nint)windowToken, DefaultPlacementStore);
    }

    internal static GenesisMainWindowResizeGuard? TryCreate(nint window) =>
        TryCreate(window, placementStore: null);

    internal static GenesisMainWindowResizeGuard? TryCreate(
        nint window,
        GenesisMainWindowPlacementStore? placementStore)
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

        GenesisSystemTrayBridge? trayBridge = null;
        if (placementStore is not null)
        {
            PlacementStores[window] = placementStore;
            if (placementStore.Load() is { } savedPlacement)
            {
                RestorePlacement(window, savedPlacement);
            }

            trayBridge = GenesisSystemTrayBridge.TryCreate(window);
        }

        return new(window, originalStyle, styleChanged: resizableStyle != originalStyle, trayBridge);
    }

    internal static int ResolveResizableStyle(int style) =>
        style | WindowStyleThickFrame | WindowStyleMaximizeBox;

    internal static uint GetDpi(nint window) => GenesisMainWindowLayout.NormalizeDpi(GetDpiForWindow(window));

    private static void RestorePlacement(nint window, GenesisMainWindowPlacement saved)
    {
        var rect = new Rectangle
        {
            Left = saved.Left,
            Top = saved.Top,
            Right = saved.Left + saved.Width,
            Bottom = saved.Top + saved.Height,
        };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref monitorInfo))
        {
            return;
        }

        var dpi = GetDpi(window);
        var minOuter = new Rectangle
        {
            Right = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientWidth, dpi),
            Bottom = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientHeight, dpi),
        };
        var minWidth = 480;
        var minHeight = 360;
        if (AdjustWindowRectExForDpi(ref minOuter, (uint)GetWindowLongW(window, WindowStyleIndex),
                false, (uint)GetWindowLongW(window, -20), dpi))
        {
            minWidth = Math.Max(1, minOuter.Right - minOuter.Left);
            minHeight = Math.Max(1, minOuter.Bottom - minOuter.Top);
        }

        var clamped = saved.ClampToWorkArea(
            monitorInfo.Work.Left,
            monitorInfo.Work.Top,
            monitorInfo.Work.Right,
            monitorInfo.Work.Bottom,
            minWidth,
            minHeight);
        var placement = new WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<WindowPlacement>(),
            ShowCmd = clamped.Maximized ? SwShowMaximized : SwShowNormal,
            NormalPosition = new Rectangle
            {
                Left = clamped.Left,
                Top = clamped.Top,
                Right = clamped.Left + clamped.Width,
                Bottom = clamped.Top + clamped.Height,
            },
        };
        if (SetWindowPlacement(window, ref placement))
        {
            GenesisGuiNextAppShellBridge.ResizeForOwner(window);
        }
    }

    private static void SaveCurrentPlacement(nint window, GenesisMainWindowPlacementStore store)
    {
        var placement = new WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<WindowPlacement>(),
        };
        if (!GetWindowPlacement(window, ref placement))
        {
            return;
        }

        var width = placement.NormalPosition.Right - placement.NormalPosition.Left;
        var height = placement.NormalPosition.Bottom - placement.NormalPosition.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        store.Save(new GenesisMainWindowPlacement(
            placement.NormalPosition.Left,
            placement.NormalPosition.Top,
            width,
            height,
            Maximized: placement.ShowCmd == SwShowMaximized));
    }

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WindowMessageDestroyOrNcDestroy) // WM_NCDESTROY
        {
            if (PlacementStores.TryRemove(window, out var store))
            {
                SaveCurrentPlacement(window, store);
            }

            _ = RemoveWindowSubclass(window, Procedure, id);
            return DefSubclassProc(window, message, wParam, lParam);
        }
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == WindowMessageExitSizeMove && PlacementStores.TryGetValue(window, out var currentStore))
        {
            SaveCurrentPlacement(window, currentStore);
        }
        else if (message == 0x0024 && lParam != 0) // WM_GETMINMAXINFO
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (_trayBridge is not null)
            {
                await _trayBridge.DisposeAsync().ConfigureAwait(false);
            }

            if (_window != 0 && IsWindow(_window))
            {
                if (PlacementStores.TryRemove(_window, out var store))
                {
                    SaveCurrentPlacement(_window, store);
                }

                _ = RemoveWindowSubclass(_window, Procedure, SubclassId);
                if (_styleChanged)
                {
                    _ = SetWindowLongW(_window, WindowStyleIndex, _originalStyle);
                    _ = RefreshFrame(_window);
                }
            }
            else if (_window != 0)
            {
                _ = PlacementStores.TryRemove(_window, out _);
            }
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rectangle NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rectangle Monitor;
        public Rectangle Work;
        public uint Flags;
    }
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref Rectangle rectangle, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref Rectangle rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint window, [In] ref WindowPlacement placement);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

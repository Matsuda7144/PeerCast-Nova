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
    private const uint SwHide = 0;
    private const uint SwShowNormal = 1;
    private const uint SwShowMaximized = 3;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const nuint SubclassId = 0x4E52535A; // NRSZ; main surface only.
    private static readonly Win32SubclassProcedure Procedure = ProcessMessage;
    private static readonly ConcurrentDictionary<nint, GenesisMainWindowPlacementStore> PlacementStores = new();
    private static readonly ConcurrentDictionary<nint, int> PendingShowCommands = new();
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

    internal static int? TryGetSavedDetailsHeight(nint window) =>
        PlacementStores.TryGetValue(window, out var store) ? store.Load()?.DetailsHeight : null;

    internal static void SaveDetailsHeight(nint window, int detailsHeight)
    {
        if (PlacementStores.TryGetValue(window, out var store))
        {
            SaveCurrentPlacement(window, store, detailsHeight);
        }
    }

    internal static void RevealWindow(nint window)
    {
        if (window == 0 || !OperatingSystem.IsWindows())
        {
            _ = PendingShowCommands.TryRemove(window, out _);
            return;
        }

        var showCommand = PendingShowCommands.TryRemove(window, out var pending)
            ? pending
            : (int)SwShowNormal;
        if (!IsWindow(window) || IsWindowVisible(window))
        {
            return;
        }

        _ = ShowWindow(window, showCommand);
        _ = RedrawWindow(window, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0100);
        if (GenesisMainWindowControlRegistry.TryResolve(window, out var handles) &&
            handles.ChannelList != 0 &&
            IsWindow(handles.ChannelList))
        {
            _ = SetFocus(handles.ChannelList);
        }
    }

    private static void RestorePlacement(nint window, GenesisMainWindowPlacement saved)
    {
        var rect = new Win32Rect
        {
            Left = saved.Left,
            Top = saved.Top,
            Right = saved.Left + saved.Width,
            Bottom = saved.Top + saved.Height,
        };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var monitorInfo = new Win32MonitorInfo { Size = (uint)Marshal.SizeOf<Win32MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref monitorInfo))
        {
            return;
        }

        var dpi = GetDpi(window);
        var minOuter = new Win32Rect
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
        var isVisible = IsWindowVisible(window);
        var targetShowCmd = clamped.Maximized ? SwShowMaximized : SwShowNormal;
        if (!isVisible)
        {
            PendingShowCommands[window] = (int)targetShowCmd;
        }

        var placement = new Win32WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<Win32WindowPlacement>(),
            ShowCmd = isVisible ? targetShowCmd : SwHide,
            NormalPosition = new Win32Rect
            {
                Left = clamped.Left,
                Top = clamped.Top,
                Right = clamped.Left + clamped.Width,
                Bottom = clamped.Top + clamped.Height,
            },
        };
        var applied = SetWindowPlacement(window, ref placement);
        if (!isVisible)
        {
            _ = SetWindowPos(
                window,
                0,
                clamped.Left,
                clamped.Top,
                clamped.Width,
                clamped.Height,
                SetWindowPositionNoZOrder | SetWindowPositionNoActivate);
        }

        if (applied || !isVisible)
        {
            GenesisGuiNextAppShellBridge.ResizeForOwner(window);
        }
    }

    private static void SaveCurrentPlacement(nint window, GenesisMainWindowPlacementStore store, int? explicitDetailsHeight = null)
    {
        var placement = new Win32WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<Win32WindowPlacement>(),
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

        var detailsHeight = explicitDetailsHeight ??
            GenesisGuiNextAppShellBridge.GetDetailsHeightForOwner(window) ??
            store.Load()?.DetailsHeight;
        store.Save(new GenesisMainWindowPlacement(
            placement.NormalPosition.Left,
            placement.NormalPosition.Top,
            width,
            height,
            Maximized: placement.ShowCmd == SwShowMaximized,
            DetailsHeight: detailsHeight));
    }

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WindowMessageDestroyOrNcDestroy) // WM_NCDESTROY
        {
            _ = PendingShowCommands.TryRemove(window, out _);
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
            var rectangle = new Win32Rect
            {
                Right = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientWidth, dpi),
                Bottom = GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientHeight, dpi),
            };
            if (AdjustWindowRectExForDpi(ref rectangle, (uint)GetWindowLongW(window, WindowStyleIndex),
                    false, (uint)GetWindowLongW(window, -20), dpi))
            {
                var limits = Marshal.PtrToStructure<Win32MinMaxInfo>(lParam);
                limits.MinTrackSize.X = rectangle.Right - rectangle.Left;
                limits.MinTrackSize.Y = rectangle.Bottom - rectangle.Top;
                Marshal.StructureToPtr(limits, lParam, false);
            }
        }
        else if (message == 0x02E0 && lParam != 0) // WM_DPICHANGED: use Windows' suggested monitor bounds.
        {
            var rectangle = Marshal.PtrToStructure<Win32Rect>(lParam);
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
            _ = PendingShowCommands.TryRemove(_window, out _);
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

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

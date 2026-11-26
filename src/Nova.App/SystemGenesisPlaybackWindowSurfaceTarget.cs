using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal interface IGenesisPlaybackWindowNativeSurfaceFactory
{
    IAsyncDisposable? Create(
        long ownerWindowToken,
        GenesisPlaybackWindowState state);
}

internal sealed class SystemGenesisPlaybackWindowSurfaceTarget :
    IGenesisPlaybackWindowSurfaceTarget
{
    private readonly IGenesisPlaybackWindowNativeSurfaceFactory _factory;
    private readonly Func<bool> _isSupportedPlatform;

    public SystemGenesisPlaybackWindowSurfaceTarget()
        : this(
            SystemGenesisPlaybackWindowNativeSurfaceFactory.Instance,
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17_763))
    {
    }

    internal SystemGenesisPlaybackWindowSurfaceTarget(
        IGenesisPlaybackWindowNativeSurfaceFactory factory,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _factory = factory;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public ValueTask<IAsyncDisposable?> OpenAsync(
        long ownerWindowToken,
        GenesisPlaybackWindowState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (ownerWindowToken == 0 ||
            !state.IsOpen ||
            string.IsNullOrWhiteSpace(state.Source) ||
            cancellationToken.IsCancellationRequested ||
            !_isSupportedPlatform())
        {
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }

        return ValueTask.FromResult(_factory.Create(ownerWindowToken, state));
    }
}

internal sealed class SystemGenesisPlaybackWindowNativeSurfaceFactory :
    IGenesisPlaybackWindowNativeSurfaceFactory
{
    public static SystemGenesisPlaybackWindowNativeSurfaceFactory Instance { get; } = new();

    private SystemGenesisPlaybackWindowNativeSurfaceFactory()
    {
    }

    public IAsyncDisposable? Create(
        long ownerWindowToken,
        GenesisPlaybackWindowState state) =>
        GenesisPlaybackWindowNativeSurface.TryCreate((nint)ownerWindowToken, state);
}

internal sealed class GenesisPlaybackWindowNativeSurface :
    IGenesisPlaybackWindowCloseAwareLifetime
{
    private const string WindowClassName = "PeerCastNovaGenesisPlaybackWindow";
    private const uint WindowStyleOverlappedWindow = 0x00CF0000;
    private const uint WindowStylePopup = 0x80000000;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int ScreenWidthMetric = 0;
    private const int ScreenHeightMetric = 1;
    private const int ShowWindowNormal = 1;
    private const int DefaultPlaybackWidth = 960;
    private const int DefaultPlaybackHeight = 540;
    private const int UseDefaultPosition = unchecked((int)0x80000000);
    private const int BlackBrush = 4;
    private const int ErrorClassAlreadyExists = 1410;
    private static readonly object WindowClassSync = new();
    private static readonly ConcurrentDictionary<nint, GenesisPlaybackWindowNativeSurface>
        SurfacesByWindow = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private static bool _windowClassRegistered;
    private static nint _windowClassInstance;
    private readonly nint _window;
    private readonly TaskCompletionSource<bool> _closedByUser =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _programmaticClose;
    private int _disposed;

    private GenesisPlaybackWindowNativeSurface(nint window)
    {
        _window = window;
    }

    public Task<bool> ClosedByUser => _closedByUser.Task;

    public static GenesisPlaybackWindowNativeSurface? TryCreate(
        nint owner,
        GenesisPlaybackWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (owner == 0 ||
            !state.IsOpen ||
            string.IsNullOrWhiteSpace(state.Source) ||
            !EnsureWindowClass())
        {
            return null;
        }

        var fullscreen = state.Mode == GenesisPlaybackWindowMode.Fullscreen;
        var borderless = state.Mode is
            GenesisPlaybackWindowMode.VideoOnly or GenesisPlaybackWindowMode.Fullscreen;
        var style = borderless ? WindowStylePopup : WindowStyleOverlappedWindow;
        var bounds = fullscreen
            ? ResolveFullscreenBounds(owner, GetOwnerMonitorBounds, GetSystemMetrics)
            : new WindowBounds(
                UseDefaultPosition,
                UseDefaultPosition,
                DefaultPlaybackWidth,
                DefaultPlaybackHeight);

        var window = CreateWindowExW(
            0,
            WindowClassName,
            BuildTitle(state.Source),
            style,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            owner,
            0,
            _windowClassInstance,
            0);
        if (window == 0)
        {
            return null;
        }

        var surface = new GenesisPlaybackWindowNativeSurface(window);
        if (!SurfacesByWindow.TryAdd(window, surface))
        {
            _ = DestroyWindow(window);
            return null;
        }

        _ = ShowWindow(window, ShowWindowNormal);
        _ = UpdateWindow(window);
        return surface;
    }

    internal static WindowBounds ResolveFullscreenBounds(
        nint owner,
        Func<nint, WindowBounds?> getOwnerMonitorBounds,
        Func<int, int> getSystemMetric)
    {
        ArgumentNullException.ThrowIfNull(getOwnerMonitorBounds);
        ArgumentNullException.ThrowIfNull(getSystemMetric);

        if (getOwnerMonitorBounds(owner) is { } monitorBounds)
        {
            return new(
                monitorBounds.X,
                monitorBounds.Y,
                Math.Max(1, monitorBounds.Width),
                Math.Max(1, monitorBounds.Height));
        }

        return new(
            0,
            0,
            Math.Max(1, getSystemMetric(ScreenWidthMetric)),
            Math.Max(1, getSystemMetric(ScreenHeightMetric)));
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Interlocked.Exchange(ref _programmaticClose, 1);
            _closedByUser.TrySetResult(false);
            if (_window != 0 && IsWindow(_window))
            {
                _ = PostMessageW(_window, WindowMessageClose, 0, 0);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool EnsureWindowClass()
    {
        lock (WindowClassSync)
        {
            if (_windowClassRegistered)
            {
                return true;
            }

            var instance = GetModuleHandleW(null);
            if (instance == 0)
            {
                return false;
            }

            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
                Instance = instance,
                BackgroundBrush = GetStockObject(BlackBrush),
                ClassName = WindowClassName,
            };
            if (RegisterClassExW(ref windowClass) == 0 &&
                Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
            {
                return false;
            }

            _windowClassInstance = instance;
            _windowClassRegistered = true;
            return true;
        }
    }

    private static WindowBounds? GetOwnerMonitorBounds(nint owner)
    {
        var monitor = MonitorFromWindow(owner, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return null;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfoW(monitor, ref monitorInfo))
        {
            return null;
        }

        return new(
            monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Top,
            monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
    }

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageClose)
        {
            if (SurfacesByWindow.TryGetValue(window, out var surface))
            {
                surface.NotifyCloseRequested();
            }

            _ = DestroyWindow(window);
            return 0;
        }

        if (message == WindowMessageNcDestroy)
        {
            if (SurfacesByWindow.TryRemove(window, out var removedSurface))
            {
                removedSurface.NotifyDestroyed();
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void NotifyCloseRequested()
    {
        var closedByUser = Volatile.Read(ref _programmaticClose) == 0;
        _closedByUser.TrySetResult(closedByUser);
    }

    private void NotifyDestroyed() =>
        _closedByUser.TrySetResult(false);

    internal static string BuildTitle(string source)
    {
        const int maximumSourceCharacters = 96;
        var trimmed = source.Trim();
        if (trimmed.Length <= maximumSourceCharacters)
        {
            return $"PeerCast Nova - {trimmed}";
        }

        var boundedLength = maximumSourceCharacters;
        if (char.IsHighSurrogate(trimmed[boundedLength - 1]) &&
            char.IsLowSurrogate(trimmed[boundedLength]))
        {
            boundedLength--;
        }

        return $"PeerCast Nova - {trimmed[..boundedLength]}…";
    }

    internal readonly record struct WindowBounds(
        int X,
        int Y,
        int Width,
        int Height);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
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
    private struct MonitorInfo
    {
        public uint Size;
        public WindowRectangle Monitor;
        public WindowRectangle Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

}

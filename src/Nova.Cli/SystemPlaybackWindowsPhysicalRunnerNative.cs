using System.Runtime.InteropServices;

namespace PeerCast.Nova.Cli;

internal sealed class SystemPlaybackWindowsPhysicalRunnerNative :
    IPlaybackWindowsPhysicalRunnerNative
{
    private const uint ClassStyleOwnDeviceContext = 0x0020;
    private const uint WindowStyleOverlapped = 0x00CF0000;
    private const int ShowWindowNormal = 1;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessageApplication = 0x8000;
    private const uint WindowMessageDrainWork = WindowMessageApplication + 0x714;
    private const int CursorArrow = 32512;
    private readonly object _sync = new();
    private readonly WindowProcedure _windowProcedure;
    private PlaybackWindowsPhysicalRunnerNativeCallbacks _callbacks;
    private nint _window;
    private bool _stopRequested;
    private bool _isDisposed;

    public SystemPlaybackWindowsPhysicalRunnerNative()
    {
        _windowProcedure = ProcessWindowMessage;
    }

    public PlaybackWindowsPhysicalRunnerNativeRunResult Run(
        PlaybackWindowsPhysicalRunnerOptions options,
        PlaybackWindowsPhysicalRunnerNativeCallbacks callbacks)
    {
        if (_isDisposed || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
        {
            return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
        }

        _callbacks = callbacks;
        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
        }

        var className = $"PeerCastNovaPhysicalRunner-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = ClassStyleOwnDeviceContext,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            ClassName = className,
        };
        var atom = RegisterClassExW(ref windowClass);
        if (atom == 0)
        {
            return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
        }

        nint window = 0;
        try
        {
            var rectangle = new WindowRectangle
            {
                Left = 0,
                Top = 0,
                Right = options.ClientWidth,
                Bottom = options.ClientHeight,
            };
            if (!AdjustWindowRectEx(ref rectangle, WindowStyleOverlapped, false, 0))
            {
                return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
            }

            window = CreateWindowExW(
                0,
                className,
                options.Title,
                WindowStyleOverlapped,
                int.MinValue,
                int.MinValue,
                rectangle.Right - rectangle.Left,
                rectangle.Bottom - rectangle.Top,
                0,
                0,
                instance,
                0);
            if (window == 0)
            {
                return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
            }

            bool requestStop;
            lock (_sync)
            {
                if (_isDisposed)
                {
                    return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
                }

                _window = window;
                requestStop = _stopRequested;
            }

            _ = ShowWindow(window, ShowWindowNormal);
            UpdateWindow(window);
            callbacks.Started(window.ToInt64());
            if (requestStop)
            {
                PostMessageW(window, WindowMessageClose, 0, 0);
            }

            while (true)
            {
                var messageResult = GetMessageW(out var message, 0, 0, 0);
                if (messageResult == -1)
                {
                    return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
                }

                if (messageResult == 0)
                {
                    return PlaybackWindowsPhysicalRunnerNativeRunResult.Stopped;
                }

                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        finally
        {
            lock (_sync)
            {
                _window = 0;
            }

            if (window != 0 && IsWindow(window))
            {
                DestroyWindow(window);
            }

            UnregisterClassW(className, instance);
        }
    }

    public bool SignalWork()
    {
        lock (_sync)
        {
            return !_isDisposed && _window != 0 &&
                PostMessageW(_window, WindowMessageDrainWork, 0, 0);
        }
    }

    public bool RequestStop()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return false;
            }

            _stopRequested = true;
            return _window == 0 || PostMessageW(_window, WindowMessageClose, 0, 0);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _stopRequested = true;
            if (_window != 0)
            {
                PostMessageW(_window, WindowMessageClose, 0, 0);
            }
        }
    }

    private nint ProcessWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessageDrainWork:
                _callbacks.DrainWork();
                return 0;
            case WindowMessageClose:
                DestroyWindow(window);
                return 0;
            case WindowMessageDestroy:
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

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
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public WindowPoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectEx(
        ref WindowRectangle rectangle,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu,
        uint extendedStyle);

    [DllImport("user32.dll")]
    private static extern int ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(
        out WindowMessage message,
        nint window,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref WindowMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref WindowMessage message);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);
}

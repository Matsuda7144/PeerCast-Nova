using System.Runtime.InteropServices;
using static PeerCast.Nova.Windows.Win32WindowNativeMethods;

namespace PeerCast.Nova.Windows;

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
        ushort atom = 0;
        nint window = 0;
        try
        {
            if (options.ApplicationIconPath is { } iconPath)
            {
                _ = ExtractIconExW(iconPath, 0, out windowClass.Icon, out windowClass.SmallIcon, 1);
            }

            atom = RegisterClassExW(ref windowClass);
            if (atom == 0)
            {
                return PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
            }

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

            if (atom != 0)
            {
                _ = UnregisterClassW(className, instance);
            }

            if (windowClass.Icon != 0)
            {
                _ = DestroyIcon(windowClass.Icon);
            }

            if (windowClass.SmallIcon != 0)
            {
                _ = DestroyIcon(windowClass.SmallIcon);
            }
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

}

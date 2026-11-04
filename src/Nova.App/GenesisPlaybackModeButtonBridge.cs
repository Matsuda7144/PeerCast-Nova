using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed class GenesisPlaybackModeButtonPlacement
{
    private const int NormalWidth = 72;
    private const int VideoOnlyWidth = 88;
    private const int FullscreenWidth = 88;
    private const int ButtonGap = 6;
    private const int StatusGap = 8;
    internal const int TotalButtonWidth =
        NormalWidth + VideoOnlyWidth + FullscreenWidth + (ButtonGap * 2);
    private readonly nint _parent;
    private readonly nint _status;
    private readonly WindowRectangle _statusRectangle;

    private GenesisPlaybackModeButtonPlacement(
        nint parent,
        nint status,
        WindowRectangle statusRectangle)
    {
        _parent = parent;
        _status = status;
        _statusRectangle = statusRectangle;
    }

    public static GenesisPlaybackModeButtonPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var parent = (nint)windowToken;
        var sourceLabel = FindWindowExW(parent, 0, "STATIC", "Source");
        var status = sourceLabel == 0
            ? 0
            : FindWindowExW(parent, sourceLabel, "STATIC", null);
        if (status == 0 || !GetWindowRect(status, out var rectangle))
        {
            return null;
        }

        var topLeft = new WindowPoint(rectangle.Left, rectangle.Top);
        var bottomRight = new WindowPoint(rectangle.Right, rectangle.Bottom);
        if (!ScreenToClient(parent, ref topLeft) ||
            !ScreenToClient(parent, ref bottomRight))
        {
            return null;
        }

        return new(
            parent,
            status,
            new(
                topLeft.X,
                topLeft.Y,
                bottomRight.X,
                bottomRight.Y));
    }

    public IAsyncDisposable? Open(GenesisMainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var originalWidth = Math.Max(1, _statusRectangle.Right - _statusRectangle.Left);
        var height = Math.Max(1, _statusRectangle.Bottom - _statusRectangle.Top);
        var reducedWidth = Math.Max(120, originalWidth - TotalButtonWidth - StatusGap);
        if (reducedWidth >= originalWidth ||
            !MoveWindow(
                _status,
                _statusRectangle.Left,
                _statusRectangle.Top,
                reducedWidth,
                height,
                true))
        {
            return null;
        }

        var left = _statusRectangle.Left + reducedWidth + StatusGap;
        var bridge = GenesisPlaybackModeButtonBridge.TryCreate(
            _parent,
            _status,
            _statusRectangle,
            left,
            _statusRectangle.Top,
            TotalButtonWidth,
            height,
            viewModel);
        if (bridge is null)
        {
            _ = RestoreStatus();
        }

        return bridge;
    }

    private bool RestoreStatus() =>
        IsWindow(_status) &&
        MoveWindow(
            _status,
            _statusRectangle.Left,
            _statusRectangle.Top,
            Math.Max(1, _statusRectangle.Right - _statusRectangle.Left),
            Math.Max(1, _statusRectangle.Bottom - _statusRectangle.Top),
            true);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct WindowRectangle(
        int Left,
        int Top,
        int Right,
        int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public WindowPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref WindowPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        nint window,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}

internal sealed class GenesisPlaybackModeButtonBridge : IAsyncDisposable
{
    internal static bool TryResize(nint owner, nint status, int left, int top, int width, int height, uint dpi)
    {
        foreach (var (container, state) in StatesByContainer)
        {
            if (GetParent(container) != owner) continue;
            var modeWidth = GenesisPlaybackModeButtonPlacement.TotalButtonWidth;
            var statusWidth = Math.Max(120, width - modeWidth - 8);
            int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
            _ = MoveWindow(status, S(left), S(top), S(statusWidth), S(height), true);
            _ = MoveWindow(container, S(left + statusWidth + 8), S(top), S(modeWidth), S(height), true);
            state.ApplyLayout(height, dpi);
            return true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageRefresh = 0x8001;
    private const int ButtonNotificationClicked = 0;
    private const int DefaultGuiFont = 17;
    private const int NormalWidth = 72;
    private const int VideoOnlyWidth = 88;
    private const int ButtonGap = 6;
    private static readonly ConcurrentDictionary<nint, ModeState> StatesByContainer = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private readonly nint _status;
    private readonly GenesisPlaybackModeButtonPlacement.WindowRectangle _statusRectangle;
    private readonly nint _container;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisPlaybackModeButtonBridge(
        nint status,
        GenesisPlaybackModeButtonPlacement.WindowRectangle statusRectangle,
        nint container,
        string className,
        nint instance)
    {
        _status = status;
        _statusRectangle = statusRectangle;
        _container = container;
        _className = className;
        _instance = instance;
    }

    public static GenesisPlaybackModeButtonBridge? TryCreate(
        nint parent,
        nint status,
        GenesisPlaybackModeButtonPlacement.WindowRectangle statusRectangle,
        int left,
        int top,
        int width,
        int height,
        GenesisMainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var instance = GetModuleHandleW(null);
        if (parent == 0 || status == 0 || instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisPlaybackModes-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            return null;
        }

        var container = CreateWindowExW(
            0,
            className,
            string.Empty,
            WindowStyleChild | WindowStyleVisible,
            left,
            top,
            width,
            height,
            parent,
            0,
            instance,
            0);
        if (container == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var normalButton = CreateButton(container, "Normal", 0, 0, NormalWidth, height);
        var videoOnlyLeft = NormalWidth + ButtonGap;
        var videoOnlyButton = CreateButton(
            container,
            "Video Only",
            videoOnlyLeft,
            0,
            VideoOnlyWidth,
            height);
        var fullscreenLeft = videoOnlyLeft + VideoOnlyWidth + ButtonGap;
        var fullscreenButton = CreateButton(
            container,
            "Fullscreen",
            fullscreenLeft,
            0,
            Math.Max(1, width - fullscreenLeft),
            height);
        if (normalButton == 0 || videoOnlyButton == 0 || fullscreenButton == 0)
        {
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        ApplyDefaultFont(normalButton, videoOnlyButton, fullscreenButton);
        var state = new ModeState(
            viewModel,
            container,
            normalButton,
            videoOnlyButton,
            fullscreenButton);
        if (!StatesByContainer.TryAdd(container, state))
        {
            state.Dispose();
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        state.RefreshButtons();
        return new(status, statusRectangle, container, className, instance);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (StatesByContainer.TryRemove(_container, out var state))
            {
                state.Dispose();
            }

            if (_container != 0 && IsWindow(_container))
            {
                _ = DestroyWindow(_container);
            }

            _ = UnregisterClassW(_className, _instance);
            if (_status != 0 && IsWindow(_status))
            {
                _ = MoveWindow(
                    _status,
                    _statusRectangle.Left,
                    _statusRectangle.Top,
                    Math.Max(1, _statusRectangle.Right - _statusRectangle.Left),
                    Math.Max(1, _statusRectangle.Bottom - _statusRectangle.Top),
                    true);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static nint CreateButton(
        nint parent,
        string text,
        int left,
        int top,
        int width,
        int height) =>
        CreateWindowExW(
            0,
            "BUTTON",
            text,
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop,
            left,
            top,
            width,
            height,
            parent,
            0,
            0,
            0);

    private static void ApplyDefaultFont(params nint[] controls)
    {
        var font = GetStockObject(DefaultGuiFont);
        if (font == 0)
        {
            return;
        }

        foreach (var control in controls)
        {
            _ = SendMessageW(control, WindowMessageSetFont, (nuint)font, 1);
        }
    }

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageNcDestroy)
        {
            if (StatesByContainer.TryRemove(window, out var removedState))
            {
                removedState.Dispose();
            }

            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (StatesByContainer.TryGetValue(window, out var state))
        {
            if (message == WindowMessageCommand)
            {
                state.ProcessCommand(wParam, lParam);
            }
            else if (message == WindowMessageRefresh)
            {
                state.RefreshButtons();
                return 0;
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    internal sealed class ModeState : IDisposable
    {
        internal void ApplyLayout(int height, uint dpi)
        {
            int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
            var videoLeft = NormalWidth + ButtonGap;
            var fullscreenLeft = videoLeft + VideoOnlyWidth + ButtonGap;
            _ = MoveWindow(_normalButton, 0, 0, S(NormalWidth), S(height), true);
            _ = MoveWindow(_videoOnlyButton, S(videoLeft), 0, S(VideoOnlyWidth), S(height), true);
            _ = MoveWindow(_fullscreenButton, S(fullscreenLeft), 0,
                S(GenesisPlaybackModeButtonPlacement.TotalButtonWidth - fullscreenLeft), S(height), true);
        }
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly nint _container;
        private readonly nint _normalButton;
        private readonly nint _videoOnlyButton;
        private readonly nint _fullscreenButton;
        private readonly Func<nint, bool, bool> _enableWindow;
        private readonly Func<nint, bool> _postRefresh;
        private int _disposed;

        public ModeState(
            GenesisMainViewModel viewModel,
            nint container,
            nint normalButton,
            nint videoOnlyButton,
            nint fullscreenButton)
            : this(
                viewModel,
                container,
                normalButton,
                videoOnlyButton,
                fullscreenButton,
                EnableWindow,
                static window => PostMessageW(window, WindowMessageRefresh, 0, 0))
        {
        }

        internal ModeState(
            GenesisMainViewModel viewModel,
            nint container,
            nint normalButton,
            nint videoOnlyButton,
            nint fullscreenButton,
            Func<nint, bool, bool> enableWindow,
            Func<nint, bool> postRefresh)
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            ArgumentNullException.ThrowIfNull(enableWindow);
            ArgumentNullException.ThrowIfNull(postRefresh);
            _viewModel = viewModel;
            _interaction = new(viewModel);
            _container = container;
            _normalButton = normalButton;
            _videoOnlyButton = videoOnlyButton;
            _fullscreenButton = fullscreenButton;
            _enableWindow = enableWindow;
            _postRefresh = postRefresh;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode != ButtonNotificationClicked)
            {
                return;
            }

            if (lParam == _normalButton)
            {
                _ = _interaction.RestorePlaybackWindow();
            }
            else if (lParam == _videoOnlyButton)
            {
                _ = _interaction.ShowVideoOnly();
            }
            else if (lParam == _fullscreenButton)
            {
                _ = _interaction.ShowFullscreen();
            }
        }

        public void RefreshButtons()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var open = _viewModel.IsPlaybackWindowOpen;
            var mode = _viewModel.PlaybackWindowMode;
            _ = _enableWindow(
                _normalButton,
                open && mode != GenesisPlaybackWindowMode.Normal);
            _ = _enableWindow(
                _videoOnlyButton,
                open && mode != GenesisPlaybackWindowMode.VideoOnly);
            _ = _enableWindow(
                _fullscreenButton,
                open && mode != GenesisPlaybackWindowMode.Fullscreen);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _ = _postRefresh(_container);
            }
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

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

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        nint window,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int objectIndex);
}

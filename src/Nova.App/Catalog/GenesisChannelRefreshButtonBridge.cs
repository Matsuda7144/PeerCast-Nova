using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed class GenesisChannelRefreshButtonPlacement
{
    internal const int RefreshWidth = 104;
    private const int RefreshGap = 8;
    private readonly nint _owner;
    private readonly nint _sourceEdit;
    private readonly WindowRectangle _sourceRectangle;

    private GenesisChannelRefreshButtonPlacement(
        nint owner,
        nint sourceEdit,
        WindowRectangle sourceRectangle)
    {
        _owner = owner;
        _sourceEdit = sourceEdit;
        _sourceRectangle = sourceRectangle;
    }

    public static GenesisChannelRefreshButtonPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var parent = (nint)windowToken;
        var sourceEdit = GenesisMainWindowControlRegistry.ResolveSourceEdit(parent);
        if (sourceEdit == 0 || !GetWindowRect(sourceEdit, out var rectangle))
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
            sourceEdit,
            new(
                topLeft.X,
                topLeft.Y,
                bottomRight.X,
                bottomRight.Y));
    }

    public IAsyncDisposable? Open(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession session,
        bool showButton = true)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);
        var parent = ResolveParentForOpen(_sourceEdit, GetParent);
        if (parent == 0)
        {
            return null;
        }

        var originalWidth = Math.Max(1, _sourceRectangle.Right - _sourceRectangle.Left);
        var height = Math.Max(1, _sourceRectangle.Bottom - _sourceRectangle.Top);
        var reducedWidth = showButton
            ? Math.Max(120, originalWidth - RefreshWidth - RefreshGap)
            : originalWidth;
        if (showButton &&
            (reducedWidth >= originalWidth ||
                !MoveWindow(
                    _sourceEdit,
                    _sourceRectangle.Left,
                    _sourceRectangle.Top,
                    reducedWidth,
                    height,
                    true)))
        {
            return null;
        }

        var refreshLeft = showButton
            ? _sourceRectangle.Left + reducedWidth + RefreshGap
            : _sourceRectangle.Left;
        var bridge = GenesisChannelRefreshButtonBridge.TryCreate(
            _owner,
            parent,
            _sourceEdit,
            _sourceRectangle,
            refreshLeft,
            _sourceRectangle.Top,
            showButton ? RefreshWidth : 1,
            height,
            new GenesisMainWindowInteraction(viewModel, session),
            showButton);
        if (bridge is null)
        {
            _ = RestoreSourceEdit();
        }

        return bridge;
    }

    internal static nint ResolveParentForOpen(
        nint sourceEdit,
        Func<nint, nint> getParent)
    {
        ArgumentNullException.ThrowIfNull(getParent);
        return sourceEdit == 0 ? 0 : getParent(sourceEdit);
    }

    private bool RestoreSourceEdit() =>
        IsWindow(_sourceEdit) &&
        MoveWindow(
            _sourceEdit,
            _sourceRectangle.Left,
            _sourceRectangle.Top,
            Math.Max(1, _sourceRectangle.Right - _sourceRectangle.Left),
            Math.Max(1, _sourceRectangle.Bottom - _sourceRectangle.Top),
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref WindowPoint point);

}

internal sealed class GenesisChannelRefreshButtonBridge : IAsyncDisposable
{
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageSetFont = 0x0030;
    private const int ButtonNotificationClicked = 0;
    private const int DefaultGuiFont = 17;
    private static readonly ConcurrentDictionary<nint, RefreshState> StatesByContainer = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private readonly nint _sourceEdit;
    private readonly GenesisChannelRefreshButtonPlacement.WindowRectangle _sourceRectangle;
    private readonly nint _container;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisChannelRefreshButtonBridge(
        nint sourceEdit,
        GenesisChannelRefreshButtonPlacement.WindowRectangle sourceRectangle,
        nint container,
        string className,
        nint instance)
    {
        _sourceEdit = sourceEdit;
        _sourceRectangle = sourceRectangle;
        _container = container;
        _className = className;
        _instance = instance;
    }

    public static GenesisChannelRefreshButtonBridge? TryCreate(
        nint owner,
        nint parent,
        nint sourceEdit,
        GenesisChannelRefreshButtonPlacement.WindowRectangle sourceRectangle,
        int left,
        int top,
        int width,
        int height,
        GenesisMainWindowInteraction interaction,
        bool showButton = true)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var instance = GetModuleHandleW(null);
        if (owner == 0 || parent == 0 || sourceEdit == 0 || instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisRefresh-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            BackgroundBrush = GenesisNativeWindowClassPresentation.WindowBackgroundBrush,
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
            WindowStyleChild | (showButton ? WindowStyleVisible : 0),
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

        var button = CreateWindowExW(
            0,
            "BUTTON",
            showButton ? "Refresh List" : string.Empty,
            WindowStyleChild |
                (showButton ? WindowStyleVisible : 0) |
                WindowStyleTabStop,
            0,
            0,
            width,
            height,
            container,
            0,
            0,
            0);
        if (button == 0)
        {
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        ApplyDefaultFont(button);
        var state = new RefreshState(owner, button, interaction);
        if (!StatesByContainer.TryAdd(container, state))
        {
            _ = DestroyWindow(container);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        return new(sourceEdit, sourceRectangle, container, className, instance);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            StatesByContainer.TryRemove(_container, out _);
            if (_container != 0 && IsWindow(_container))
            {
                _ = DestroyWindow(_container);
            }

            _ = UnregisterClassW(_className, _instance);
            if (_sourceEdit != 0 && IsWindow(_sourceEdit))
            {
                _ = MoveWindow(
                    _sourceEdit,
                    _sourceRectangle.Left,
                    _sourceRectangle.Top,
                    Math.Max(1, _sourceRectangle.Right - _sourceRectangle.Left),
                    Math.Max(1, _sourceRectangle.Bottom - _sourceRectangle.Top),
                    true);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void ApplyDefaultFont(nint control)
    {
        var font = GetStockObject(DefaultGuiFont);
        if (font != 0)
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
            StatesByContainer.TryRemove(window, out _);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageCommand &&
            StatesByContainer.TryGetValue(window, out var state))
        {
            state.ProcessCommand(wParam, lParam);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    public static bool CanRefreshForOwner(nint owner) =>
        owner != 0 && StatesByContainer.Values.Any(
            state => state.Owner == owner && state.CanRefreshCurrentView);

    public static bool TryRefreshForOwner(nint owner) =>
        FindRefreshRequestForOwner(owner)?.Invoke() == true;

    internal static Func<bool>? FindRefreshRequestForOwner(nint owner)
    {
        if (owner == 0)
        {
            return null;
        }

        var state = StatesByContainer.Values.FirstOrDefault(
            candidate => candidate.Owner == owner);
        // Registration and request admission are separate: a registered route
        // remains present while its current refresh is pending or unavailable.
        return state is null ? null : state.RequestRefresh;
    }

    internal sealed class RefreshState
    {
        private readonly nint _owner;
        private readonly nint _button;
        private readonly GenesisMainWindowInteraction _interaction;
        private Task _pendingRefresh = Task.CompletedTask;

        public RefreshState(
            nint owner,
            nint button,
            GenesisMainWindowInteraction interaction)
        {
            _owner = owner;
            _button = button;
            _interaction = interaction;
        }

        public nint Owner => _owner;

        public bool CanRefreshCurrentView => _interaction.CanRefreshCurrentView;

        internal Task PendingRefresh => _pendingRefresh;

        public bool RequestRefresh()
        {
            var refreshRequestId = Guid.NewGuid();
            if (!_pendingRefresh.IsCompleted)
            {
                _interaction.RecordRefreshSuppressed(
                    refreshRequestId,
                    "RefreshAlreadyPending");
                return false;
            }

            if (!CanRefreshCurrentView)
            {
                _interaction.RecordRefreshSuppressed(
                    refreshRequestId,
                    "ViewCannotRefresh");
                return false;
            }

            _pendingRefresh = ObserveRefreshAsync(
                _interaction.RefreshCurrentViewAsync(refreshRequestId));
            return true;
        }

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode == ButtonNotificationClicked &&
                lParam == _button)
            {
                _ = RequestRefresh();
            }
        }

        private static async Task ObserveRefreshAsync(ValueTask refresh)
        {
            try
            {
                await refresh.ConfigureAwait(false);
            }
            catch
            {
                // Catalog refresh converts expected failures to bounded GUI state.
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

}

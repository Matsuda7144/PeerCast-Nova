using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisNetworkSettingsWindowContent(
    IReadOnlyList<string> StatusLines,
    string AvailabilityNote)
{
    public static GenesisNetworkSettingsWindowContent Create(
        GenesisNetworkSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var status = GenesisNetworkSettingsPresentation.Create(viewModel);
        return new(
            [
                $"Listener: {status.ListenerStateText}",
                $"Bound endpoint: {status.BoundEndpointText}",
                $"Relay connections: {status.RelayConnectionsText}",
                $"Direct listeners: {status.DirectListenersText}",
                $"IPv4 reachability: {status.Ipv4ReachabilityText}",
                $"IPv6 reachability: {status.Ipv6ReachabilityText}",
                $"External IPv4: {status.ExternalIpv4Text}",
                $"External IPv6: {status.ExternalIpv6Text}",
            ],
            CreateAvailabilityNote(viewModel));
    }

    private static string CreateAvailabilityNote(
        GenesisNetworkSettingsViewModel viewModel)
    {
        if (!viewModel.IsRuntimeStatusCurrent)
        {
            return "Network status refresh failed. Listener editing and port checks are unavailable until current backend status can be confirmed.";
        }

        return viewModel.Capabilities.SupportsSharedListener
            ? "Listener editing is not yet exposed by this native surface. Port reachability has not been checked."
            : "Application-wide listener editing and port checks are unavailable until supported by the active backend.";
    }
}

/// <summary>
/// Modeless, status-only Network / Relay surface. It deliberately exposes no
/// editable port or relay controls until the application controller reports the
/// corresponding capability as real, applied state.
/// </summary>
internal sealed partial class GenesisNetworkSettingsWindow : IAsyncDisposable
{
    private const uint WindowStyleCaption = 0x00C00000;
    private const uint WindowStyleSysMenu = 0x00080000;
    private const uint WindowStyleMinimizeBox = 0x00020000;
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowStyleClipChildren = 0x02000000;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const nuint VirtualKeyEnter = 0x0d;
    private const nuint VirtualKeyEscape = 0x1b;
    private const nuint CloseButtonSubclassId = 1;
    private const int ButtonNotificationClicked = 0;
    private const int DefaultGuiFont = 17;
    private const int ArrowCursor = 32512;
    private const int ColorWindow = 5;
    private const int ShowWindowNormal = 1;
    private const int UseDefaultPosition = unchecked((int)0x80000000);
    private const int WindowWidth = 570;
    private const int WindowHeight = 390;
    private const int CloseControlId = 2001;
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private static readonly Win32SubclassProcedure CloseButtonSubclassProcedureDelegate =
        ProcessCloseButtonMessage;

    private readonly nint _window;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisNetworkSettingsWindow(
        nint window,
        string className,
        nint instance)
    {
        _window = window;
        _className = className;
        _instance = instance;
    }

    public bool IsOpen =>
        Volatile.Read(ref _disposed) == 0 &&
        _window != 0 &&
        IsWindow(_window);

    public static GenesisNetworkSettingsWindow? TryCreate(
        nint owner,
        GenesisNetworkSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner))
        {
            return null;
        }

        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaNetworkRelay-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)ArrowCursor),
            BackgroundBrush = (nint)(ColorWindow + 1),
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            return null;
        }

        var dpi = GenesisMainWindowResizeGuard.GetDpi(owner);
        var window = CreateWindowExW(
            0,
            className,
            "通信 / リレー状態",
            WindowStyleCaption |
                WindowStyleSysMenu |
                WindowStyleMinimizeBox |
                WindowStyleClipChildren,
            UseDefaultPosition,
            UseDefaultPosition,
            GenesisMainWindowLayout.Scale(WindowWidth, dpi),
            GenesisMainWindowLayout.Scale(WindowHeight, dpi),
            owner,
            0,
            instance,
            0);
        if (window == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var content = GenesisNetworkSettingsWindowContent.Create(viewModel);
        if (!TryCreateControls(window, instance, content, dpi))
        {
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        _ = ShowWindow(window, ShowWindowNormal);
        _ = SetForegroundWindow(window);
        return new(window, className, instance);
    }

    public void Activate()
    {
        if (!IsOpen)
        {
            return;
        }

        _ = ShowWindow(_window, ShowWindowNormal);
        _ = SetForegroundWindow(_window);
    }

    public ValueTask DisposeAsync()
    {
        if (_natState is not null) return _natState.DisposeAsync();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        if (_window != 0 && IsWindow(_window))
        {
            _ = DestroyWindow(_window);
        }

        _ = UnregisterClassW(_className, _instance);
        return ValueTask.CompletedTask;
    }

    private static bool TryCreateControls(
        nint window,
        nint instance,
        GenesisNetworkSettingsWindowContent content,
        uint dpi)
    {
        var y = 18;
        foreach (var line in content.StatusLines)
        {
            if (CreateLabel(
                    window,
                    instance,
                    line,
                    GenesisMainWindowLayout.Scale(22, dpi),
                    GenesisMainWindowLayout.Scale(y, dpi),
                    GenesisMainWindowLayout.Scale(510, dpi),
                    GenesisMainWindowLayout.Scale(22, dpi)) == 0)
            {
                return false;
            }

            y += 30;
        }

        if (CreateLabel(
                window,
                instance,
                content.AvailabilityNote,
                GenesisMainWindowLayout.Scale(22, dpi),
                GenesisMainWindowLayout.Scale(y + 6, dpi),
                GenesisMainWindowLayout.Scale(510, dpi),
                GenesisMainWindowLayout.Scale(48, dpi)) == 0)
        {
            return false;
        }

        var closeButton = CreateControl(
            "BUTTON",
            "閉じる",
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop,
            GenesisMainWindowLayout.Scale(448, dpi),
            GenesisMainWindowLayout.Scale(306, dpi),
            GenesisMainWindowLayout.Scale(84, dpi),
            GenesisMainWindowLayout.Scale(28, dpi),
            window,
            CloseControlId,
            instance);
        if (closeButton == 0)
        {
            return false;
        }

        if (!SetWindowSubclass(
                closeButton,
                CloseButtonSubclassProcedureDelegate,
                CloseButtonSubclassId,
                unchecked((nuint)window)))
        {
            return false;
        }

        _ = RequestInitialFocus(closeButton, SetFocus);
        return true;
    }

    internal static bool RequestInitialFocus(
        nint control,
        Func<nint, nint> setFocus)
    {
        ArgumentNullException.ThrowIfNull(setFocus);
        if (control == 0)
        {
            return false;
        }

        _ = setFocus(control);
        return true;
    }

    internal static bool TryRequestCloseFromButtonKey(
        uint message,
        nuint virtualKey,
        nint parentWindow,
        Func<nint, bool> postClose)
    {
        ArgumentNullException.ThrowIfNull(postClose);
        if (message != WindowMessageKeyDown ||
            (virtualKey != VirtualKeyEnter && virtualKey != VirtualKeyEscape) ||
            parentWindow == 0)
        {
            return false;
        }

        return postClose(parentWindow);
    }

    private static bool PostClose(nint window) =>
        PostMessageW(window, WindowMessageClose, 0, 0);

    private static nint ProcessCloseButtonMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (TryRequestCloseFromButtonKey(
                message,
                wParam,
                unchecked((nint)referenceData),
                PostClose))
        {
            return 0;
        }

        if (message == WindowMessageNcDestroy)
        {
            _ = RemoveWindowSubclass(
                window,
                CloseButtonSubclassProcedureDelegate,
                subclassId);
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static nint CreateLabel(
        nint parent,
        nint instance,
        string text,
        int x,
        int y,
        int width,
        int height) =>
        CreateControl(
            "STATIC",
            text,
            WindowStyleChild | WindowStyleVisible,
            x,
            y,
            width,
            height,
            parent,
            0,
            instance);

    private static nint CreateControl(
        string className,
        string text,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        int controlId,
        nint instance)
    {
        var control = CreateWindowExW(
            0,
            className,
            text,
            style,
            x,
            y,
            width,
            height,
            parent,
            (nint)controlId,
            instance,
            0);
        if (control != 0)
        {
            ApplyDefaultFont(control);
        }

        return control;
    }

    private static void ApplyDefaultFont(nint control) => _ = GenesisWindowUiFont.Apply(control);

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (TryProcessNatMessage(window, message, wParam, lParam, out var result)) return result;

        if (message == WindowMessageClose)
        {
            _ = DestroyWindow(window);
            return 0;
        }

        if (message == WindowMessageCommand)
        {
            var controlId = (int)(wParam & 0xffff);
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (controlId == CloseControlId &&
                notificationCode == ButtonNotificationClicked)
            {
                _ = DestroyWindow(window);
                return 0;
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
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

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed class GenesisAboutButtonPlacement
{
    private const int ButtonWidth = 72;
    private const int ButtonGap = 8;
    private readonly nint _owner;
    private readonly nint _sourceEdit;

    private GenesisAboutButtonPlacement(
        nint owner,
        nint sourceEdit)
    {
        _owner = owner;
        _sourceEdit = sourceEdit;
    }

    public static GenesisAboutButtonPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var owner = (nint)windowToken;
        var sourceEdit = GenesisMainWindowControlRegistry.ResolveSourceEdit(owner);
        if (sourceEdit == 0)
        {
            return null;
        }

        return new(owner, sourceEdit);
    }

    public IAsyncDisposable? Open(bool showButton = true)
    {
        var material = NovaAboutLegalMaterial.Load();
        if (material.Result != NovaAboutLegalMaterialResult.Loaded ||
            material.Model is null)
        {
            return null;
        }

        var parent = GetParent(_sourceEdit);
        if (parent == 0 || !GetWindowRect(_sourceEdit, out var rectangle))
        {
            return null;
        }

        var topLeft = new Win32Point(rectangle.Left, rectangle.Top);
        var bottomRight = new Win32Point(rectangle.Right, rectangle.Bottom);
        if (!ScreenToClient(parent, ref topLeft) ||
            !ScreenToClient(parent, ref bottomRight))
        {
            return null;
        }

        var sourceRectangle = new WindowRectangle(
            topLeft.X,
            topLeft.Y,
            bottomRight.X,
            bottomRight.Y);
        var originalWidth = Math.Max(
            1,
            sourceRectangle.Right - sourceRectangle.Left);
        var height = Math.Max(1, sourceRectangle.Bottom - sourceRectangle.Top);
        var reducedWidth = showButton
            ? originalWidth - ButtonWidth - ButtonGap
            : originalWidth;
        if (showButton &&
            (reducedWidth < 120 ||
                !MoveWindow(
                    _sourceEdit,
                    sourceRectangle.Left,
                    sourceRectangle.Top,
                    reducedWidth,
                    height,
                    true)))
        {
            return null;
        }

        var bridge = GenesisAboutButtonBridge.TryCreate(
            _owner,
            parent,
            _sourceEdit,
            sourceRectangle,
            showButton
                ? sourceRectangle.Left + reducedWidth + ButtonGap
                : sourceRectangle.Left,
            sourceRectangle.Top,
            showButton ? ButtonWidth : 1,
            height,
            material.Model,
            showButton);
        if (bridge is null)
        {
            _ = RestoreSourceEdit(sourceRectangle);
        }

        return bridge;
    }

    private bool RestoreSourceEdit(WindowRectangle sourceRectangle) =>
        IsWindow(_sourceEdit) &&
        MoveWindow(
            _sourceEdit,
            sourceRectangle.Left,
            sourceRectangle.Top,
            Math.Max(1, sourceRectangle.Right - sourceRectangle.Left),
            Math.Max(1, sourceRectangle.Bottom - sourceRectangle.Top),
            true);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct WindowRectangle(
        int Left,
        int Top,
        int Right,
        int Bottom);
}

internal sealed class GenesisAboutButtonBridge : IAsyncDisposable
{
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageSetFont = 0x0030;
    private const int ButtonNotificationClicked = 0;
    private const int DefaultGuiFont = 17;
    private static readonly ConcurrentDictionary<nint, AboutState> StatesByContainer = new();
    private static readonly Win32WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private readonly nint _sourceEdit;
    private readonly GenesisAboutButtonPlacement.WindowRectangle _sourceRectangle;
    private readonly nint _container;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisAboutButtonBridge(
        nint sourceEdit,
        GenesisAboutButtonPlacement.WindowRectangle sourceRectangle,
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

    public static GenesisAboutButtonBridge? TryCreate(
        nint owner,
        nint parent,
        nint sourceEdit,
        GenesisAboutButtonPlacement.WindowRectangle sourceRectangle,
        int left,
        int top,
        int width,
        int height,
        NovaAboutLegalModel model,
        bool showButton = true)
    {
        ArgumentNullException.ThrowIfNull(model);
        var instance = GetModuleHandleW(null);
        if (owner == 0 || parent == 0 || sourceEdit == 0 || instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisAbout-{Guid.NewGuid():N}";
        var windowClass = new Win32WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<Win32WindowClassEx>(),
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
            showButton ? "About" : string.Empty,
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
        var state = new AboutState(
            owner,
            button,
            new GenesisAboutInteraction(
                model,
                new SystemGenesisAboutDialog(owner)));
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
            _ = StatesByContainer.TryRemove(_container, out _);
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

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageNcDestroy)
        {
            _ = StatesByContainer.TryRemove(window, out _);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageCommand &&
            StatesByContainer.TryGetValue(window, out var state))
        {
            state.ProcessCommand(wParam, lParam);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static void ApplyDefaultFont(nint control)
    {
        var font = GetStockObject(DefaultGuiFont);
        if (font != 0)
        {
            _ = SendMessageW(control, WindowMessageSetFont, (nuint)font, 1);
        }
    }

    private sealed class AboutState(
        nint owner,
        nint button,
        GenesisAboutInteraction interaction)
    {
        public nint Owner => owner;

        public void Show() => _ = interaction.Show();

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode == ButtonNotificationClicked && lParam == button)
            {
                _ = interaction.Show();
            }
        }
    }

    public static bool TryShowForOwner(nint owner)
    {
        if (owner == 0)
        {
            return false;
        }

        var state = StatesByContainer.Values.FirstOrDefault(
            candidate => candidate.Owner == owner);
        if (state is null)
        {
            return false;
        }

        state.Show();
        return true;
    }
}

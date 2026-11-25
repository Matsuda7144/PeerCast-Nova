using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextSettingsSurfaceFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextSettingsSurfaceFeedback
{
    private const string UnavailableMessage =
        "Settings could not be initialized. Notification, Yellow Page, Network / Relay, and Playback settings are unavailable for this session.";
    private const string Caption = "Settings";

    public static GenesisGuiNextSettingsSurfaceFeedbackPresentation? Resolve(
        bool available) =>
        available ? null : new(UnavailableMessage, Caption);

    public static void ReportIfUnavailable(
        bool available,
        Action<GenesisGuiNextSettingsSurfaceFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        var presentation = Resolve(available);
        if (presentation is null)
        {
            return;
        }

        try
        {
            present(presentation);
        }
        catch
        {
            // Settings-surface feedback is auxiliary presentation. It must never
            // reject the otherwise usable main GUI lifetime.
        }
    }
}

internal static class SystemGenesisGuiNextSettingsSurfaceFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static void ReportIfUnavailable(nint owner, bool available)
    {
        if (available ||
            !OperatingSystem.IsWindows() ||
            owner == 0 ||
            !IsWindow(owner))
        {
            return;
        }

        GenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(
            available: false,
            presentation =>
            {
                _ = MessageBoxW(
                    owner,
                    presentation.Message,
                    presentation.Caption,
                    MessageBoxOk | MessageBoxIconWarning);
            });
    }

}

internal enum GenesisChannelNotificationSettingsCommand : uint
{
    None = 0,
    NotifyNewChannels = 1,
    NotifyFavoriteChannels = 2,
    PlayNotificationSound = 3,
    TestSound = 4,
    YellowPages = 5,
    NetworkRelay = 6,
    Playback = 7,
    Recording = 8,
    Connection = 9,
    Appearance = 10,
}

internal static class GenesisChannelNotificationSettingsMenuInteraction
{
    public static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            settings,
            command,
            showYellowPagesAsync: null,
            showNetworkRelayAsync: null,
            showPlaybackAsync: null,
            cancellationToken: cancellationToken);

    internal static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            settings,
            command,
            showYellowPagesAsync,
            showNetworkRelayAsync: null,
            showPlaybackAsync: null,
            cancellationToken);

    internal static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync,
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            settings,
            command,
            showYellowPagesAsync,
            showNetworkRelayAsync,
            showPlaybackAsync: null,
            cancellationToken);

    internal static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync,
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync,
        Func<CancellationToken, ValueTask>? showPlaybackAsync,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(settings, command, showYellowPagesAsync, showNetworkRelayAsync,
            showPlaybackAsync, showRecordingAsync: null, cancellationToken);

    internal static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync,
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync,
        Func<CancellationToken, ValueTask>? showPlaybackAsync,
        Func<CancellationToken, ValueTask>? showRecordingAsync,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(settings, command, showYellowPagesAsync, showNetworkRelayAsync,
            showPlaybackAsync, showRecordingAsync, showConnectionAsync: null, cancellationToken);

    internal static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync,
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync,
        Func<CancellationToken, ValueTask>? showPlaybackAsync,
        Func<CancellationToken, ValueTask>? showRecordingAsync,
        Func<CancellationToken, ValueTask>? showConnectionAsync,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(settings, command, showYellowPagesAsync, showNetworkRelayAsync, showPlaybackAsync,
            showRecordingAsync, showConnectionAsync, null, cancellationToken);

    internal static ValueTask ApplyAsync(
        GenesisChannelNotificationSettings settings,
        GenesisChannelNotificationSettingsCommand command,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync,
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync,
        Func<CancellationToken, ValueTask>? showPlaybackAsync,
        Func<CancellationToken, ValueTask>? showRecordingAsync,
        Func<CancellationToken, ValueTask>? showConnectionAsync,
        Func<CancellationToken, ValueTask>? showAppearanceAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var current = settings.Current;
        switch (command)
        {
            case GenesisChannelNotificationSettingsCommand.Appearance:
                return showAppearanceAsync is null ? ValueTask.CompletedTask : showAppearanceAsync(cancellationToken);
            case GenesisChannelNotificationSettingsCommand.Connection:
                return showConnectionAsync is null
                    ? ValueTask.CompletedTask
                    : showConnectionAsync(cancellationToken);
            case GenesisChannelNotificationSettingsCommand.NotifyNewChannels:
                settings.SetNotifyNewChannels(!current.NotifyNewChannels);
                return ValueTask.CompletedTask;
            case GenesisChannelNotificationSettingsCommand.NotifyFavoriteChannels:
                settings.SetNotifyFavoriteChannels(!current.NotifyFavoriteChannels);
                return ValueTask.CompletedTask;
            case GenesisChannelNotificationSettingsCommand.PlayNotificationSound:
                settings.SetPlaySound(!current.PlaySound);
                return ValueTask.CompletedTask;
            case GenesisChannelNotificationSettingsCommand.TestSound:
                return settings.TestSoundAsync(cancellationToken);
            case GenesisChannelNotificationSettingsCommand.YellowPages:
                return showYellowPagesAsync is null
                    ? ValueTask.CompletedTask
                    : showYellowPagesAsync(cancellationToken);
            case GenesisChannelNotificationSettingsCommand.NetworkRelay:
                return showNetworkRelayAsync is null
                    ? ValueTask.CompletedTask
                    : showNetworkRelayAsync(cancellationToken);
            case GenesisChannelNotificationSettingsCommand.Recording:
                return showRecordingAsync is null
                    ? ValueTask.CompletedTask
                    : showRecordingAsync(cancellationToken);
            case GenesisChannelNotificationSettingsCommand.Playback:
                return showPlaybackAsync is null
                    ? ValueTask.CompletedTask
                    : showPlaybackAsync(cancellationToken);
            default:
                return ValueTask.CompletedTask;
        }
    }
}

internal sealed class GenesisChannelNotificationSettingsButtonPlacement
{
    private const int ButtonWidth = 72;
    private const int ButtonGap = 8;
    private readonly nint _owner;
    private readonly nint _sourceEdit;
    private readonly GenesisAppearanceSettings? _appearance;

    private GenesisChannelNotificationSettingsButtonPlacement(
        nint owner,
        nint sourceEdit, GenesisAppearanceSettings? appearance)
    {
        _owner = owner;
        _sourceEdit = sourceEdit;
        _appearance = appearance;
    }

    public static GenesisChannelNotificationSettingsButtonPlacement? Capture(
        long windowToken, GenesisAppearanceSettings? appearance = null)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var owner = (nint)windowToken;
        var sourceEdit = FindWindowExW(owner, 0, "EDIT", null);
        var placement = sourceEdit == 0
            ? null
            : new GenesisChannelNotificationSettingsButtonPlacement(owner, sourceEdit, appearance);
        SystemGenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(
            owner,
            placement is not null);
        return placement;
    }

    public IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        bool showButton = true) =>
        OpenCore(
            settings,
            yellowPages: null,
            networkSettingsController: null,
            playbackSettings: null,
            showButton: showButton);

    public IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration yellowPages,
        bool showButton = true)
    {
        ArgumentNullException.ThrowIfNull(yellowPages);
        return OpenCore(
            settings,
            yellowPages,
            GenesisUnavailableNetworkSettingsController.Instance,
            playbackSettings: null,
            showButton: showButton);
    }

    internal IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration yellowPages,
        IGenesisNetworkSettingsController networkSettingsController,
        bool showButton = true)
    {
        ArgumentNullException.ThrowIfNull(yellowPages);
        ArgumentNullException.ThrowIfNull(networkSettingsController);
        return OpenCore(
            settings,
            yellowPages,
            networkSettingsController,
            playbackSettings: null,
            showButton);
    }

    internal IAsyncDisposable? Open(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration yellowPages,
        GenesisGuiNextPlaybackSettings playbackSettings,
        IGenesisNetworkSettingsController networkSettingsController,
        bool showButton = true,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        IGuiNextNetworkSettingsAdapter? networkAdapter = null,
        GenesisGuiNextConnectionSettings? connectionSettings = null)
    {
        ArgumentNullException.ThrowIfNull(yellowPages);
        ArgumentNullException.ThrowIfNull(playbackSettings);
        ArgumentNullException.ThrowIfNull(networkSettingsController);
        return OpenCore(
            settings,
            yellowPages,
            networkSettingsController,
            playbackSettings,
            showButton, recordingSettings, networkAdapter, connectionSettings);
    }

    private IAsyncDisposable? OpenCore(
        GenesisChannelNotificationSettings settings,
        GenesisYellowPageConfiguration? yellowPages,
        IGenesisNetworkSettingsController? networkSettingsController,
        GenesisGuiNextPlaybackSettings? playbackSettings,
        bool showButton,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        IGuiNextNetworkSettingsAdapter? networkAdapter = null,
        GenesisGuiNextConnectionSettings? connectionSettings = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var parent = GetParent(_sourceEdit);
        if (parent == 0 || !GetWindowRect(_sourceEdit, out var rectangle))
        {
            return FailOpen();
        }

        var topLeft = new WindowPoint(rectangle.Left, rectangle.Top);
        var bottomRight = new WindowPoint(rectangle.Right, rectangle.Bottom);
        if (!ScreenToClient(parent, ref topLeft) ||
            !ScreenToClient(parent, ref bottomRight))
        {
            return FailOpen();
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
            return FailOpen();
        }

        var yellowPageHost = yellowPages is null
            ? null
            : new GenesisYellowPageSettingsWindowHost(_owner, yellowPages);
        var networkHost = networkSettingsController is null
            ? null
            : new GenesisNetworkSettingsWindowHost(_owner, networkSettingsController, networkAdapter);
        var connectionHost = connectionSettings is null || networkHost is null
            ? null
            : new GenesisGuiNextConnectionSettingsWindowHost(_owner, connectionSettings, networkHost.ShowAsync);
        var playbackHost = playbackSettings is null
            ? null
            : new GenesisGuiNextPlaybackSettingsWindowHost(_owner, playbackSettings);
        var appearanceHost = _appearance is null ? null : new GenesisAppearanceSettingsWindowHost(_owner, _appearance);
        var recordingHost = recordingSettings is null
            ? null
            : new GenesisRecordingOutputDirectorySettingsWindowHost(_owner, recordingSettings);
        Func<CancellationToken, ValueTask>? showRecordingAsync =
            recordingHost is null ? null : recordingHost.ShowAsync;
        Func<CancellationToken, ValueTask>? showYellowPagesAsync =
            yellowPageHost is null ? null : yellowPageHost.ShowAsync;
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync =
            networkHost is null ? null : networkHost.ShowAsync;
        Func<CancellationToken, ValueTask>? showPlaybackAsync =
            playbackHost is null ? null : playbackHost.ShowAsync;
        Func<CancellationToken, ValueTask>? showConnectionAsync =
            connectionHost is null ? null : connectionHost.ShowAsync;
        var bridge = GenesisChannelNotificationSettingsButtonBridge.TryCreate(
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
            settings,
            showYellowPagesAsync,
            showNetworkRelayAsync,
            showPlaybackAsync,
            showButton, showRecordingAsync, showConnectionAsync, appearanceHost is null ? null : appearanceHost.ShowAsync);
        if (bridge is null)
        {
            _ = RestoreSourceEdit(sourceRectangle);
            return FailOpen();
        }

        return yellowPageHost is null && networkHost is null && playbackHost is null && recordingHost is null && connectionHost is null && appearanceHost is null
            ? bridge
            : new SettingsLifetime(bridge, yellowPageHost, networkHost, playbackHost, recordingHost, connectionHost, appearanceHost);
    }

    private IAsyncDisposable? FailOpen()
    {
        SystemGenesisGuiNextSettingsSurfaceFeedback.ReportIfUnavailable(
            _owner,
            available: false);
        return null;
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

    private sealed class SettingsLifetime : IAsyncDisposable
    {
        private readonly IAsyncDisposable _bridge;
        private readonly GenesisYellowPageSettingsWindowHost? _yellowPageHost;
        private readonly GenesisNetworkSettingsWindowHost? _networkHost;
        private readonly GenesisGuiNextPlaybackSettingsWindowHost? _playbackHost;
        private readonly GenesisRecordingOutputDirectorySettingsWindowHost? _recordingHost;
        private readonly GenesisGuiNextConnectionSettingsWindowHost? _connectionHost;
        private readonly GenesisAppearanceSettingsWindowHost? _appearanceHost;
        private int _disposed;

        public SettingsLifetime(
            IAsyncDisposable bridge,
            GenesisYellowPageSettingsWindowHost? yellowPageHost,
            GenesisNetworkSettingsWindowHost? networkHost,
            GenesisGuiNextPlaybackSettingsWindowHost? playbackHost,
            GenesisRecordingOutputDirectorySettingsWindowHost? recordingHost,
            GenesisGuiNextConnectionSettingsWindowHost? connectionHost, GenesisAppearanceSettingsWindowHost? appearanceHost)
        {
            _bridge = bridge;
            _yellowPageHost = yellowPageHost;
            _networkHost = networkHost;
            _playbackHost = playbackHost;
            _recordingHost = recordingHost;
            _connectionHost = connectionHost;
            _appearanceHost = appearanceHost;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                try
                {
                    if (_appearanceHost is not null) await _appearanceHost.DisposeAsync().ConfigureAwait(false);
                    if (_connectionHost is not null)
                        await _connectionHost.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (_recordingHost is not null)
                    {
                        await _recordingHost.DisposeAsync().ConfigureAwait(false);
                    }
                    if (_networkHost is not null)
                    {
                        await _networkHost.DisposeAsync().ConfigureAwait(false);
                    }
                    if (_playbackHost is not null)
                    {
                        await _playbackHost.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                try
                {
                    if (_yellowPageHost is not null)
                    {
                        await _yellowPageHost.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await _bridge.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint window,
        out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref WindowPoint point);

}

internal sealed class GenesisChannelNotificationSettingsButtonBridge : IAsyncDisposable
{
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowMessageNull = 0x0000;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint MenuFlagString = 0x0000;
    private const uint MenuFlagChecked = 0x0008;
    private const uint MenuFlagSeparator = 0x0800;
    private const uint TrackPopupMenuNoNotify = 0x0080;
    private const uint TrackPopupMenuReturnCommand = 0x0100;
    private const int ButtonNotificationClicked = 0;
    private const int DefaultGuiFont = 17;
    private static readonly ConcurrentDictionary<nint, SettingsState> StatesByContainer = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private readonly nint _sourceEdit;
    private readonly GenesisChannelNotificationSettingsButtonPlacement.WindowRectangle
        _sourceRectangle;
    private readonly nint _container;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisChannelNotificationSettingsButtonBridge(
        nint sourceEdit,
        GenesisChannelNotificationSettingsButtonPlacement.WindowRectangle sourceRectangle,
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

    public static GenesisChannelNotificationSettingsButtonBridge? TryCreate(
        nint owner,
        nint parent,
        nint sourceEdit,
        GenesisChannelNotificationSettingsButtonPlacement.WindowRectangle sourceRectangle,
        int left,
        int top,
        int width,
        int height,
        GenesisChannelNotificationSettings settings,
        Func<CancellationToken, ValueTask>? showYellowPagesAsync = null,
        Func<CancellationToken, ValueTask>? showNetworkRelayAsync = null,
        Func<CancellationToken, ValueTask>? showPlaybackAsync = null,
        bool showButton = true,
        Func<CancellationToken, ValueTask>? showRecordingAsync = null,
        Func<CancellationToken, ValueTask>? showConnectionAsync = null,
        Func<CancellationToken, ValueTask>? showAppearanceAsync = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var instance = GetModuleHandleW(null);
        if (owner == 0 || parent == 0 || sourceEdit == 0 || instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaGenesisSettings-{Guid.NewGuid():N}";
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
            showButton ? "Settings" : string.Empty,
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
        if (!StatesByContainer.TryAdd(
                container,
                new SettingsState(
                    owner,
                    button,
                    settings,
                    showYellowPagesAsync,
                    showNetworkRelayAsync,
                    showPlaybackAsync, showRecordingAsync, showConnectionAsync, showAppearanceAsync)))
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

    private sealed class SettingsState
    {
        private readonly nint _owner;
        private readonly nint _button;
        private readonly GenesisChannelNotificationSettings _settings;
        private readonly Func<CancellationToken, ValueTask>? _showYellowPagesAsync;
        private readonly Func<CancellationToken, ValueTask>? _showNetworkRelayAsync;
        private readonly Func<CancellationToken, ValueTask>? _showPlaybackAsync;
        private readonly Func<CancellationToken, ValueTask>? _showRecordingAsync;
        private readonly Func<CancellationToken, ValueTask>? _showConnectionAsync;
        private readonly Func<CancellationToken, ValueTask>? _showAppearanceAsync;

        public SettingsState(
            nint owner,
            nint button,
            GenesisChannelNotificationSettings settings,
            Func<CancellationToken, ValueTask>? showYellowPagesAsync,
            Func<CancellationToken, ValueTask>? showNetworkRelayAsync,
            Func<CancellationToken, ValueTask>? showPlaybackAsync,
            Func<CancellationToken, ValueTask>? showRecordingAsync,
            Func<CancellationToken, ValueTask>? showConnectionAsync,
            Func<CancellationToken, ValueTask>? showAppearanceAsync)
        {
            _owner = owner;
            _button = button;
            _settings = settings;
            _showYellowPagesAsync = showYellowPagesAsync;
            _showNetworkRelayAsync = showNetworkRelayAsync;
            _showPlaybackAsync = showPlaybackAsync;
            _showRecordingAsync = showRecordingAsync;
            _showConnectionAsync = showConnectionAsync;
            _showAppearanceAsync = showAppearanceAsync;
        }

        public nint Owner => _owner;

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode != ButtonNotificationClicked || lParam != _button)
            {
                return;
            }

            _ = OpenMenu(_button);
        }

        public bool OpenMenu(nint anchorWindow)
        {
            var command = ShowMenu(anchorWindow);
            if (command is null)
            {
                SystemGenesisGuiNextSettingsMenuFeedback.ReportOpenFailure(_owner);
                return false;
            }

            if (command.Value != GenesisChannelNotificationSettingsCommand.None)
            {
                _ = ObserveSelectionAsync(command.Value);
            }

            return true;
        }

        private GenesisChannelNotificationSettingsCommand? ShowMenu(nint anchorWindow)
        {
            if (!GetWindowRect(anchorWindow != 0 ? anchorWindow : _button, out var rectangle))
            {
                return null;
            }

            var menu = CreatePopupMenu();
            if (menu == 0)
            {
                return null;
            }

            try
            {
                var current = _settings.Current;
                if (!AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.NotifyNewChannels,
                        "Notify new channels",
                        current.NotifyNewChannels) ||
                    !AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.NotifyFavoriteChannels,
                        "Notify favorite channels",
                        current.NotifyFavoriteChannels) ||
                    !AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.PlayNotificationSound,
                        "Play notification sound",
                        current.PlaySound) ||
                    !AppendMenuW(menu, MenuFlagSeparator, 0, null) ||
                    !AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.TestSound,
                        "Test Sound",
                        isChecked: false))
                {
                    return null;
                }

                if ((_showYellowPagesAsync is not null ||
                        _showConnectionAsync is not null ||
                        _showNetworkRelayAsync is not null ||
                        _showPlaybackAsync is not null ||
                        _showRecordingAsync is not null || _showAppearanceAsync is not null) &&
                    !AppendMenuW(menu, MenuFlagSeparator, 0, null))
                {
                    return null;
                }

                if (_showYellowPagesAsync is not null &&
                    !AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.YellowPages,
                        "Yellow Pages...",
                        isChecked: false))
                {
                    return null;
                }

                if (_showConnectionAsync is not null &&
                    !AppendSetting(menu, GenesisChannelNotificationSettingsCommand.Connection,
                        "接続 / 帯域 / リレー数...", isChecked: false))
                {
                    return null;
                }

                if (_showNetworkRelayAsync is not null &&
                    !AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.NetworkRelay,
                        "Network / Relay...",
                        isChecked: false))
                {
                    return null;
                }

                if (_showPlaybackAsync is not null &&
                    !AppendSetting(
                        menu,
                        GenesisChannelNotificationSettingsCommand.Playback,
                        "Playback / External Player...",
                        isChecked: false))
                {
                    return null;
                }

                if (_showRecordingAsync is not null &&
                    !AppendSetting(menu, GenesisChannelNotificationSettingsCommand.Recording,
                        "録画保存先...", isChecked: false))
                {
                    return null;
                }

                if (_showAppearanceAsync is not null &&
                    !AppendSetting(menu, GenesisChannelNotificationSettingsCommand.Appearance, "外観設定...", false)) return null;

                _ = SetForegroundWindow(_owner);
                var selected = TrackPopupMenu(
                    menu,
                    TrackPopupMenuNoNotify | TrackPopupMenuReturnCommand,
                    rectangle.Left,
                    rectangle.Bottom,
                    0,
                    _owner,
                    0);
                _ = PostMessageW(_owner, WindowMessageNull, 0, 0);
                return (GenesisChannelNotificationSettingsCommand)selected;
            }
            finally
            {
                _ = DestroyMenu(menu);
            }
        }

        private static bool AppendSetting(
            nint menu,
            GenesisChannelNotificationSettingsCommand command,
            string label,
            bool isChecked) =>
            AppendMenuW(
                menu,
                MenuFlagString | (isChecked ? MenuFlagChecked : 0),
                (nuint)command,
                label);

        private async Task ObserveSelectionAsync(
            GenesisChannelNotificationSettingsCommand command)
        {
            try
            {
                await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(
                        _settings,
                        command,
                        _showYellowPagesAsync,
                        _showNetworkRelayAsync,
                        _showPlaybackAsync, showRecordingAsync: _showRecordingAsync,
                        showConnectionAsync: _showConnectionAsync, showAppearanceAsync: _showAppearanceAsync)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Settings actions are auxiliary and must not fault the GUI lifetime.
            }
        }
    }

    public static bool TryOpenForOwner(nint owner, nint anchorWindow)
    {
        if (owner == 0)
        {
            return false;
        }

        var state = StatesByContainer.Values.FirstOrDefault(
            candidate => candidate.Owner == owner);
        return state?.OpenMenu(anchorWindow) == true;
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
    private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(
        nint menu,
        uint flags,
        nuint identifier,
        string? text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint owner,
        nint rectangle);

}

using System.Diagnostics;
using System.Runtime.InteropServices;
using PeerCast.Nova.Playback.Bbs;

namespace PeerCast.Nova.Windows;

internal sealed record LibMpvPlaybackWindowOptions
{
    public string Title { get; init; } = "PeerCast Nova - FLV Playback";
    public string? StatusLabel { get; init; }
    public string? ContactUrl { get; init; }
    public int VideoWidth { get; init; } = 960;
    public int VideoHeight { get; init; } = 540;
    public LibMpvPlaybackAudioState InitialAudioState { get; init; } = LibMpvPlaybackAudioState.Default;
    public LibMpvPlaybackWindowState? InitialWindowState { get; init; }
    public string? ApplicationIconPath { get; init; } = Environment.ProcessPath;
    public PlaybackBbsSession? BbsSession { get; init; }
}

internal sealed class LibMpvPlaybackWindowHost : IAsyncDisposable
{
    // PeerCast Nova Modern palette (Win32 COLORREF: 0x00BBGGRR), matching GenesisGuiNextDesign.
    private const int ColorPanel = 0x0033230D;
    private const int ColorInput = 0x00422B10;
    private const int ColorBorder = 0x006D4920;
    private const int ColorSeparator = 0x00463016;
    private const int ColorSelected = 0x00C65B08;
    private const int ColorHover = 0x005B3512;
    private const int ColorAccent = 0x00FFBF00;
    private const int ColorText = 0x00FFF7EC;
    private const int ColorSecondaryText = 0x00CBB69A;
    private const int ColorDisabledText = 0x0093826C;

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const uint SpiGetHighContrast = 0x0042;
    private const uint HcfHighContrastOn = 0x0001;

    private const int StatusBarHeight = 20;
    private const int ThreadBarHeight = 20;
    private const int BbsInputBarHeight = 22;
    private const int BbsSendButtonWidth = 26;
    private const int VolumeButtonWidth = 58;
    private const int ClockWidth = 44;

    private const uint ClassStyleOwnDeviceContextDoubleClicks = 0x0028; // CS_OWNDC | CS_DBLCLKS
    private const uint WindowStyleVisible = 0x10000000; // WS_VISIBLE
    private const uint WindowStyleOverlappedClipChildren = 0x02CF0000;
    private const uint WindowStyleBorderlessClipChildren = 0x82000000; // WS_POPUP | WS_CLIPCHILDREN
    private const uint ChildVideoDisabled = 0x58000000;
    private const uint ChildOwnerDrawButton = 0x5000000B; // WS_CHILD | WS_VISIBLE | BS_OWNERDRAW
    private const uint ChildOwnerDrawButtonTabStop = 0x5001000B; // WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW
    private const uint ChildBbsInputEdit = 0x50811044; // WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_BORDER | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN
    private const int ShowWindowHide = 0;
    private const int ShowWindowNormal = 1;
    private const int ShowWindowMinimize = 6;
    private const int CursorArrow = 32512;
    private const int StockBlackBrush = 4;
    private const int StockDefaultGuiFont = 17;
    private const uint CommonControlsBarClasses = 0x00000004;

    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessageMove = 0x0003;
    private const uint WindowMessageSize = 0x0005;
    private const uint WindowMessageActivate = 0x0006;
    private const uint WindowMessagePaint = 0x000F;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDrawItem = 0x002B;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageNcHitTest = 0x0084;
    private const uint WindowMessageNcLeftButtonDown = 0x00A1;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageSysKeyDown = 0x0104;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageTimer = 0x0113;
    private const uint WindowMessageCtlColorEdit = 0x0133;
    private const uint WindowMessageMouseMove = 0x0200;
    private const uint WindowMessageLeftButtonDown = 0x0201;
    private const uint WindowMessageLeftButtonDblClk = 0x0203;
    private const uint WindowMessageRightButtonUp = 0x0205;
    private const uint WindowMessageMouseWheel = 0x020A;
    private const uint WindowMessageMoving = 0x0216;
    private const uint WindowMessageExitSizeMove = 0x0232;
    private const uint WindowMessageBbsStateChanged = 0x8001;
    private const uint WindowMessageBbsPostCompleted = 0x8002;
    private const uint WindowMessageApplyAutoScale = 0x8003;

    private const int GwlStyle = -16;
    private const nint HwndTopMost = -1;
    private const nint HwndNoTopMost = -2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorDefaultToNull = 0;

    private const int HitCaption = 2;
    private const int HitLeft = 10;
    private const int HitRight = 11;
    private const int HitTop = 12;
    private const int HitTopLeft = 13;
    private const int HitTopRight = 14;
    private const int HitBottom = 15;
    private const int HitBottomLeft = 16;
    private const int HitBottomRight = 17;
    private const int BorderlessResizeEdge = 6;

    private const uint OdsSelected = 0x0001;
    private const uint OdsFocus = 0x0010;
    private const int BkModeTransparent = 1;
    private const uint DrawTextSingleLineCenter = 0x00000025; // DT_CENTER | DT_VCENTER | DT_SINGLELINE
    private const uint DrawTextSingleLineCenterEllipsis = 0x00008825; // DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS
    private const uint DrawTextSingleLineLeftEllipsis = 0x00008824; // DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS
    private const uint DrawTextSingleLineRight = 0x00000826; // DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX

    private const int ButtonClicked = 0;
    private const int VolumeButtonId = 1001;
    private const int BbsThreadBarId = 1004;
    private const int BbsInputEditId = 1005;
    private const int BbsSendButtonId = 1006;

    private const nuint BbsHoverTimerId = 1;
    private const nuint ClockTimerId = 2;
    private const nuint CaptionOverlayTimerId = 3;
    private const uint MenuFlagString = 0x0000;
    private const uint MenuFlagPopup = 0x0010;
    private const uint MenuFlagSeparator = 0x0800;
    private const uint MenuFlagChecked = 0x0008;
    private const uint TrackPopupReturnCmd = 0x0100;
    private const uint TrackPopupBottomAlign = 0x0020;
    private const uint MenuCmdRefresh = 2001;
    private const uint MenuCmdToggleSage = 2002;
    private const uint MenuCmdOpenBrowser = 2003;
    private const uint MenuCmdToggleAlwaysOnTop = 2101;
    private const uint MenuCmdToggleBorderless = 2102;
    private const uint MenuCmdToggleFullscreen = 2103;
    private const uint MenuCmdToggleMute = 2104;
    private const uint MenuCmdScreenshot = 2105;
    private const uint MenuCmdOpenScreenshotFolder = 2106;
    private const uint MenuCmdTogglePause = 2107;
    private const uint MenuCmdReload = 2108;
    private const uint MenuCmdResizeSmall = 2110;
    private const uint MenuCmdResizeMedium = 2111;
    private const uint MenuCmdResizeHd = 2112;
    private const uint MenuCmdResizeLarge = 2113;
    private const uint MenuCmdBrightUp = 2120;
    private const uint MenuCmdBrightDown = 2121;
    private const uint MenuCmdContrastUp = 2122;
    private const uint MenuCmdContrastDown = 2123;
    private const uint MenuCmdSatUp = 2124;
    private const uint MenuCmdSatDown = 2125;
    private const uint MenuCmdGammaUp = 2126;
    private const uint MenuCmdGammaDown = 2127;
    private const uint MenuCmdVideoAdjustReset = 2128;
    private const uint MenuCmdScaleBase = 2140;
    private const uint MenuCmdToggleAutoResize100 = 2150;
    private const uint MenuCmdSpeedBase = 2160;
    private const uint MenuCmdSeekBack30 = 2170;
    private const uint MenuCmdSeekBack5 = 2171;
    private const uint MenuCmdSeekForward5 = 2172;
    private const uint MenuCmdSeekForward30 = 2173;
    private const uint MenuCmdVolumeUp = 2180;
    private const uint MenuCmdVolumeDown = 2181;
    private const uint MenuCmdVolumePresetBase = 2182;
    private const uint MenuCmdAudioDeviceBase = 2190;
    private const uint MenuCmdThreadBase = 3000;
    private const int MaxAudioDeviceMenuItems = 32;

    private const nuint VirtualKeyBack = 0x08;
    private const nuint VirtualKeyReturn = 0x0D;
    private const int VirtualKeyShift = 0x10;
    private const nuint VirtualKeyEscape = 0x1B;
    private const nuint VirtualKeySpace = 0x20;
    private const nuint VirtualKeyLeft = 0x25;
    private const nuint VirtualKeyUp = 0x26;
    private const nuint VirtualKeyRight = 0x27;
    private const nuint VirtualKeyDown = 0x28;
    private const nuint VirtualKeyB = 0x42;
    private const nuint VirtualKeyF = 0x46;
    private const nuint VirtualKeyM = 0x4D;
    private const nuint VirtualKeyR = 0x52;
    private const nuint VirtualKeyS = 0x53;
    private const nuint VirtualKeyT = 0x54;
    private const nuint VirtualKeyOemOpenBrackets = 0xDB;
    private const nuint VirtualKeyOemCloseBrackets = 0xDD;
    private static readonly int[] VolumePresets = [100, 80, 60, 40, 20];

    private readonly object _sync = new();
    private readonly LibMpvPlaybackWindowOptions _options;
    private readonly LibMpvPlaybackAudioController _audio;
    private readonly WindowProcedure _windowProcedure;
    private readonly TaskCompletionSource<uint> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private nint _window;
    private nint _videoWindow;
    private nint _volumeButton;
    private nint _bbsThreadBar;
    private nint _bbsInputEdit;
    private nint _bbsSendButton;
    private LibMpvPlaybackBbsPopup? _bbsPopup;
    private LibMpvPlaybackCaptionOverlay? _captionOverlay;
    private nint _panelBrush;
    private nint _inputBrush;
    private nint _borderBrush;
    private nint _separatorBrush;
    private nint _selectedBrush;
    private nint _hoverBrush;
    private nint _accentBrush;
    private bool _stopRequested;
    private bool _disposed;
    private bool _bbsUseSage = true;
    private bool _bbsPosting;
    private bool _bbsHoverTimerActive;
    private string? _bbsPostError;
    private bool _alwaysOnTop;
    private bool _borderless;
    private bool _borderlessMaximized;
    private bool _fullscreen;
    private bool _paused;
    private double _playbackSpeed = 1.0;
    private bool _captionOverlayTimerActive;
    private WindowRectangle _preMaximizeRect;
    private WindowRectangle _preFullscreenRect;
    private LibMpvPlaybackWindowState _windowState;
    private LibMpvVideoAdjustment _videoAdjustment = LibMpvVideoAdjustment.Default;
    private string? _statusStatsText;
    private int _nativeVideoWidth;
    private int _nativeVideoHeight;
    private bool _autoResizeTo100PercentOnStart;
    private bool _appliedInitialAutoResize;
    private bool _userResized;

    public LibMpvPlaybackWindowHost(LibMpvPlaybackWindowOptions? options = null)
    {
        _options = options is null ? new() : options with { };
        var initialState = (_options.InitialWindowState ?? LibMpvPlaybackWindowState.Default with
        {
            VideoWidth = _options.VideoWidth,
            VideoHeight = _options.VideoHeight,
            Volume = _options.InitialAudioState.Volume,
            Muted = _options.InitialAudioState.Muted,
        }).Normalized();
        _windowState = initialState;
        _alwaysOnTop = initialState.AlwaysOnTop;
        _borderless = initialState.Borderless;
        _bbsUseSage = initialState.BbsUseSage;
        _autoResizeTo100PercentOnStart = initialState.AutoResizeTo100PercentOnStart;
        _audio = new(initialState.AudioState);
        _audio.StateChanged += state =>
        {
            AudioStateChanged?.Invoke(state);
            UpdateSavedWindowState(s => s with { Volume = state.Volume, Muted = state.Muted });
        };
        _windowProcedure = ProcessWindowMessage;
    }

    public event Action<LibMpvPlaybackAudioState>? AudioStateChanged;

    public event Action<LibMpvPlaybackWindowState>? WindowStateChanged;

    public event Action? ScreenshotRequested;

    public event Action<bool>? PauseChanged;

    public event Action<double>? SpeedChanged;

    public event Action? ReloadRequested;

    public event Action<double>? SeekRequested;

    public Func<(IReadOnlyList<LibMpvAudioDevice> Devices, string? CurrentDevice)>? AudioDevicesProvider { get; set; }

    public event Action<string>? AudioDeviceSelected;

    public event Action<LibMpvVideoAdjustment, string>? VideoAdjustmentChanged;

    public void UpdateStatusStats(string? statsText)
    {
        Volatile.Write(ref _statusStatsText, statsText);
    }

    public void UpdateVideoStats(LibMpvPlaybackStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var formatted = stats.FormatCompact();
        if (!string.IsNullOrEmpty(formatted))
        {
            Volatile.Write(ref _statusStatsText, formatted);
        }

        if (stats.VideoWidth <= 0 || stats.VideoHeight <= 0)
        {
            return;
        }

        Volatile.Write(ref _nativeVideoWidth, stats.VideoWidth);
        Volatile.Write(ref _nativeVideoHeight, stats.VideoHeight);

        if (!_autoResizeTo100PercentOnStart || _appliedInitialAutoResize || _userResized)
        {
            return;
        }

        nint window;
        lock (_sync)
        {
            window = _window;
        }

        if (window != 0 && IsWindow(window))
        {
            _ = PostMessageW(window, WindowMessageApplyAutoScale, 0, 0);
        }
    }

    public LibMpvPlaybackAudioState AudioState => _audio.Current;

    public LibMpvPlaybackWindowState WindowState
    {
        get
        {
            lock (_sync)
            {
                return _windowState;
            }
        }
    }

    public Task Closed => _closed.Task;

    public async ValueTask<uint> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new LibMpvPlaybackException("Embedded libmpv playback requires Windows.");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                throw new InvalidOperationException("The embedded playback window has already been started.");
            }

            _thread = new Thread(RunWindowThread)
            {
                IsBackground = true,
                Name = "PeerCastNova-LibMpvWindow",
            };
            _thread.Start();
        }

        try
        {
            return await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        nint window;
        lock (_sync)
        {
            if (_disposed)
            {
                window = 0;
            }
            else
            {
                _disposed = true;
                _stopRequested = true;
                window = _window;
            }
        }

        if (window != 0 && IsWindow(window))
        {
            _ = PostMessageW(window, WindowMessageClose, 0, 0);
        }

        if (_thread is not null)
        {
            try
            {
                await _closed.Task.ConfigureAwait(false);
            }
            catch (LibMpvPlaybackException)
            {
                // Window startup or runtime failure is already reported to observers.
            }
        }
    }

    private void RunWindowThread()
    {
        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            FailStart("The embedded playback window could not resolve the application module.");
            return;
        }

        var commonControls = new InitCommonControlsExData
        {
            Size = (uint)Marshal.SizeOf<InitCommonControlsExData>(),
            Icc = CommonControlsBarClasses,
        };
        _ = InitCommonControlsEx(ref commonControls);

        CreateThemeBrushes();
        var mainClassName = $"PeerCastNovaLibMpvWindow-{Guid.NewGuid():N}";
        var videoClassName = $"PeerCastNovaLibMpvVideo-{Guid.NewGuid():N}";
        var mainClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = ClassStyleOwnDeviceContextDoubleClicks,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            BackgroundBrush = _panelBrush != 0 ? _panelBrush : (nint)16,
            ClassName = mainClassName,
        };
        var videoClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = ClassStyleOwnDeviceContextDoubleClicks,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(DefaultVideoWindowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            BackgroundBrush = GetStockObject(StockBlackBrush),
            ClassName = videoClassName,
        };

        ushort mainAtom = 0;
        ushort videoAtom = 0;
        nint window = 0;
        try
        {
            if (_options.ApplicationIconPath is { } iconPath)
            {
                _ = ExtractIconExW(iconPath, 0, out mainClass.Icon, out mainClass.SmallIcon, 1);
            }

            mainAtom = RegisterClassExW(ref mainClass);
            videoAtom = RegisterClassExW(ref videoClass);
            if (mainAtom == 0 || videoAtom == 0)
            {
                FailStart("The embedded playback window class could not be registered.");
                return;
            }

            var initial = WindowState;
            var clientWidth = Math.Max(LibMpvPlaybackWindowState.MinVideoWidth, initial.VideoWidth);
            var clientHeight = Math.Max(LibMpvPlaybackWindowState.MinVideoHeight, initial.VideoHeight) + BottomBarsHeight;
            var windowStyle = _borderless ? WindowStyleBorderlessClipChildren : WindowStyleOverlappedClipChildren;
            var rectangle = new WindowRectangle
            {
                Left = 0,
                Top = 0,
                Right = clientWidth,
                Bottom = clientHeight,
            };
            if (!AdjustWindowRectEx(ref rectangle, windowStyle, false, 0))
            {
                FailStart("The embedded playback window bounds could not be calculated.");
                return;
            }

            var initialX = int.MinValue;
            var initialY = int.MinValue;
            if (initial.Left is { } savedLeft && initial.Top is { } savedTop)
            {
                var probePoint = new WindowPoint { X = savedLeft + 40, Y = savedTop + 40 };
                if (MonitorFromPoint(probePoint, MonitorDefaultToNull) != 0)
                {
                    initialX = savedLeft;
                    initialY = savedTop;
                }
            }

            window = CreateWindowExW(
                0,
                mainClassName,
                _options.Title,
                windowStyle,
                initialX,
                initialY,
                rectangle.Right - rectangle.Left,
                rectangle.Bottom - rectangle.Top,
                0,
                0,
                instance,
                0);
            if (window == 0)
            {
                FailStart("The embedded playback window could not be created.");
                return;
            }

            ApplyDarkTitleBar(window);
            if (_alwaysOnTop)
            {
                ApplyAlwaysOnTop(window, alwaysOnTop: true);
            }

            _captionOverlay = new LibMpvPlaybackCaptionOverlay(instance, window);
            _captionOverlay.ButtonClicked += button => HandleCaptionOverlayButton(window, button);

            _videoWindow = CreateWindowExW(
                0,
                videoClassName,
                string.Empty,
                ChildVideoDisabled,
                0,
                0,
                clientWidth,
                clientHeight - BottomBarsHeight,
                window,
                0,
                instance,
                0);
            if (HasBbs)
            {
                _bbsThreadBar = CreateWindowExW(
                    0,
                    "BUTTON",
                    _options.BbsSession!.Snapshot.FormatThreadBarText(),
                    ChildOwnerDrawButton,
                    0,
                    0,
                    clientWidth,
                    ThreadBarHeight,
                    window,
                    (nint)BbsThreadBarId,
                    instance,
                    0);
                _bbsInputEdit = CreateWindowExW(
                    0,
                    "EDIT",
                    string.Empty,
                    ChildBbsInputEdit,
                    0,
                    0,
                    Math.Max(120, clientWidth - BbsSendButtonWidth - 2),
                    BbsInputBarHeight - 2,
                    window,
                    (nint)BbsInputEditId,
                    instance,
                    0);
                _bbsSendButton = CreateWindowExW(
                    0,
                    "BUTTON",
                    "▶",
                    ChildOwnerDrawButton,
                    0,
                    0,
                    BbsSendButtonWidth,
                    BbsInputBarHeight - 2,
                    window,
                    (nint)BbsSendButtonId,
                    instance,
                    0);
                _bbsPopup = new LibMpvPlaybackBbsPopup(instance, window);
                _bbsPopup.UpdatePosts(_options.BbsSession.Snapshot.Posts, _options.BbsSession.Snapshot.StatusMessage);
            }
            _volumeButton = CreateWindowExW(
                0,
                "BUTTON",
                _audio.Current.FormatVolumeLabel(),
                ChildOwnerDrawButton,
                0,
                0,
                VolumeButtonWidth,
                StatusBarHeight,
                window,
                (nint)VolumeButtonId,
                instance,
                0);

            if (_videoWindow == 0 || _volumeButton == 0 ||
                (HasBbs && (_bbsThreadBar == 0 || _bbsInputEdit == 0 || _bbsSendButton == 0)))
            {
                FailStart("The embedded playback window controls could not be created.");
                return;
            }

            var font = GetStockObject(StockDefaultGuiFont);
            if (font != 0)
            {
                _ = SendMessageW(_volumeButton, WindowMessageSetFont, (nuint)font, 1);
                if (HasBbs)
                {
                    _ = SendMessageW(_bbsThreadBar, WindowMessageSetFont, (nuint)font, 1);
                    _ = SendMessageW(_bbsInputEdit, WindowMessageSetFont, (nuint)font, 1);
                    _ = SendMessageW(_bbsSendButton, WindowMessageSetFont, (nuint)font, 1);
                }
            }

            SyncControls();
            LayoutControls(clientWidth, clientHeight);
            _ = SetTimer(window, ClockTimerId, 1000, 0);

            bool requestStop;
            lock (_sync)
            {
                if (_disposed)
                {
                    FailStart("The embedded playback window was closed before startup completed.");
                    return;
                }

                _window = window;
                requestStop = _stopRequested;
            }

            _ = ShowWindow(window, ShowWindowNormal);
            _ = UpdateWindow(window);
            var videoWindowId = unchecked((uint)_videoWindow.ToInt64());
            if (videoWindowId == 0)
            {
                FailStart("The embedded playback video surface handle was invalid.");
                return;
            }

            if (_options.BbsSession is { } bbsSession)
            {
                bbsSession.StateChanged += OnBbsStateChanged;
            }

            _started.TrySetResult(videoWindowId);
            if (requestStop)
            {
                _ = PostMessageW(window, WindowMessageClose, 0, 0);
            }

            while (true)
            {
                var messageResult = GetMessageW(out var message, 0, 0, 0);
                if (messageResult == -1)
                {
                    _closed.TrySetException(
                        new LibMpvPlaybackException("The embedded playback window message loop failed."));
                    return;
                }

                if (messageResult == 0)
                {
                    _closed.TrySetResult();
                    return;
                }

                if (message.Message == WindowMessageMouseMove)
                {
                    if (_borderless && !_fullscreen)
                    {
                        ShowCaptionOverlay(window);
                    }

                    if (HasBbs && message.Window == _bbsThreadBar)
                    {
                        ShowBbsPopup();
                    }
                }

                if (message.Message == WindowMessageMouseWheel)
                {
                    var wheelDelta = unchecked((short)((message.WParam >> 16) & 0xFFFF));
                    if (TryHandleBbsMouseWheel(wheelDelta, out var passToControl))
                    {
                        if (!passToControl)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (_audio.ApplyWheelDelta(wheelDelta))
                        {
                            SyncControls();
                        }

                        continue;
                    }
                }

                if (message.Message == WindowMessageSysKeyDown && message.WParam == VirtualKeyReturn)
                {
                    ToggleFullscreen(window);
                    continue;
                }

                if (message.Message == WindowMessageKeyDown)
                {
                    if (HasBbs && message.Window == _bbsInputEdit)
                    {
                        // 1 行入力欄のため素の Enter で送信、Shift+Enter で改行とする。
                        if (message.WParam == VirtualKeyReturn && (GetKeyState(VirtualKeyShift) & 0x8000) == 0)
                        {
                            SubmitBbsPost();
                            continue;
                        }
                    }
                    else if (TryHandleKeyDown(window, message.WParam))
                    {
                        continue;
                    }
                }

                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }
        finally
        {
            if (_options.BbsSession is { } bbsSession)
            {
                bbsSession.StateChanged -= OnBbsStateChanged;
            }

            if (_bbsHoverTimerActive && window != 0 && IsWindow(window))
            {
                _ = KillTimer(window, BbsHoverTimerId);
                _bbsHoverTimerActive = false;
            }

            if (_captionOverlayTimerActive && window != 0 && IsWindow(window))
            {
                _ = KillTimer(window, CaptionOverlayTimerId);
                _captionOverlayTimerActive = false;
            }

            if (window != 0 && IsWindow(window))
            {
                _ = KillTimer(window, ClockTimerId);
            }

            _captionOverlay?.Dispose();
            _captionOverlay = null;
            _bbsPopup?.Dispose();
            _bbsPopup = null;

            lock (_sync)
            {
                _window = 0;
            }

            if (window != 0 && IsWindow(window))
            {
                _ = DestroyWindow(window);
            }

            if (videoAtom != 0)
            {
                _ = UnregisterClassW(videoClassName, instance);
            }

            if (mainAtom != 0)
            {
                _ = UnregisterClassW(mainClassName, instance);
            }

            if (mainClass.Icon != 0)
            {
                _ = DestroyIcon(mainClass.Icon);
            }

            if (mainClass.SmallIcon != 0)
            {
                _ = DestroyIcon(mainClass.SmallIcon);
            }

            DestroyThemeBrushes();
            _closed.TrySetResult();
        }
    }

    private void CreateThemeBrushes()
    {
        _panelBrush = CreateSolidBrush(ColorPanel);
        _inputBrush = CreateSolidBrush(ColorInput);
        _borderBrush = CreateSolidBrush(ColorBorder);
        _separatorBrush = CreateSolidBrush(ColorSeparator);
        _selectedBrush = CreateSolidBrush(ColorSelected);
        _hoverBrush = CreateSolidBrush(ColorHover);
        _accentBrush = CreateSolidBrush(ColorAccent);
    }

    private void DestroyThemeBrushes()
    {
        DeleteBrush(ref _panelBrush);
        DeleteBrush(ref _inputBrush);
        DeleteBrush(ref _borderBrush);
        DeleteBrush(ref _separatorBrush);
        DeleteBrush(ref _selectedBrush);
        DeleteBrush(ref _hoverBrush);
        DeleteBrush(ref _accentBrush);
    }

    private static void DeleteBrush(ref nint brush)
    {
        if (brush != 0)
        {
            _ = DeleteObject(brush);
            brush = 0;
        }
    }

    private static void ApplyDarkTitleBar(nint window)
    {
        if (window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000) || IsHighContrastEnabled() != false)
        {
            return;
        }

        try
        {
            var darkMode = 1;
            var captionColor = ColorPanel;
            var textColor = ColorText;
            _ = DwmSetWindowAttribute(window, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
            _ = DwmSetWindowAttribute(window, DwmCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(window, DwmTextColor, ref textColor, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static bool? IsHighContrastEnabled()
    {
        var state = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        return SystemParametersInfoW(SpiGetHighContrast, state.Size, ref state, 0)
            ? (state.Flags & HcfHighContrastOn) != 0
            : null;
    }

    private bool TryHandleKeyDown(nint window, nuint virtualKey)
    {
        if (virtualKey == VirtualKeyEscape && _fullscreen)
        {
            ToggleFullscreen(window);
            return true;
        }

        if (virtualKey == VirtualKeyF)
        {
            ToggleFullscreen(window);
            return true;
        }

        if (virtualKey == VirtualKeyB)
        {
            ToggleBorderless(window);
            return true;
        }

        if (virtualKey == VirtualKeyT)
        {
            ToggleAlwaysOnTop(window);
            return true;
        }

        if (virtualKey == VirtualKeyS)
        {
            ScreenshotRequested?.Invoke();
            return true;
        }

        if (virtualKey == VirtualKeySpace)
        {
            TogglePause();
            return true;
        }

        if (virtualKey == VirtualKeyR)
        {
            RequestReload();
            return true;
        }

        if (virtualKey is VirtualKeyLeft or VirtualKeyRight)
        {
            var step = (GetKeyState(VirtualKeyShift) & 0x8000) != 0 ? 30.0 : 5.0;
            SeekRequested?.Invoke(virtualKey == VirtualKeyLeft ? -step : step);
            return true;
        }

        if (virtualKey == VirtualKeyOemOpenBrackets)
        {
            ApplyPlaybackSpeed(LibMpvPlaybackClient.StepSpeed(_playbackSpeed, -1));
            return true;
        }

        if (virtualKey == VirtualKeyOemCloseBrackets)
        {
            ApplyPlaybackSpeed(LibMpvPlaybackClient.StepSpeed(_playbackSpeed, +1));
            return true;
        }

        if (virtualKey == VirtualKeyBack)
        {
            ApplyPlaybackSpeed(1.0, forceNotify: true);
            return true;
        }

        var changed = virtualKey switch
        {
            VirtualKeyUp => _audio.AdjustVolume(LibMpvPlaybackAudioState.VolumeStep),
            VirtualKeyDown => _audio.AdjustVolume(-LibMpvPlaybackAudioState.VolumeStep),
            VirtualKeyM => _audio.ToggleMute(),
            _ => false,
        };

        if (changed)
        {
            SyncControls();
        }

        return virtualKey is VirtualKeyUp or VirtualKeyDown or VirtualKeyM;
    }

    private nint ProcessWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessageNcHitTest:
                if (_borderless && !_fullscreen)
                {
                    var hit = HitTestBorderlessEdge(window, lParam);
                    if (hit != 0)
                    {
                        return hit;
                    }
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageMoving:
                if (!_fullscreen && lParam != 0)
                {
                    SnapMovingRect(window, lParam);
                }

                return 1;

            case WindowMessageLeftButtonDown:
                if (!_fullscreen && IsClientPointInVideoArea(window, lParam))
                {
                    _ = ReleaseCapture();
                    _ = SendMessageW(window, WindowMessageNcLeftButtonDown, HitCaption, 0);
                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageLeftButtonDblClk:
                if (IsClientPointInVideoArea(window, lParam))
                {
                    ToggleFullscreen(window);
                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageRightButtonUp:
                if (IsClientPointInVideoArea(window, lParam))
                {
                    ShowVideoContextMenu(window);
                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageMove:
                HideBbsPopup();
                SyncCaptionOverlayPosition(window);
                return 0;

            case WindowMessageSize:
                HideBbsPopup();
                var width = unchecked((int)(lParam.ToInt64() & 0xFFFF));
                var height = unchecked((int)((lParam.ToInt64() >> 16) & 0xFFFF));
                LayoutControls(width, height);
                SyncCaptionOverlayPosition(window);
                return 0;

            case WindowMessageExitSizeMove:
                _borderlessMaximized = false;
                _userResized = true;
                CaptureNormalWindowBounds(window);
                return 0;

            case WindowMessageActivate:
                if ((wParam & 0xFFFF) == 0)
                {
                    HideBbsPopup();
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessagePaint:
                PaintControlBar(window);
                return 0;

            case WindowMessageCtlColorEdit:
                if (wParam != 0 && lParam == _bbsInputEdit && _bbsInputEdit != 0)
                {
                    var hdc = (nint)wParam;
                    _ = SetBkColor(hdc, ColorInput);
                    _ = SetTextColor(hdc, ColorText);
                    if (_inputBrush != 0)
                    {
                        return _inputBrush;
                    }
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageDrawItem:
                if (lParam != 0)
                {
                    var controlId = unchecked((int)wParam);
                    if (controlId == VolumeButtonId)
                    {
                        DrawVolumeButton(lParam);
                        return 1;
                    }

                    if (controlId == BbsThreadBarId)
                    {
                        DrawBbsThreadBar(lParam);
                        return 1;
                    }

                    if (controlId == BbsSendButtonId)
                    {
                        DrawBbsSendButton(lParam);
                        return 1;
                    }
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageCommand:
                var notification = (int)((wParam >> 16) & 0xFFFF);
                if (notification == ButtonClicked && lParam == _volumeButton && _volumeButton != 0)
                {
                    if (_audio.ToggleMute())
                    {
                        SyncControls();
                    }

                    return 0;
                }

                if (notification == ButtonClicked && lParam == _bbsThreadBar && _bbsThreadBar != 0)
                {
                    ShowBbsThreadMenu(window);
                    return 0;
                }

                if (notification == ButtonClicked && lParam == _bbsSendButton && _bbsSendButton != 0)
                {
                    HandleBbsActionButtonClick();
                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageTimer:
                if (wParam == BbsHoverTimerId)
                {
                    CheckBbsPopupHover();
                    return 0;
                }

                if (wParam == CaptionOverlayTimerId)
                {
                    CheckCaptionOverlayHover(window);
                    return 0;
                }

                if (wParam == ClockTimerId)
                {
                    _ = InvalidateRect(window, 0, false);
                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageBbsStateChanged:
                SyncBbsControls();
                return 0;

            case WindowMessageBbsPostCompleted:
                CompleteBbsPostOnUiThread(wParam != 0);
                return 0;

            case WindowMessageApplyAutoScale:
                TryApplyInitialAutoScale(window);
                return 0;

            case WindowMessageClose:
                CaptureNormalWindowBounds(window);
                _ = DestroyWindow(window);
                return 0;

            case WindowMessageDestroy:
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private bool HasBbs => _options.BbsSession is not null;

    private int BottomBarsHeight => _fullscreen
        ? 0
        : HasBbs
            ? ThreadBarHeight + BbsInputBarHeight + StatusBarHeight
            : StatusBarHeight;

    private void PaintControlBar(nint window)
    {
        var hdc = BeginPaint(window, out var paint);
        try
        {
            if (hdc != 0 && GetClientRect(window, out var client))
            {
                var barTop = Math.Max(0, client.Bottom - BottomBarsHeight);
                var barRect = new WindowRectangle
                {
                    Left = 0,
                    Top = barTop,
                    Right = client.Right,
                    Bottom = client.Bottom,
                };
                if (_panelBrush != 0)
                {
                    _ = FillRect(hdc, ref barRect, _panelBrush);
                }

                var statusTop = Math.Max(0, client.Bottom - StatusBarHeight);
                if (_separatorBrush != 0 && statusTop < client.Bottom)
                {
                    var separatorRect = new WindowRectangle
                    {
                        Left = 0,
                        Top = statusTop,
                        Right = client.Right,
                        Bottom = statusTop + 1,
                    };
                    _ = FillRect(hdc, ref separatorRect, _separatorBrush);
                }

                var font = GetStockObject(StockDefaultGuiFont);
                var previousFont = font != 0 ? SelectObject(hdc, font) : 0;
                try
                {
                    _ = SetBkMode(hdc, BkModeTransparent);
                    var rightAreaWidth = VolumeButtonWidth + ClockWidth + 8;
                    var baseStatus = string.IsNullOrWhiteSpace(_options.StatusLabel)
                        ? _options.Title
                        : _options.StatusLabel;
                    var stats = Volatile.Read(ref _statusStatsText);
                    var statusText = string.IsNullOrWhiteSpace(stats)
                        ? baseStatus
                        : $"{baseStatus}  {stats}";
                    if (client.Right - rightAreaWidth > 16)
                    {
                        var labelRect = new WindowRectangle
                        {
                            Left = 6,
                            Top = statusTop + 1,
                            Right = client.Right - rightAreaWidth,
                            Bottom = client.Bottom,
                        };
                        _ = SetTextColor(hdc, ColorSecondaryText);
                        _ = DrawTextW(hdc, statusText, statusText.Length, ref labelRect, DrawTextSingleLineLeftEllipsis);
                    }

                    var clockText = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    var clockRect = new WindowRectangle
                    {
                        Left = Math.Max(0, client.Right - VolumeButtonWidth - ClockWidth - 4),
                        Top = statusTop + 1,
                        Right = Math.Max(0, client.Right - VolumeButtonWidth - 4),
                        Bottom = client.Bottom,
                    };
                    _ = SetTextColor(hdc, ColorSecondaryText);
                    _ = DrawTextW(hdc, clockText, clockText.Length, ref clockRect, DrawTextSingleLineRight);
                }
                finally
                {
                    if (previousFont != 0)
                    {
                        _ = SelectObject(hdc, previousFont);
                    }
                }
            }
        }
        finally
        {
            _ = EndPaint(window, ref paint);
        }
    }

    private void DrawVolumeButton(nint drawItemPointer)
    {
        var item = Marshal.PtrToStructure<DrawItemStruct>(drawItemPointer);
        var state = _audio.Current;
        var pressed = (item.ItemState & OdsSelected) != 0;
        var fillBrush = pressed ? _hoverBrush : _panelBrush;
        var rect = item.ItemRect;
        if (fillBrush != 0)
        {
            _ = FillRect(item.DeviceContext, ref rect, fillBrush);
        }

        var font = GetStockObject(StockDefaultGuiFont);
        var previousFont = font != 0 ? SelectObject(item.DeviceContext, font) : 0;
        try
        {
            _ = SetBkMode(item.DeviceContext, BkModeTransparent);
            _ = SetTextColor(item.DeviceContext, state.Muted ? ColorDisabledText : ColorText);
            var text = state.Muted ? "MUTE" : $"{state.Volume}%";
            var textRect = new WindowRectangle
            {
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right - 6,
                Bottom = rect.Bottom,
            };
            _ = DrawTextW(item.DeviceContext, text, text.Length, ref textRect, DrawTextSingleLineRight);
        }
        finally
        {
            if (previousFont != 0)
            {
                _ = SelectObject(item.DeviceContext, previousFont);
            }
        }
    }

    private void LayoutControls(int clientWidth, int clientHeight)
    {
        if (_videoWindow == 0)
        {
            return;
        }

        var safeWidth = Math.Max(0, clientWidth);
        var safeHeight = Math.Max(0, clientHeight);
        var videoHeight = Math.Max(0, safeHeight - BottomBarsHeight);
        _ = MoveWindow(_videoWindow, 0, 0, safeWidth, videoHeight, true);

        var nextY = videoHeight;
        if (HasBbs)
        {
            if (_bbsThreadBar != 0)
            {
                _ = MoveWindow(_bbsThreadBar, 0, nextY, safeWidth, ThreadBarHeight, true);
            }

            nextY += ThreadBarHeight;
            var editTop = nextY + 1;
            var editHeight = BbsInputBarHeight - 2;
            var sendWidth = BbsSendButtonWidth;
            var editWidth = Math.Max(60, safeWidth - sendWidth - 2);
            if (_bbsInputEdit != 0)
            {
                _ = MoveWindow(_bbsInputEdit, 1, editTop, editWidth, editHeight, true);
            }

            if (_bbsSendButton != 0)
            {
                _ = MoveWindow(_bbsSendButton, editWidth + 1, editTop, sendWidth, editHeight, true);
            }

            nextY += BbsInputBarHeight;
        }

        if (_volumeButton != 0)
        {
            var volumeX = Math.Max(0, safeWidth - VolumeButtonWidth);
            _ = MoveWindow(_volumeButton, volumeX, nextY + 1, VolumeButtonWidth, StatusBarHeight - 1, true);
        }

        if (_window != 0)
        {
            _ = InvalidateRect(_window, 0, true);
        }
    }

    private void OnBbsStateChanged(BbsSessionSnapshot snapshot)
    {
        nint window;
        lock (_sync)
        {
            window = _window;
        }

        if (window != 0 && IsWindow(window))
        {
            _ = PostMessageW(window, WindowMessageBbsStateChanged, 0, 0);
        }
    }

    private void SyncBbsControls()
    {
        if (!HasBbs)
        {
            return;
        }

        var snapshot = _options.BbsSession!.Snapshot;
        _bbsPopup?.UpdatePosts(snapshot.Posts, snapshot.StatusMessage);
        if (_bbsThreadBar != 0)
        {
            _ = SetWindowTextW(_bbsThreadBar, FormatThreadBarDisplayText(snapshot));
            _ = InvalidateRect(_bbsThreadBar, 0, true);
        }
    }

    private string FormatThreadBarDisplayText(BbsSessionSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(_bbsPostError)
            ? $"[ {_bbsPostError} ]  {snapshot.FormatThreadBarText()}"
            : snapshot.FormatThreadBarText();

    private void DrawBbsThreadBar(nint drawItemPointer)
    {
        var item = Marshal.PtrToStructure<DrawItemStruct>(drawItemPointer);
        var pressed = (item.ItemState & OdsSelected) != 0;
        var hovered = _bbsPopup is { IsVisible: true };
        var fillBrush = pressed || hovered ? _hoverBrush : _panelBrush;
        var rect = item.ItemRect;
        if (fillBrush != 0)
        {
            _ = FillRect(item.DeviceContext, ref rect, fillBrush);
        }

        if (_separatorBrush != 0 && rect.Bottom > rect.Top)
        {
            var topLine = new WindowRectangle { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Top + 1 };
            var bottomLine = new WindowRectangle { Left = rect.Left, Top = rect.Bottom - 1, Right = rect.Right, Bottom = rect.Bottom };
            _ = FillRect(item.DeviceContext, ref topLine, _separatorBrush);
            _ = FillRect(item.DeviceContext, ref bottomLine, _separatorBrush);
        }

        var font = GetStockObject(StockDefaultGuiFont);
        var previousFont = font != 0 ? SelectObject(item.DeviceContext, font) : 0;
        try
        {
            _ = SetBkMode(item.DeviceContext, BkModeTransparent);
            _ = SetTextColor(item.DeviceContext, hovered ? ColorAccent : ColorText);
            var text = HasBbs ? FormatThreadBarDisplayText(_options.BbsSession!.Snapshot) : string.Empty;
            var textRect = new WindowRectangle
            {
                Left = rect.Left + 8,
                Top = rect.Top,
                Right = rect.Right - 8,
                Bottom = rect.Bottom,
            };
            _ = DrawTextW(item.DeviceContext, text, text.Length, ref textRect, DrawTextSingleLineCenterEllipsis);
        }
        finally
        {
            if (previousFont != 0)
            {
                _ = SelectObject(item.DeviceContext, previousFont);
            }
        }
    }

    private void DrawBbsSendButton(nint drawItemPointer)
    {
        var item = Marshal.PtrToStructure<DrawItemStruct>(drawItemPointer);
        var pressed = (item.ItemState & OdsSelected) != 0;
        var focused = (item.ItemState & OdsFocus) != 0;
        var fillBrush = pressed ? _hoverBrush : _bbsPosting ? _selectedBrush : _inputBrush;
        var borderBrush = pressed || focused || _bbsPosting ? _accentBrush : _borderBrush;

        var rect = item.ItemRect;
        if (fillBrush != 0)
        {
            _ = FillRect(item.DeviceContext, ref rect, fillBrush);
        }

        if (borderBrush != 0)
        {
            _ = FrameRect(item.DeviceContext, ref rect, borderBrush);
        }

        var font = GetStockObject(StockDefaultGuiFont);
        var previousFont = font != 0 ? SelectObject(item.DeviceContext, font) : 0;
        try
        {
            _ = SetBkMode(item.DeviceContext, BkModeTransparent);
            _ = SetTextColor(item.DeviceContext, _bbsPosting ? ColorSecondaryText : ColorText);
            var text = _bbsPosting ? "…" : "▶";
            _ = DrawTextW(item.DeviceContext, text, text.Length, ref rect, DrawTextSingleLineCenter);
        }
        finally
        {
            if (previousFont != 0)
            {
                _ = SelectObject(item.DeviceContext, previousFont);
            }
        }
    }

    private void ShowBbsPopup()
    {
        if (!HasBbs || _bbsPopup is null || _bbsThreadBar == 0 || _videoWindow == 0)
        {
            return;
        }

        if (!GetWindowRect(_bbsThreadBar, out var barRect) || !GetWindowRect(_videoWindow, out var videoRect))
        {
            return;
        }

        var videoHeight = Math.Max(180, videoRect.Bottom - videoRect.Top);
        _bbsPopup.ShowAbove(barRect.Left, barRect.Top, Math.Max(320, barRect.Right - barRect.Left), videoHeight);
        if (!_bbsHoverTimerActive && _window != 0)
        {
            _ = SetTimer(_window, BbsHoverTimerId, 120, 0);
            _bbsHoverTimerActive = true;
        }

        _ = InvalidateRect(_bbsThreadBar, 0, true);
    }

    private void HideBbsPopup()
    {
        if (_bbsHoverTimerActive && _window != 0 && IsWindow(_window))
        {
            _ = KillTimer(_window, BbsHoverTimerId);
            _bbsHoverTimerActive = false;
        }

        if (_bbsPopup is { IsVisible: true })
        {
            _bbsPopup.Hide();
            if (_bbsThreadBar != 0 && IsWindow(_bbsThreadBar))
            {
                _ = InvalidateRect(_bbsThreadBar, 0, true);
            }
        }
    }

    private void CheckBbsPopupHover()
    {
        if (!GetCursorPos(out var point))
        {
            HideBbsPopup();
            return;
        }

        var overThreadBar = _bbsThreadBar != 0 && IsScreenPointInWindow(_bbsThreadBar, point.X, point.Y);
        var overPopup = _bbsPopup is not null && _bbsPopup.ContainsScreenPoint(point.X, point.Y);
        if (!overThreadBar && !overPopup)
        {
            HideBbsPopup();
        }
    }

    private bool TryHandleBbsMouseWheel(short wheelDelta, out bool passToControl)
    {
        passToControl = false;
        if (!HasBbs || !GetCursorPos(out var point))
        {
            return false;
        }

        if (_bbsPopup is { IsVisible: true } && _bbsPopup.ContainsScreenPoint(point.X, point.Y))
        {
            _bbsPopup.ScrollByNotches(wheelDelta / 120);
            return true;
        }

        if (_bbsInputEdit != 0 && IsScreenPointInWindow(_bbsInputEdit, point.X, point.Y))
        {
            passToControl = true;
            return true;
        }

        return false;
    }

    private void HandleBbsActionButtonClick()
    {
        var text = ReadBbsInputText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            SubmitBbsPost();
            return;
        }

        if (_options.BbsSession is { } bbs)
        {
            _ = Task.Run(() => bbs.RefreshAsync());
        }
    }

    private void SubmitBbsPost()
    {
        if (!HasBbs || _bbsPosting)
        {
            return;
        }

        var text = ReadBbsInputText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _bbsPosting = true;
        _bbsPostError = null;
        if (_bbsSendButton != 0)
        {
            _ = EnableWindow(_bbsSendButton, false);
            _ = InvalidateRect(_bbsSendButton, 0, true);
        }

        if (_bbsThreadBar != 0)
        {
            _ = InvalidateRect(_bbsThreadBar, 0, true);
        }

        var bbs = _options.BbsSession!;
        var mail = _bbsUseSage ? "sage" : string.Empty;
        _ = Task.Run(async () =>
        {
            var result = await bbs.PostAsync(string.Empty, mail, text).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _bbsPostError = result.ErrorMessage ?? "書き込みに失敗しました";
            }

            nint window;
            lock (_sync)
            {
                window = _window;
            }

            if (window != 0 && IsWindow(window))
            {
                _ = PostMessageW(window, WindowMessageBbsPostCompleted, result.Succeeded ? (nuint)1 : 0, 0);
            }
        });
    }

    private void CompleteBbsPostOnUiThread(bool succeeded)
    {
        _bbsPosting = false;
        if (succeeded)
        {
            _bbsPostError = null;
            if (_bbsInputEdit != 0)
            {
                _ = SetWindowTextW(_bbsInputEdit, string.Empty);
            }
        }

        if (_bbsSendButton != 0)
        {
            _ = EnableWindow(_bbsSendButton, true);
            _ = InvalidateRect(_bbsSendButton, 0, true);
        }

        if (_bbsThreadBar != 0)
        {
            _ = InvalidateRect(_bbsThreadBar, 0, true);
        }
    }

    private void ShowBbsThreadMenu(nint window)
    {
        if (!HasBbs || _bbsThreadBar == 0)
        {
            return;
        }

        HideBbsPopup();
        var bbs = _options.BbsSession!;
        var snapshot = bbs.Snapshot;
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            var threads = snapshot.Threads.Take(20).ToArray();
            for (var i = 0; i < threads.Length; i++)
            {
                var thread = threads[i];
                var flags = MenuFlagString;
                if (string.Equals(thread.Key, snapshot.ThreadKey, StringComparison.Ordinal))
                {
                    flags |= MenuFlagChecked;
                }

                var label = $"{thread.Title} ({thread.PostCount})".Replace("&", "&&", StringComparison.Ordinal);
                _ = AppendMenuW(menu, flags, MenuCmdThreadBase + (uint)i, label);
            }

            if (threads.Length > 0)
            {
                _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);
            }

            _ = AppendMenuW(menu, MenuFlagString, MenuCmdRefresh, "掲示板を更新");
            _ = AppendMenuW(menu, MenuFlagString | (_bbsUseSage ? MenuFlagChecked : 0), MenuCmdToggleSage, "sage で書き込む");
            _ = AppendMenuW(menu, MenuFlagString, MenuCmdOpenBrowser, "ブラウザで開く");

            var anchor = GetWindowRect(_bbsThreadBar, out var barRect)
                ? new WindowPoint { X = barRect.Left + 8, Y = barRect.Top }
                : new WindowPoint { X = 0, Y = 0 };
            var selected = TrackPopupMenuEx(menu, TrackPopupReturnCmd | TrackPopupBottomAlign, anchor.X, anchor.Y, window, 0);
            if (selected == MenuCmdRefresh)
            {
                _ = Task.Run(() => bbs.RefreshAsync());
            }
            else if (selected == MenuCmdToggleSage)
            {
                _bbsUseSage = !_bbsUseSage;
                UpdateSavedWindowState(s => s with { BbsUseSage = _bbsUseSage });
            }
            else if (selected == MenuCmdOpenBrowser)
            {
                var url = snapshot.Target.ThreadUrl ?? snapshot.Target.BoardUrl;
                try
                {
                    _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch
                {
                }
            }
            else if (selected >= MenuCmdThreadBase)
            {
                var threadIndex = (int)(selected - MenuCmdThreadBase);
                if (threadIndex >= 0 && threadIndex < threads.Length)
                {
                    var key = threads[threadIndex].Key;
                    _ = Task.Run(() => bbs.SelectThreadAsync(key));
                }
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private string ReadBbsInputText()
    {
        if (_bbsInputEdit == 0)
        {
            return string.Empty;
        }

        var length = GetWindowTextLengthW(_bbsInputEdit);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(_bbsInputEdit, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    private static bool IsScreenPointInWindow(nint hwnd, int screenX, int screenY) =>
        GetWindowRect(hwnd, out var rect) &&
        screenX >= rect.Left && screenX <= rect.Right &&
        screenY >= rect.Top && screenY <= rect.Bottom;

    private void TogglePause()
    {
        _paused = !_paused;
        PauseChanged?.Invoke(_paused);
    }

    private void ApplyPlaybackSpeed(double speed, bool forceNotify = false)
    {
        var clamped = LibMpvPlaybackClient.ClampSpeed(speed);
        if (!forceNotify && Math.Abs(_playbackSpeed - clamped) < 0.0001)
        {
            return;
        }

        _playbackSpeed = clamped;
        SpeedChanged?.Invoke(clamped);
    }

    private void RequestReload()
    {
        _paused = false;
        _playbackSpeed = 1.0;
        ReloadRequested?.Invoke();
    }

    private void ToggleAlwaysOnTop(nint window)
    {
        if (window == 0)
        {
            return;
        }

        _alwaysOnTop = !_alwaysOnTop;
        ApplyAlwaysOnTop(window, _alwaysOnTop);
        UpdateSavedWindowState(s => s with { AlwaysOnTop = _alwaysOnTop });
    }

    private static void ApplyAlwaysOnTop(nint window, bool alwaysOnTop) =>
        _ = SetWindowPos(
            window,
            alwaysOnTop ? HwndTopMost : HwndNoTopMost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);

    private void ToggleBorderless(nint window)
    {
        if (window == 0)
        {
            return;
        }

        if (_fullscreen)
        {
            ToggleFullscreen(window);
        }

        HideBbsPopup();
        HideCaptionOverlay();
        if (!GetClientRect(window, out var client) || !GetWindowRect(window, out var outer))
        {
            return;
        }

        _borderless = !_borderless;
        _borderlessMaximized = false;
        var style = _borderless ? WindowStyleBorderlessClipChildren : WindowStyleOverlappedClipChildren;
        SetVisibleWindowStyle(window, style);
        var adjusted = new WindowRectangle
        {
            Left = 0,
            Top = 0,
            Right = Math.Max(LibMpvPlaybackWindowState.MinVideoWidth, client.Right - client.Left),
            Bottom = Math.Max(LibMpvPlaybackWindowState.MinVideoHeight + BottomBarsHeight, client.Bottom - client.Top),
        };
        _ = AdjustWindowRectEx(ref adjusted, style, false, 0);
        _ = SetWindowPos(
            window,
            _alwaysOnTop ? HwndTopMost : HwndNoTopMost,
            outer.Left,
            outer.Top,
            adjusted.Right - adjusted.Left,
            adjusted.Bottom - adjusted.Top,
            SwpFrameChanged | SwpShowWindow);
        if (!_borderless)
        {
            ApplyDarkTitleBar(window);
        }

        if (GetClientRect(window, out var newClient))
        {
            LayoutControls(newClient.Right - newClient.Left, newClient.Bottom - newClient.Top);
        }

        _ = UpdateWindow(window);
        UpdateSavedWindowState(s => s with { Borderless = _borderless });
    }

    private void ToggleFullscreen(nint window)
    {
        if (window == 0)
        {
            return;
        }

        HideBbsPopup();
        HideCaptionOverlay();
        if (!_fullscreen)
        {
            if (GetWindowRect(window, out var currentRect))
            {
                _preFullscreenRect = currentRect;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (monitor == 0 || !GetMonitorInfoW(monitor, ref monitorInfo))
            {
                return;
            }

            _fullscreen = true;
            SetBottomControlsVisible(visible: false);
            SetVisibleWindowStyle(window, WindowStyleBorderlessClipChildren);
            var bounds = monitorInfo.Monitor;
            _ = SetWindowPos(
                window,
                HwndTopMost,
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top,
                SwpFrameChanged | SwpShowWindow);
            LayoutControls(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            _ = UpdateWindow(window);
        }
        else
        {
            _fullscreen = false;
            var style = _borderless ? WindowStyleBorderlessClipChildren : WindowStyleOverlappedClipChildren;
            SetVisibleWindowStyle(window, style);
            SetBottomControlsVisible(visible: true);
            _ = SetWindowPos(
                window,
                _alwaysOnTop ? HwndTopMost : HwndNoTopMost,
                _preFullscreenRect.Left,
                _preFullscreenRect.Top,
                Math.Max(LibMpvPlaybackWindowState.MinVideoWidth, _preFullscreenRect.Right - _preFullscreenRect.Left),
                Math.Max(LibMpvPlaybackWindowState.MinVideoHeight + BottomBarsHeight, _preFullscreenRect.Bottom - _preFullscreenRect.Top),
                SwpFrameChanged | SwpShowWindow);
            if (!_borderless)
            {
                ApplyDarkTitleBar(window);
            }

            if (GetClientRect(window, out var client))
            {
                LayoutControls(client.Right - client.Left, client.Bottom - client.Top);
            }

            _ = UpdateWindow(window);
        }
    }

    private void ShowCaptionOverlay(nint window)
    {
        if (!_borderless || _fullscreen || _captionOverlay is null || window == 0 || IsIconic(window))
        {
            return;
        }

        if (!GetWindowRect(window, out var outer))
        {
            return;
        }

        _captionOverlay.ShowAtTopRight(outer.Right, outer.Top, _borderlessMaximized);
        if (!_captionOverlayTimerActive)
        {
            _ = SetTimer(window, CaptionOverlayTimerId, 120, 0);
            _captionOverlayTimerActive = true;
        }
    }

    private void SyncCaptionOverlayPosition(nint window)
    {
        if (_captionOverlay is not { IsVisible: true } || window == 0)
        {
            return;
        }

        if (!_borderless || _fullscreen || IsIconic(window) || !GetWindowRect(window, out var outer))
        {
            HideCaptionOverlay();
            return;
        }

        _captionOverlay.ShowAtTopRight(outer.Right, outer.Top, _borderlessMaximized);
    }

    private void HideCaptionOverlay()
    {
        if (_captionOverlayTimerActive && _window != 0 && IsWindow(_window))
        {
            _ = KillTimer(_window, CaptionOverlayTimerId);
            _captionOverlayTimerActive = false;
        }

        _captionOverlay?.Hide();
    }

    private void CheckCaptionOverlayHover(nint window)
    {
        if (!_borderless || _fullscreen || window == 0 || IsIconic(window) || !GetCursorPos(out var point))
        {
            HideCaptionOverlay();
            return;
        }

        var overOwner = IsScreenPointInWindow(window, point.X, point.Y);
        var overOverlay = _captionOverlay is not null && _captionOverlay.ContainsScreenPoint(point.X, point.Y);
        if (!overOwner && !overOverlay)
        {
            HideCaptionOverlay();
        }
    }

    private void HandleCaptionOverlayButton(nint window, LibMpvCaptionOverlayButton button)
    {
        if (window == 0 || !IsWindow(window))
        {
            return;
        }

        switch (button)
        {
            case LibMpvCaptionOverlayButton.Minimize:
                HideCaptionOverlay();
                HideBbsPopup();
                _ = ShowWindow(window, ShowWindowMinimize);
                break;

            case LibMpvCaptionOverlayButton.Maximize:
                ToggleBorderlessMaximize(window);
                break;

            case LibMpvCaptionOverlayButton.Close:
                HideCaptionOverlay();
                _ = PostMessageW(window, WindowMessageClose, 0, 0);
                break;
        }
    }

    private void ToggleBorderlessMaximize(nint window)
    {
        if (!_borderless || _fullscreen || window == 0)
        {
            return;
        }

        HideBbsPopup();
        if (!_borderlessMaximized)
        {
            if (GetWindowRect(window, out var current))
            {
                _preMaximizeRect = current;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
            {
                return;
            }

            _borderlessMaximized = true;
            var work = info.Work;
            _ = SetWindowPos(
                window,
                _alwaysOnTop ? HwndTopMost : HwndNoTopMost,
                work.Left,
                work.Top,
                work.Right - work.Left,
                work.Bottom - work.Top,
                SwpFrameChanged | SwpShowWindow);
            if (GetClientRect(window, out var client))
            {
                LayoutControls(client.Right - client.Left, client.Bottom - client.Top);
            }

            _ = UpdateWindow(window);
            ShowCaptionOverlay(window);
        }
        else
        {
            _borderlessMaximized = false;
            var width = Math.Max(LibMpvPlaybackWindowState.MinVideoWidth, _preMaximizeRect.Right - _preMaximizeRect.Left);
            var height = Math.Max(LibMpvPlaybackWindowState.MinVideoHeight + BottomBarsHeight, _preMaximizeRect.Bottom - _preMaximizeRect.Top);
            _ = SetWindowPos(
                window,
                _alwaysOnTop ? HwndTopMost : HwndNoTopMost,
                _preMaximizeRect.Left,
                _preMaximizeRect.Top,
                width,
                height,
                SwpFrameChanged | SwpShowWindow);
            if (GetClientRect(window, out var client))
            {
                LayoutControls(client.Right - client.Left, client.Bottom - client.Top);
            }

            _ = UpdateWindow(window);
            ShowCaptionOverlay(window);
        }
    }

    private static void SetVisibleWindowStyle(nint window, uint style) =>
        _ = SetWindowLongPtrW(window, GwlStyle, unchecked((nint)(nuint)(style | WindowStyleVisible)));

    private void SetBottomControlsVisible(bool visible)
    {
        var cmd = visible ? ShowWindowNormal : ShowWindowHide;
        if (_volumeButton != 0)
        {
            _ = ShowWindow(_volumeButton, cmd);
        }

        if (_bbsThreadBar != 0)
        {
            _ = ShowWindow(_bbsThreadBar, cmd);
        }

        if (_bbsInputEdit != 0)
        {
            _ = ShowWindow(_bbsInputEdit, cmd);
        }

        if (_bbsSendButton != 0)
        {
            _ = ShowWindow(_bbsSendButton, cmd);
        }
    }

    private void ResizeVideoArea(nint window, int videoWidth, int videoHeight)
    {
        if (window == 0)
        {
            return;
        }

        if (_fullscreen)
        {
            ToggleFullscreen(window);
        }

        _borderlessMaximized = false;
        var style = _borderless ? WindowStyleBorderlessClipChildren : WindowStyleOverlappedClipChildren;
        var rect = new WindowRectangle
        {
            Left = 0,
            Top = 0,
            Right = videoWidth,
            Bottom = videoHeight + BottomBarsHeight,
        };
        _ = AdjustWindowRectEx(ref rect, style, false, 0);
        _ = SetWindowPos(
            window,
            0,
            0,
            0,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            SwpNoMove | SwpNoZOrder | SwpNoActivate);
        CaptureNormalWindowBounds(window);
    }

    private void ShowVideoContextMenu(nint window)
    {
        HideBbsPopup();
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = AppendMenuW(
                menu,
                MenuFlagString | (_paused ? MenuFlagChecked : 0),
                MenuCmdTogglePause,
                _paused ? "再生を再開 (Space)" : "一時停止 (Space)");

            var speedMenu = CreatePopupMenu();
            if (speedMenu != 0)
            {
                var speedPresets = LibMpvPlaybackClient.SpeedPresets;
                for (var i = 0; i < speedPresets.Length; i++)
                {
                    var preset = speedPresets[i];
                    var label = preset switch
                    {
                        1.0 => "1.0x (標準)",
                        1.25 => "1.25x (追いつき)",
                        _ => LibMpvPlaybackClient.FormatSpeedLabel(preset),
                    };
                    var flags = MenuFlagString | (Math.Abs(_playbackSpeed - preset) < 0.001 ? MenuFlagChecked : 0);
                    _ = AppendMenuW(speedMenu, flags, MenuCmdSpeedBase + (uint)i, label);
                }

                _ = AppendMenuW(menu, MenuFlagPopup, (nuint)speedMenu, "再生速度");
            }

            _ = AppendMenuW(menu, MenuFlagString, MenuCmdReload, "再読込 (R)");

            var seekMenu = CreatePopupMenu();
            if (seekMenu != 0)
            {
                _ = AppendMenuW(seekMenu, MenuFlagString, MenuCmdSeekBack30, "30秒戻す (Shift+←)");
                _ = AppendMenuW(seekMenu, MenuFlagString, MenuCmdSeekBack5, "5秒戻す (←)");
                _ = AppendMenuW(seekMenu, MenuFlagString, MenuCmdSeekForward5, "5秒進める (→)");
                _ = AppendMenuW(seekMenu, MenuFlagString, MenuCmdSeekForward30, "30秒進める (Shift+→)");
                _ = AppendMenuW(menu, MenuFlagPopup, (nuint)seekMenu, "シーク");
            }

            _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);

            var scaleMenu = CreatePopupMenu();
            if (scaleMenu != 0)
            {
                var nativeW = Volatile.Read(ref _nativeVideoWidth);
                var nativeH = Volatile.Read(ref _nativeVideoHeight);
                var currentVideoW = 0;
                var currentVideoH = 0;
                if (GetClientRect(window, out var clientRect))
                {
                    currentVideoW = clientRect.Right - clientRect.Left;
                    currentVideoH = (clientRect.Bottom - clientRect.Top) - BottomBarsHeight;
                }

                var presets = LibMpvPlaybackWindowState.ScalePresets;
                for (var i = 0; i < presets.Length; i++)
                {
                    var percent = presets[i];
                    var flags = MenuFlagString;
                    if (nativeW > 0 && nativeH > 0)
                    {
                        var (expectedW, expectedH) = LibMpvPlaybackWindowState.ScaleVideoSize(nativeW, nativeH, percent);
                        if (Math.Abs(currentVideoW - expectedW) <= 2 && Math.Abs(currentVideoH - expectedH) <= 2)
                        {
                            flags |= MenuFlagChecked;
                        }
                    }

                    _ = AppendMenuW(scaleMenu, flags, MenuCmdScaleBase + (uint)i, $"{percent}%");
                }

                _ = AppendMenuW(scaleMenu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(
                    scaleMenu,
                    MenuFlagString | (_autoResizeTo100PercentOnStart ? MenuFlagChecked : 0),
                    MenuCmdToggleAutoResize100,
                    "起動時に 100% で開く");
                _ = AppendMenuW(menu, MenuFlagPopup, (nuint)scaleMenu, "倍率");
            }

            var sizeMenu = CreatePopupMenu();
            if (sizeMenu != 0)
            {
                _ = AppendMenuW(sizeMenu, MenuFlagString, MenuCmdResizeSmall, "640 × 360");
                _ = AppendMenuW(sizeMenu, MenuFlagString, MenuCmdResizeMedium, "960 × 540");
                _ = AppendMenuW(sizeMenu, MenuFlagString, MenuCmdResizeHd, "1280 × 720");
                _ = AppendMenuW(sizeMenu, MenuFlagString, MenuCmdResizeLarge, "1600 × 900");
                _ = AppendMenuW(menu, MenuFlagPopup, (nuint)sizeMenu, "サイズ");
            }

            _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);
            _ = AppendMenuW(menu, MenuFlagString | (_alwaysOnTop ? MenuFlagChecked : 0), MenuCmdToggleAlwaysOnTop, "最前面に固定 (T)");
            _ = AppendMenuW(menu, MenuFlagString | (_borderless ? MenuFlagChecked : 0), MenuCmdToggleBorderless, "枠なし表示 (B)");
            _ = AppendMenuW(menu, MenuFlagString | (_fullscreen ? MenuFlagChecked : 0), MenuCmdToggleFullscreen, "フルスクリーン (F)");
            _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);
            _ = AppendMenuW(menu, MenuFlagString, MenuCmdScreenshot, "スナップショットを保存 (S)");
            _ = AppendMenuW(menu, MenuFlagString, MenuCmdOpenScreenshotFolder, "スナップショットフォルダを開く");
            _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);

            var adjustMenu = CreatePopupMenu();
            if (adjustMenu != 0)
            {
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdBrightUp, $"明るさ +10 (現在 {_videoAdjustment.Brightness:+0;-0;0})");
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdBrightDown, "明るさ -10");
                _ = AppendMenuW(adjustMenu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdContrastUp, $"コントラスト +10 (現在 {_videoAdjustment.Contrast:+0;-0;0})");
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdContrastDown, "コントラスト -10");
                _ = AppendMenuW(adjustMenu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdSatUp, $"彩度 +10 (現在 {_videoAdjustment.Saturation:+0;-0;0})");
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdSatDown, "彩度 -10");
                _ = AppendMenuW(adjustMenu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdGammaUp, $"ガンマ +10 (現在 {_videoAdjustment.Gamma:+0;-0;0})");
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdGammaDown, "ガンマ -10");
                _ = AppendMenuW(adjustMenu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(adjustMenu, MenuFlagString, MenuCmdVideoAdjustReset, "映像調整をリセット");
                _ = AppendMenuW(menu, MenuFlagPopup, (nuint)adjustMenu, "映像調整");
            }

            _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);

            LibMpvAudioDevice[] audioDevices = [];
            var volumeMenu = CreatePopupMenu();
            if (volumeMenu != 0)
            {
                var currentAudio = _audio.Current;
                _ = AppendMenuW(volumeMenu, MenuFlagString, MenuCmdVolumeUp, "音量を上げる +5% (↑)");
                _ = AppendMenuW(volumeMenu, MenuFlagString, MenuCmdVolumeDown, "音量を下げる -5% (↓)");
                _ = AppendMenuW(volumeMenu, MenuFlagSeparator, 0, string.Empty);
                for (var i = 0; i < VolumePresets.Length; i++)
                {
                    var preset = VolumePresets[i];
                    var flags = MenuFlagString | (!currentAudio.Muted && currentAudio.Volume == preset ? MenuFlagChecked : 0);
                    _ = AppendMenuW(volumeMenu, flags, MenuCmdVolumePresetBase + (uint)i, $"{preset}%");
                }

                if (AudioDevicesProvider?.Invoke() is { Devices.Count: > 0 } audioDeviceState)
                {
                    audioDevices = audioDeviceState.Devices.Take(MaxAudioDeviceMenuItems).ToArray();
                    var deviceMenu = CreatePopupMenu();
                    if (deviceMenu != 0)
                    {
                        for (var i = 0; i < audioDevices.Length; i++)
                        {
                            var device = audioDevices[i];
                            var label = string.Equals(device.Name, "auto", StringComparison.Ordinal)
                                ? "自動選択"
                                : device.Description;
                            var isCurrent = string.Equals(audioDeviceState.CurrentDevice, device.Name, StringComparison.Ordinal);
                            var flags = MenuFlagString | (isCurrent ? MenuFlagChecked : 0);
                            _ = AppendMenuW(deviceMenu, flags, MenuCmdAudioDeviceBase + (uint)i, label);
                        }

                        _ = AppendMenuW(volumeMenu, MenuFlagSeparator, 0, string.Empty);
                        _ = AppendMenuW(volumeMenu, MenuFlagPopup, (nuint)deviceMenu, "音声出力先");
                    }
                }

                _ = AppendMenuW(volumeMenu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(volumeMenu, MenuFlagString | (currentAudio.Muted ? MenuFlagChecked : 0), MenuCmdToggleMute, "ミュート (M)");
                _ = AppendMenuW(menu, MenuFlagPopup, (nuint)volumeMenu, $"音量 ({currentAudio.FormatVolumeLabel()})");
            }

            var contactUrl = _options.BbsSession?.Snapshot.Target.ThreadUrl
                ?? _options.BbsSession?.Snapshot.Target.BoardUrl
                ?? _options.ContactUrl;
            if (!string.IsNullOrWhiteSpace(contactUrl))
            {
                _ = AppendMenuW(menu, MenuFlagSeparator, 0, string.Empty);
                _ = AppendMenuW(menu, MenuFlagString, MenuCmdOpenBrowser, "コンタクトURLをブラウザで開く");
            }

            if (!GetCursorPos(out var cursor))
            {
                return;
            }

            var selected = TrackPopupMenuEx(menu, TrackPopupReturnCmd, cursor.X, cursor.Y, window, 0);
            if (selected >= MenuCmdSpeedBase &&
                selected < MenuCmdSpeedBase + (uint)LibMpvPlaybackClient.SpeedPresets.Length)
            {
                var speed = LibMpvPlaybackClient.SpeedPresets[(int)(selected - MenuCmdSpeedBase)];
                ApplyPlaybackSpeed(speed, forceNotify: true);
                return;
            }

            if (selected >= MenuCmdScaleBase &&
                selected < MenuCmdScaleBase + (uint)LibMpvPlaybackWindowState.ScalePresets.Length)
            {
                var scalePercent = LibMpvPlaybackWindowState.ScalePresets[(int)(selected - MenuCmdScaleBase)];
                ApplyVideoScale(window, scalePercent);
                return;
            }

            if (selected >= MenuCmdVolumePresetBase &&
                selected < MenuCmdVolumePresetBase + (uint)VolumePresets.Length)
            {
                var presetVolume = VolumePresets[(int)(selected - MenuCmdVolumePresetBase)];
                if (_audio.SetVolume(presetVolume))
                {
                    SyncControls();
                }
                return;
            }

            if (selected >= MenuCmdAudioDeviceBase &&
                selected < MenuCmdAudioDeviceBase + (uint)audioDevices.Length)
            {
                AudioDeviceSelected?.Invoke(audioDevices[(int)(selected - MenuCmdAudioDeviceBase)].Name);
                return;
            }

            switch (selected)
            {
                case MenuCmdTogglePause:
                    TogglePause();
                    break;
                case MenuCmdReload:
                    RequestReload();
                    break;
                case MenuCmdSeekBack30:
                    SeekRequested?.Invoke(-30.0);
                    break;
                case MenuCmdSeekBack5:
                    SeekRequested?.Invoke(-5.0);
                    break;
                case MenuCmdSeekForward5:
                    SeekRequested?.Invoke(5.0);
                    break;
                case MenuCmdSeekForward30:
                    SeekRequested?.Invoke(30.0);
                    break;
                case MenuCmdVolumeUp:
                    if (_audio.AdjustVolume(LibMpvPlaybackAudioState.VolumeStep))
                    {
                        SyncControls();
                    }
                    break;
                case MenuCmdVolumeDown:
                    if (_audio.AdjustVolume(-LibMpvPlaybackAudioState.VolumeStep))
                    {
                        SyncControls();
                    }
                    break;
                case MenuCmdToggleAutoResize100:
                    _autoResizeTo100PercentOnStart = !_autoResizeTo100PercentOnStart;
                    UpdateSavedWindowState(s => s with { AutoResizeTo100PercentOnStart = _autoResizeTo100PercentOnStart });
                    break;
                case MenuCmdToggleAlwaysOnTop:
                    ToggleAlwaysOnTop(window);
                    break;
                case MenuCmdToggleBorderless:
                    ToggleBorderless(window);
                    break;
                case MenuCmdToggleFullscreen:
                    ToggleFullscreen(window);
                    break;
                case MenuCmdScreenshot:
                    ScreenshotRequested?.Invoke();
                    break;
                case MenuCmdOpenScreenshotFolder:
                    OpenScreenshotFolder();
                    break;
                case MenuCmdBrightUp:
                    ApplyVideoAdjustment(_videoAdjustment with { Brightness = _videoAdjustment.Brightness + LibMpvVideoAdjustment.Step }, "明るさ");
                    break;
                case MenuCmdBrightDown:
                    ApplyVideoAdjustment(_videoAdjustment with { Brightness = _videoAdjustment.Brightness - LibMpvVideoAdjustment.Step }, "明るさ");
                    break;
                case MenuCmdContrastUp:
                    ApplyVideoAdjustment(_videoAdjustment with { Contrast = _videoAdjustment.Contrast + LibMpvVideoAdjustment.Step }, "コントラスト");
                    break;
                case MenuCmdContrastDown:
                    ApplyVideoAdjustment(_videoAdjustment with { Contrast = _videoAdjustment.Contrast - LibMpvVideoAdjustment.Step }, "コントラスト");
                    break;
                case MenuCmdSatUp:
                    ApplyVideoAdjustment(_videoAdjustment with { Saturation = _videoAdjustment.Saturation + LibMpvVideoAdjustment.Step }, "彩度");
                    break;
                case MenuCmdSatDown:
                    ApplyVideoAdjustment(_videoAdjustment with { Saturation = _videoAdjustment.Saturation - LibMpvVideoAdjustment.Step }, "彩度");
                    break;
                case MenuCmdGammaUp:
                    ApplyVideoAdjustment(_videoAdjustment with { Gamma = _videoAdjustment.Gamma + LibMpvVideoAdjustment.Step }, "ガンマ");
                    break;
                case MenuCmdGammaDown:
                    ApplyVideoAdjustment(_videoAdjustment with { Gamma = _videoAdjustment.Gamma - LibMpvVideoAdjustment.Step }, "ガンマ");
                    break;
                case MenuCmdVideoAdjustReset:
                    ApplyVideoAdjustment(LibMpvVideoAdjustment.Default, "映像調整をリセット");
                    break;
                case MenuCmdResizeSmall:
                    ResizeVideoArea(window, 640, 360);
                    break;
                case MenuCmdResizeMedium:
                    ResizeVideoArea(window, 960, 540);
                    break;
                case MenuCmdResizeHd:
                    ResizeVideoArea(window, 1280, 720);
                    break;
                case MenuCmdResizeLarge:
                    ResizeVideoArea(window, 1600, 900);
                    break;
                case MenuCmdToggleMute:
                    if (_audio.ToggleMute())
                    {
                        SyncControls();
                    }

                    break;
                case MenuCmdOpenBrowser when !string.IsNullOrWhiteSpace(contactUrl):
                    try
                    {
                        _ = Process.Start(new ProcessStartInfo(contactUrl) { UseShellExecute = true });
                    }
                    catch
                    {
                    }

                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void ApplyVideoScale(nint window, int scalePercent)
    {
        var nativeW = Volatile.Read(ref _nativeVideoWidth);
        var nativeH = Volatile.Read(ref _nativeVideoHeight);
        var (width, height) = LibMpvPlaybackWindowState.ScaleVideoSize(nativeW, nativeH, scalePercent);
        _userResized = true;
        ResizeVideoArea(window, width, height);
    }

    private void TryApplyInitialAutoScale(nint window)
    {
        if (!_autoResizeTo100PercentOnStart || _appliedInitialAutoResize || _userResized || _fullscreen || _borderlessMaximized)
        {
            return;
        }

        var nativeW = Volatile.Read(ref _nativeVideoWidth);
        var nativeH = Volatile.Read(ref _nativeVideoHeight);
        if (nativeW <= 0 || nativeH <= 0)
        {
            return;
        }

        _appliedInitialAutoResize = true;
        var (width, height) = LibMpvPlaybackWindowState.ScaleVideoSize(nativeW, nativeH, 100);
        ResizeVideoArea(window, width, height);
    }

    private void ApplyVideoAdjustment(LibMpvVideoAdjustment next, string label)
    {
        _videoAdjustment = next.Clamped();
        var summary = _videoAdjustment.IsDefault
            ? "映像調整をリセット"
            : $"{label}: 明るさ{_videoAdjustment.Brightness:+0;-0;0} / コントラスト{_videoAdjustment.Contrast:+0;-0;0} / 彩度{_videoAdjustment.Saturation:+0;-0;0} / ガンマ{_videoAdjustment.Gamma:+0;-0;0}";
        VideoAdjustmentChanged?.Invoke(_videoAdjustment, summary);
    }

    private static void OpenScreenshotFolder()
    {
        try
        {
            var directory = LibMpvScreenshotPaths.ResolveDefaultDirectory();
            Directory.CreateDirectory(directory);
            _ = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static nint HitTestBorderlessEdge(nint window, nint lParam)
    {
        if (!GetWindowRect(window, out var rect))
        {
            return 0;
        }

        var screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var onLeft = screenX >= rect.Left && screenX < rect.Left + BorderlessResizeEdge;
        var onRight = screenX <= rect.Right && screenX > rect.Right - BorderlessResizeEdge;
        var onTop = screenY >= rect.Top && screenY < rect.Top + BorderlessResizeEdge;
        var onBottom = screenY <= rect.Bottom && screenY > rect.Bottom - BorderlessResizeEdge;

        if (onTop && onLeft) return HitTopLeft;
        if (onTop && onRight) return HitTopRight;
        if (onBottom && onLeft) return HitBottomLeft;
        if (onBottom && onRight) return HitBottomRight;
        if (onLeft) return HitLeft;
        if (onRight) return HitRight;
        if (onTop) return HitTop;
        if (onBottom) return HitBottom;
        return 0;
    }

    private static void SnapMovingRect(nint window, nint rectPtr)
    {
        var rect = Marshal.PtrToStructure<WindowRectangle>(rectPtr);
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var (snappedLeft, snappedTop) = LibMpvPlaybackWindowState.SnapToWorkArea(
            rect.Left,
            rect.Top,
            width,
            height,
            info.Work.Left,
            info.Work.Top,
            info.Work.Right,
            info.Work.Bottom);
        rect.Left = snappedLeft;
        rect.Top = snappedTop;
        rect.Right = snappedLeft + width;
        rect.Bottom = snappedTop + height;
        Marshal.StructureToPtr(rect, rectPtr, false);
    }

    private bool IsClientPointInVideoArea(nint window, nint lParam)
    {
        if (!GetClientRect(window, out var client))
        {
            return false;
        }

        var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var videoBottom = Math.Max(0, (client.Bottom - client.Top) - BottomBarsHeight);
        return y >= 0 && y < videoBottom;
    }

    private void CaptureNormalWindowBounds(nint window)
    {
        if (_fullscreen || window == 0 || !GetWindowRect(window, out var outer) || !GetClientRect(window, out var client))
        {
            return;
        }

        var videoWidth = client.Right - client.Left;
        var videoHeight = (client.Bottom - client.Top) - BottomBarsHeight;
        if (videoWidth < LibMpvPlaybackWindowState.MinVideoWidth || videoHeight < LibMpvPlaybackWindowState.MinVideoHeight)
        {
            return;
        }

        UpdateSavedWindowState(s => s with
        {
            Left = outer.Left,
            Top = outer.Top,
            VideoWidth = videoWidth,
            VideoHeight = videoHeight,
        });
    }

    private void UpdateSavedWindowState(Func<LibMpvPlaybackWindowState, LibMpvPlaybackWindowState> updater)
    {
        LibMpvPlaybackWindowState updated;
        lock (_sync)
        {
            var next = updater(_windowState).Normalized();
            if (next == _windowState)
            {
                return;
            }

            _windowState = next;
            updated = next;
        }

        WindowStateChanged?.Invoke(updated);
    }

    private void SyncControls()
    {
        var state = _audio.Current;
        if (_volumeButton != 0)
        {
            _ = SetWindowTextW(_volumeButton, state.FormatVolumeLabel());
            _ = InvalidateRect(_volumeButton, 0, true);
        }
    }

    private void FailStart(string message)
    {
        var exception = new LibMpvPlaybackException(message);
        _started.TrySetException(exception);
        _closed.TrySetException(exception);
    }

    private static readonly WindowProcedure DefaultVideoWindowProcedure = DefWindowProcW;

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControlsExData
    {
        public uint Size;
        public uint Icc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
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
    private struct PaintStruct
    {
        public nint DeviceContext;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Erase;
        public WindowRectangle PaintRect;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Restore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[]? Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawItemStruct
    {
        public uint ControlType;
        public uint ControlId;
        public uint ItemId;
        public uint ItemAction;
        public uint ItemState;
        public nint ItemWindow;
        public nint DeviceContext;
        public WindowRectangle ItemRect;
        public nuint ItemData;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public WindowRectangle Monitor;
        public WindowRectangle Work;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitCommonControlsEx(ref InitCommonControlsExData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref HighContrast state, uint flags);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out WindowRectangle rect);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint window, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref WindowRectangle rect, nint brush);

    [DllImport("user32.dll")]
    private static extern int FrameRect(nint hdc, ref WindowRectangle rect, nint brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(nint hdc, string text, int count, ref WindowRectangle rect, uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        nint window,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint window, string text);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int objectIndex);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(nint hdc, int color);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(nint hdc, int color);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ExtractIconExW(
        string fileName, int iconIndex, out nint largeIcon, out nint smallIcon, uint iconCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out WindowPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, [Out] char[] buffer, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint window, nuint eventId, uint elapseMs, nint timerFunc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint eventId);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint itemId, string newItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        nint menu, uint flags, int x, int y, nint window, nint paramsPtr);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(WindowPoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
}

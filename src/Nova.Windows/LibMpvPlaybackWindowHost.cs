using System.Runtime.InteropServices;

namespace PeerCast.Nova.Windows;

internal sealed record LibMpvPlaybackWindowOptions
{
    public string Title { get; init; } = "PeerCast Nova - FLV Playback";
    public int VideoWidth { get; init; } = 960;
    public int VideoHeight { get; init; } = 540;
    public LibMpvPlaybackAudioState InitialAudioState { get; init; } = LibMpvPlaybackAudioState.Default;
    public string? ApplicationIconPath { get; init; } = Environment.ProcessPath;
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

    private const int ControlBarHeight = 36;
    private const int BarMargin = 8;
    private const int ControlGap = 8;
    private const int MuteButtonWidth = 68;
    private const int VolumeLabelWidth = 54;
    private const int MaxSliderWidth = 220;
    private const int ControlHeight = 24;

    private const uint ClassStyleOwnDeviceContext = 0x0020;
    private const uint WindowStyleOverlappedClipChildren = 0x02CF0000;
    private const uint ChildVideoDisabled = 0x58000000;
    private const uint ChildOwnerDrawButtonTabStop = 0x5001000B; // WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW
    private const uint ChildTrackbar = 0x50010018;
    private const uint ChildStaticRightCenter = 0x50000202;
    private const int ShowWindowNormal = 1;
    private const int CursorArrow = 32512;
    private const int StockBlackBrush = 4;
    private const int StockDefaultGuiFont = 17;
    private const uint CommonControlsBarClasses = 0x00000004;

    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessageSize = 0x0005;
    private const uint WindowMessagePaint = 0x000F;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDrawItem = 0x002B;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageNotify = 0x004E;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageHScroll = 0x0114;
    private const uint WindowMessageCtlColorStatic = 0x0138;
    private const uint WindowMessageMouseWheel = 0x020A;

    private const uint TrackbarGetPos = 0x0400;
    private const uint TrackbarSetPos = 0x0405;
    private const uint TrackbarSetRangeMin = 0x0407;
    private const uint TrackbarSetRangeMax = 0x0408;
    private const uint TrackbarSetPageSize = 0x0415;

    private const int NmCustomDraw = -12;
    private const uint CddsPrePaint = 0x00000001;
    private const uint CddsItemPrePaint = 0x00010001;
    private const int CdrfDoDefault = 0x00000000;
    private const int CdrfSkipDefault = 0x00000004;
    private const int CdrfNotifyItemDraw = 0x00000020;
    private const nuint TbcdThumb = 2;
    private const nuint TbcdChannel = 3;

    private const uint OdsSelected = 0x0001;
    private const uint OdsFocus = 0x0010;
    private const int BkModeTransparent = 1;
    private const uint DrawTextSingleLineCenter = 0x00000025; // DT_CENTER | DT_VCENTER | DT_SINGLELINE

    private const int ButtonClicked = 0;
    private const int MuteButtonId = 1001;
    private const int VolumeSliderId = 1002;
    private const int VolumeLabelId = 1003;

    private const nuint VirtualKeyUp = 0x26;
    private const nuint VirtualKeyDown = 0x28;
    private const nuint VirtualKeyM = 0x4D;

    private readonly object _sync = new();
    private readonly LibMpvPlaybackWindowOptions _options;
    private readonly LibMpvPlaybackAudioController _audio;
    private readonly WindowProcedure _windowProcedure;
    private readonly TaskCompletionSource<uint> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private nint _window;
    private nint _videoWindow;
    private nint _muteButton;
    private nint _volumeSlider;
    private nint _volumeLabel;
    private nint _panelBrush;
    private nint _inputBrush;
    private nint _borderBrush;
    private nint _separatorBrush;
    private nint _selectedBrush;
    private nint _hoverBrush;
    private nint _accentBrush;
    private nint _textBrush;
    private nint _secondaryTextBrush;
    private nint _disabledTextBrush;
    private bool _stopRequested;
    private bool _disposed;
    private bool _syncingControls;

    public LibMpvPlaybackWindowHost(LibMpvPlaybackWindowOptions? options = null)
    {
        _options = options is null ? new() : options with { };
        _audio = new(_options.InitialAudioState);
        _audio.StateChanged += state => AudioStateChanged?.Invoke(state);
        _windowProcedure = ProcessWindowMessage;
    }

    public event Action<LibMpvPlaybackAudioState>? AudioStateChanged;

    public LibMpvPlaybackAudioState AudioState => _audio.Current;

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
            Style = ClassStyleOwnDeviceContext,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            BackgroundBrush = _panelBrush != 0 ? _panelBrush : (nint)16,
            ClassName = mainClassName,
        };
        var videoClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = ClassStyleOwnDeviceContext,
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

            var clientWidth = Math.Max(320, _options.VideoWidth);
            var clientHeight = Math.Max(180, _options.VideoHeight) + ControlBarHeight;
            var rectangle = new WindowRectangle
            {
                Left = 0,
                Top = 0,
                Right = clientWidth,
                Bottom = clientHeight,
            };
            if (!AdjustWindowRectEx(ref rectangle, WindowStyleOverlappedClipChildren, false, 0))
            {
                FailStart("The embedded playback window bounds could not be calculated.");
                return;
            }

            window = CreateWindowExW(
                0,
                mainClassName,
                _options.Title,
                WindowStyleOverlappedClipChildren,
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
                FailStart("The embedded playback window could not be created.");
                return;
            }

            ApplyDarkTitleBar(window);

            _videoWindow = CreateWindowExW(
                0,
                videoClassName,
                string.Empty,
                ChildVideoDisabled,
                0,
                0,
                clientWidth,
                clientHeight - ControlBarHeight,
                window,
                0,
                instance,
                0);
            _muteButton = CreateWindowExW(
                0,
                "BUTTON",
                _audio.Current.FormatMuteButtonText(),
                ChildOwnerDrawButtonTabStop,
                0,
                0,
                MuteButtonWidth,
                ControlHeight,
                window,
                (nint)MuteButtonId,
                instance,
                0);
            _volumeSlider = CreateWindowExW(
                0,
                "msctls_trackbar32",
                string.Empty,
                ChildTrackbar,
                0,
                0,
                MaxSliderWidth,
                ControlHeight,
                window,
                (nint)VolumeSliderId,
                instance,
                0);
            _volumeLabel = CreateWindowExW(
                0,
                "STATIC",
                _audio.Current.FormatVolumeLabel(),
                ChildStaticRightCenter,
                0,
                0,
                VolumeLabelWidth,
                ControlHeight,
                window,
                (nint)VolumeLabelId,
                instance,
                0);

            if (_videoWindow == 0 || _muteButton == 0 || _volumeSlider == 0 || _volumeLabel == 0)
            {
                FailStart("The embedded playback window controls could not be created.");
                return;
            }

            var font = GetStockObject(StockDefaultGuiFont);
            if (font != 0)
            {
                _ = SendMessageW(_muteButton, WindowMessageSetFont, (nuint)font, 1);
                _ = SendMessageW(_volumeLabel, WindowMessageSetFont, (nuint)font, 1);
            }

            _ = SendMessageW(_volumeSlider, TrackbarSetRangeMin, 0, LibMpvPlaybackAudioState.MinVolume);
            _ = SendMessageW(_volumeSlider, TrackbarSetRangeMax, 0, LibMpvPlaybackAudioState.MaxVolume);
            _ = SendMessageW(_volumeSlider, TrackbarSetPageSize, 0, LibMpvPlaybackAudioState.VolumeStep * 2);
            SyncControls();
            LayoutControls(clientWidth, clientHeight);

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

                if (message.Message == WindowMessageMouseWheel)
                {
                    var wheelDelta = unchecked((short)((message.WParam >> 16) & 0xFFFF));
                    if (_audio.ApplyWheelDelta(wheelDelta))
                    {
                        SyncControls();
                    }

                    continue;
                }

                if (message.Message == WindowMessageKeyDown && TryHandleKeyDown(message.WParam))
                {
                    continue;
                }

                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
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
        _textBrush = CreateSolidBrush(ColorText);
        _secondaryTextBrush = CreateSolidBrush(ColorSecondaryText);
        _disabledTextBrush = CreateSolidBrush(ColorDisabledText);
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
        DeleteBrush(ref _textBrush);
        DeleteBrush(ref _secondaryTextBrush);
        DeleteBrush(ref _disabledTextBrush);
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

    private bool TryHandleKeyDown(nuint virtualKey)
    {
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
            case WindowMessageSize:
                var width = unchecked((int)(lParam.ToInt64() & 0xFFFF));
                var height = unchecked((int)((lParam.ToInt64() >> 16) & 0xFFFF));
                LayoutControls(width, height);
                return 0;

            case WindowMessagePaint:
                PaintControlBar(window);
                return 0;

            case WindowMessageCtlColorStatic:
                if (wParam != 0)
                {
                    var hdc = (nint)wParam;
                    _ = SetBkColor(hdc, ColorPanel);
                    _ = SetTextColor(hdc, _audio.Current.Muted ? ColorSecondaryText : ColorText);
                    if (_panelBrush != 0)
                    {
                        return _panelBrush;
                    }
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageDrawItem:
                if (lParam != 0 && unchecked((int)wParam) == MuteButtonId)
                {
                    DrawMuteButton(lParam);
                    return 1;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageNotify:
                if (lParam != 0)
                {
                    var header = Marshal.PtrToStructure<NmHdr>(lParam);
                    if (header.HwndFrom == _volumeSlider && header.Code == NmCustomDraw)
                    {
                        return HandleSliderCustomDraw(lParam);
                    }
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageCommand:
                var notification = (int)((wParam >> 16) & 0xFFFF);
                if (notification == ButtonClicked && lParam == _muteButton && _muteButton != 0)
                {
                    if (_audio.ToggleMute())
                    {
                        SyncControls();
                    }

                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageHScroll:
                if (lParam == _volumeSlider && _volumeSlider != 0 && !_syncingControls)
                {
                    var position = unchecked((int)SendMessageW(_volumeSlider, TrackbarGetPos, 0, 0).ToInt64());
                    if (_audio.SetVolume(position))
                    {
                        SyncControls(updateSlider: false);
                    }

                    return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);

            case WindowMessageClose:
                _ = DestroyWindow(window);
                return 0;

            case WindowMessageDestroy:
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private void PaintControlBar(nint window)
    {
        var hdc = BeginPaint(window, out var paint);
        try
        {
            if (hdc != 0 && GetClientRect(window, out var client))
            {
                var barTop = Math.Max(0, client.Bottom - ControlBarHeight);
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

                if (_separatorBrush != 0 && barTop < client.Bottom)
                {
                    var separatorRect = new WindowRectangle
                    {
                        Left = 0,
                        Top = barTop,
                        Right = client.Right,
                        Bottom = barTop + 1,
                    };
                    _ = FillRect(hdc, ref separatorRect, _separatorBrush);
                }
            }
        }
        finally
        {
            _ = EndPaint(window, ref paint);
        }
    }

    private void DrawMuteButton(nint drawItemPointer)
    {
        var item = Marshal.PtrToStructure<DrawItemStruct>(drawItemPointer);
        var state = _audio.Current;
        var pressed = (item.ItemState & OdsSelected) != 0;
        var focused = (item.ItemState & OdsFocus) != 0;
        var fillBrush = pressed
            ? _hoverBrush
            : state.Muted
                ? _selectedBrush
                : _inputBrush;
        var borderBrush = pressed || focused || state.Muted
            ? _accentBrush
            : _borderBrush;

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
            _ = SetTextColor(item.DeviceContext, ColorText);
            var text = state.FormatMuteButtonText();
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

    private nint HandleSliderCustomDraw(nint customDrawPointer)
    {
        var draw = Marshal.PtrToStructure<NmCustomDrawStruct>(customDrawPointer);
        if (draw.DrawStage == CddsPrePaint)
        {
            return CdrfNotifyItemDraw;
        }

        if (draw.DrawStage != CddsItemPrePaint)
        {
            return CdrfDoDefault;
        }

        if (draw.ItemSpec == TbcdChannel)
        {
            var channel = draw.Rect;
            if (_inputBrush != 0)
            {
                _ = FillRect(draw.DeviceContext, ref channel, _inputBrush);
            }

            var state = _audio.Current;
            var width = Math.Max(0, channel.Right - channel.Left);
            var filledWidth = (int)Math.Round(
                width * (state.Volume / (double)LibMpvPlaybackAudioState.MaxVolume));
            if (filledWidth > 0)
            {
                var filled = new WindowRectangle
                {
                    Left = channel.Left,
                    Top = channel.Top,
                    Right = channel.Left + filledWidth,
                    Bottom = channel.Bottom,
                };
                var fillBrush = state.Muted ? _disabledTextBrush : _accentBrush;
                if (fillBrush != 0)
                {
                    _ = FillRect(draw.DeviceContext, ref filled, fillBrush);
                }
            }

            if (_borderBrush != 0)
            {
                _ = FrameRect(draw.DeviceContext, ref channel, _borderBrush);
            }

            return CdrfSkipDefault;
        }

        if (draw.ItemSpec == TbcdThumb)
        {
            var thumb = draw.Rect;
            var state = _audio.Current;
            var thumbFill = state.Muted ? _secondaryTextBrush : _textBrush;
            var thumbBorder = state.Muted ? _borderBrush : _accentBrush;
            if (thumbFill != 0)
            {
                _ = FillRect(draw.DeviceContext, ref thumb, thumbFill);
            }

            if (thumbBorder != 0)
            {
                _ = FrameRect(draw.DeviceContext, ref thumb, thumbBorder);
            }

            return CdrfSkipDefault;
        }

        return CdrfDoDefault;
    }

    private void LayoutControls(int clientWidth, int clientHeight)
    {
        if (_videoWindow == 0)
        {
            return;
        }

        var safeWidth = Math.Max(0, clientWidth);
        var safeHeight = Math.Max(0, clientHeight);
        var videoHeight = Math.Max(0, safeHeight - ControlBarHeight);
        _ = MoveWindow(_videoWindow, 0, 0, safeWidth, videoHeight, true);

        var controlTop = videoHeight + ((ControlBarHeight - ControlHeight) / 2);
        var muteX = BarMargin;
        var sliderX = muteX + MuteButtonWidth + ControlGap;
        var availableSliderWidth = Math.Max(
            60,
            safeWidth - sliderX - ControlGap - VolumeLabelWidth - BarMargin);
        var sliderWidth = Math.Min(MaxSliderWidth, availableSliderWidth);
        var labelX = sliderX + sliderWidth + ControlGap;

        if (_muteButton != 0)
        {
            _ = MoveWindow(_muteButton, muteX, controlTop, MuteButtonWidth, ControlHeight, true);
        }

        if (_volumeSlider != 0)
        {
            _ = MoveWindow(_volumeSlider, sliderX, controlTop, sliderWidth, ControlHeight, true);
        }

        if (_volumeLabel != 0)
        {
            _ = MoveWindow(_volumeLabel, labelX, controlTop, VolumeLabelWidth, ControlHeight, true);
        }

        if (_window != 0)
        {
            _ = InvalidateRect(_window, 0, true);
        }
    }

    private void SyncControls(bool updateSlider = true)
    {
        var state = _audio.Current;
        _syncingControls = true;
        try
        {
            if (updateSlider && _volumeSlider != 0)
            {
                _ = SendMessageW(_volumeSlider, TrackbarSetPos, 1, state.Volume);
            }

            if (_volumeSlider != 0)
            {
                _ = InvalidateRect(_volumeSlider, 0, true);
            }

            if (_muteButton != 0)
            {
                _ = SetWindowTextW(_muteButton, state.FormatMuteButtonText());
                _ = InvalidateRect(_muteButton, 0, true);
            }

            if (_volumeLabel != 0)
            {
                _ = SetWindowTextW(_volumeLabel, state.FormatVolumeLabel());
                _ = InvalidateRect(_volumeLabel, 0, true);
            }
        }
        finally
        {
            _syncingControls = false;
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
    private struct NmHdr
    {
        public nint HwndFrom;
        public nuint IdFrom;
        public int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NmCustomDrawStruct
    {
        public NmHdr Header;
        public uint DrawStage;
        public nint DeviceContext;
        public WindowRectangle Rect;
        public nuint ItemSpec;
        public uint ItemState;
        public nint ItemLParam;
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
}

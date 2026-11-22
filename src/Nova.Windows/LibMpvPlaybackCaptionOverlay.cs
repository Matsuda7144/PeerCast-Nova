using System.Runtime.InteropServices;

namespace PeerCast.Nova.Windows;

internal enum LibMpvCaptionOverlayButton
{
    None = 0,
    Minimize = 1,
    Maximize = 2,
    Close = 3,
}

internal sealed class LibMpvPlaybackCaptionOverlay : IDisposable
{
    internal const int ButtonWidth = 30;
    internal const int ButtonHeight = 24;
    internal const int TotalWidth = ButtonWidth * 3;
    internal const int TotalHeight = ButtonHeight;
    internal const int EdgeInset = 4;

    private const uint WindowStylePopup = 0x80000000; // WS_POPUP
    private const uint ExStylePopupLayered = 0x08080080; // WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW
    private const int ShowNoActivate = 4; // SW_SHOWNOACTIVATE
    private const int HideWindow = 0; // SW_HIDE
    private const int CursorArrow = 32512;

    private const uint WindowMessageMouseMove = 0x0200;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageMouseWheel = 0x020A;
    private const uint WindowMessageMouseLeave = 0x02A3;
    private const uint TrackMouseLeave = 0x00000002;

    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const uint DibRgbColors = 0;

    private readonly nint _instance;
    private readonly nint _ownerWindow;
    private readonly string _className;
    private readonly WindowProcedure _windowProcedure;
    private ushort _classAtom;
    private nint _window;
    private LibMpvCaptionOverlayButton _hoveredButton;
    private bool _maximized;
    private bool _visible;
    private bool _trackingMouseLeave;
    private bool _disposed;
    private int _screenX;
    private int _screenY;

    public LibMpvPlaybackCaptionOverlay(nint instance, nint ownerWindow)
    {
        _instance = instance;
        _ownerWindow = ownerWindow;
        _className = $"PeerCastNovaCaptionOverlay-{Guid.NewGuid():N}";
        _windowProcedure = ProcessWindowMessage;

        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            ClassName = _className,
        };
        _classAtom = RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            return;
        }

        _window = CreateWindowExW(
            ExStylePopupLayered,
            _className,
            string.Empty,
            WindowStylePopup,
            0,
            0,
            TotalWidth,
            TotalHeight,
            ownerWindow,
            0,
            instance,
            0);
    }

    public event Action<LibMpvCaptionOverlayButton>? ButtonClicked;

    public bool IsVisible => _visible && _window != 0;

    internal static LibMpvCaptionOverlayButton HitTestButton(int localX, int localY)
    {
        if (localX < 0 || localX >= TotalWidth || localY < 0 || localY >= TotalHeight)
        {
            return LibMpvCaptionOverlayButton.None;
        }

        return (localX / ButtonWidth) switch
        {
            0 => LibMpvCaptionOverlayButton.Minimize,
            1 => LibMpvCaptionOverlayButton.Maximize,
            2 => LibMpvCaptionOverlayButton.Close,
            _ => LibMpvCaptionOverlayButton.None,
        };
    }

    public void ShowAtTopRight(int ownerRightScreenX, int ownerTopScreenY, bool maximized)
    {
        if (_disposed || _window == 0)
        {
            return;
        }

        var nextX = ownerRightScreenX - TotalWidth - EdgeInset;
        var nextY = ownerTopScreenY + EdgeInset;
        var needsRedraw = !_visible || _maximized != maximized || _screenX != nextX || _screenY != nextY;
        _screenX = nextX;
        _screenY = nextY;
        _maximized = maximized;

        if (needsRedraw)
        {
            RenderLayered();
        }

        if (!_visible)
        {
            _visible = true;
            _ = ShowWindow(_window, ShowNoActivate);
        }
    }

    public void Hide()
    {
        if (!_visible || _window == 0)
        {
            return;
        }

        _visible = false;
        _hoveredButton = LibMpvCaptionOverlayButton.None;
        _trackingMouseLeave = false;
        _ = ShowWindow(_window, HideWindow);
    }

    public bool ContainsScreenPoint(int screenX, int screenY)
    {
        if (!_visible || _window == 0 || !GetWindowRect(_window, out var rect))
        {
            return false;
        }

        return screenX >= rect.Left && screenX <= rect.Right &&
               screenY >= rect.Top && screenY <= rect.Bottom;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _visible = false;

        if (_window != 0 && IsWindow(_window))
        {
            _ = DestroyWindow(_window);
        }

        _window = 0;

        if (_classAtom != 0)
        {
            _ = UnregisterClassW(_className, _instance);
            _classAtom = 0;
        }
    }

    private nint ProcessWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessageMouseMove:
                var moveX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                var moveY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                var hovered = HitTestButton(moveX, moveY);
                if (hovered != _hoveredButton)
                {
                    _hoveredButton = hovered;
                    RenderLayered();
                }

                if (!_trackingMouseLeave)
                {
                    var tme = new TrackMouseEventData
                    {
                        Size = (uint)Marshal.SizeOf<TrackMouseEventData>(),
                        Flags = TrackMouseLeave,
                        Window = window,
                    };
                    _trackingMouseLeave = TrackMouseEvent(ref tme);
                }

                return 0;

            case WindowMessageMouseLeave:
                _trackingMouseLeave = false;
                if (_hoveredButton != LibMpvCaptionOverlayButton.None)
                {
                    _hoveredButton = LibMpvCaptionOverlayButton.None;
                    RenderLayered();
                }

                return 0;

            case WindowMessageLeftButtonUp:
                var clickX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                var clickY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                var clicked = HitTestButton(clickX, clickY);
                if (clicked != LibMpvCaptionOverlayButton.None)
                {
                    ButtonClicked?.Invoke(clicked);
                }

                return 0;

            case WindowMessageMouseWheel:
                if (_ownerWindow != 0 && IsWindow(_ownerWindow))
                {
                    _ = PostMessageW(_ownerWindow, WindowMessageMouseWheel, wParam, lParam);
                }

                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private void RenderLayered()
    {
        if (_window == 0)
        {
            return;
        }

        var pixels = new uint[TotalWidth * TotalHeight];
        // Alpha = 1 keeps the entire 30x24 button hitboxes clickable while remaining visually transparent.
        const uint transparentClickable = 0x01000000;
        Array.Fill(pixels, transparentClickable);

        if (_hoveredButton != LibMpvCaptionOverlayButton.None)
        {
            var buttonIndex = (int)_hoveredButton - 1;
            var left = buttonIndex * ButtonWidth + 1;
            var right = (buttonIndex + 1) * ButtonWidth - 1;
            var hoverColor = _hoveredButton == LibMpvCaptionOverlayButton.Close
                ? Premultiply(215, 52, 58, 210)
                : Premultiply(20, 35, 55, 160);
            FillPixels(pixels, left, 1, right, TotalHeight - 1, hoverColor);
        }

        DrawMinimizeGlyph(pixels, offsetX: 0);
        DrawMaximizeGlyph(pixels, offsetX: ButtonWidth, _maximized);
        DrawCloseGlyph(pixels, offsetX: ButtonWidth * 2);

        var screenDc = GetDC(0);
        if (screenDc == 0)
        {
            return;
        }

        nint memDc = 0;
        nint dib = 0;
        nint oldBitmap = 0;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == 0)
            {
                return;
            }

            var bmi = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = TotalWidth,
                    Height = -TotalHeight, // top-down DIB
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
            };
            dib = CreateDIBSection(memDc, ref bmi, DibRgbColors, out var bitsPtr, 0, 0);
            if (dib == 0 || bitsPtr == 0)
            {
                return;
            }

            Marshal.Copy(MemoryMarshal.Cast<uint, int>(pixels).ToArray(), 0, bitsPtr, pixels.Length);
            oldBitmap = SelectObject(memDc, dib);

            var dstPoint = new WindowPoint { X = _screenX, Y = _screenY };
            var srcPoint = new WindowPoint { X = 0, Y = 0 };
            var size = new WindowSize { Width = TotalWidth, Height = TotalHeight };
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha,
            };

            _ = UpdateLayeredWindow(_window, screenDc, ref dstPoint, ref size, memDc, ref srcPoint, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (oldBitmap != 0 && memDc != 0)
            {
                _ = SelectObject(memDc, oldBitmap);
            }

            if (dib != 0)
            {
                _ = DeleteObject(dib);
            }

            if (memDc != 0)
            {
                _ = DeleteDC(memDc);
            }

            _ = ReleaseDC(0, screenDc);
        }
    }

    private static void DrawMinimizeGlyph(uint[] pixels, int offsetX)
    {
        var mask = new bool[ButtonWidth * ButtonHeight];
        FillMaskRect(mask, 10, 14, 20, 16);
        BlitOutlinedMask(pixels, mask, offsetX);
    }

    private static void DrawMaximizeGlyph(uint[] pixels, int offsetX, bool maximized)
    {
        var mask = new bool[ButtonWidth * ButtonHeight];
        if (!maximized)
        {
            StrokeMaskRect(mask, 10, 7, 20, 16, thickness: 2);
        }
        else
        {
            // Overlapping restore squares.
            StrokeMaskRect(mask, 12, 6, 20, 13, thickness: 1);
            StrokeMaskRect(mask, 9, 9, 17, 16, thickness: 2);
        }

        BlitOutlinedMask(pixels, mask, offsetX);
    }

    private static void DrawCloseGlyph(uint[] pixels, int offsetX)
    {
        var mask = new bool[ButtonWidth * ButtonHeight];
        const int x0 = 10;
        const int y0 = 7;
        const int size = 9;
        for (var i = 0; i < size; i++)
        {
            SetMaskPixel(mask, x0 + i, y0 + i);
            SetMaskPixel(mask, x0 + i + 1, y0 + i);
            SetMaskPixel(mask, x0 + (size - 1 - i), y0 + i);
            SetMaskPixel(mask, x0 + (size - i), y0 + i);
        }

        BlitOutlinedMask(pixels, mask, offsetX);
    }

    private static void BlitOutlinedMask(uint[] pixels, bool[] mask, int offsetX)
    {
        var outlineColor = Premultiply(24, 24, 24, 225);
        var foregroundColor = Premultiply(255, 255, 255, 255);

        for (var y = 0; y < ButtonHeight; y++)
        {
            for (var x = 0; x < ButtonWidth; x++)
            {
                if (mask[y * ButtonWidth + x])
                {
                    continue;
                }

                var hasNeighbor = false;
                for (var dy = -1; dy <= 1 && !hasNeighbor; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= ButtonHeight)
                    {
                        continue;
                    }

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx >= 0 && nx < ButtonWidth && mask[ny * ButtonWidth + nx])
                        {
                            hasNeighbor = true;
                            break;
                        }
                    }
                }

                if (hasNeighbor)
                {
                    pixels[y * TotalWidth + (offsetX + x)] = outlineColor;
                }
            }
        }

        for (var y = 0; y < ButtonHeight; y++)
        {
            for (var x = 0; x < ButtonWidth; x++)
            {
                if (mask[y * ButtonWidth + x])
                {
                    pixels[y * TotalWidth + (offsetX + x)] = foregroundColor;
                }
            }
        }
    }

    private static void FillMaskRect(bool[] mask, int left, int top, int right, int bottom)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                SetMaskPixel(mask, x, y);
            }
        }
    }

    private static void StrokeMaskRect(bool[] mask, int left, int top, int right, int bottom, int thickness)
    {
        FillMaskRect(mask, left, top, right, top + thickness);
        FillMaskRect(mask, left, bottom - thickness, right, bottom);
        FillMaskRect(mask, left, top, left + thickness, bottom);
        FillMaskRect(mask, right - thickness, top, right, bottom);
    }

    private static void SetMaskPixel(bool[] mask, int x, int y)
    {
        if (x >= 0 && x < ButtonWidth && y >= 0 && y < ButtonHeight)
        {
            mask[y * ButtonWidth + x] = true;
        }
    }

    private static void FillPixels(uint[] pixels, int left, int top, int right, int bottom, uint color)
    {
        var clampedLeft = Math.Max(0, left);
        var clampedTop = Math.Max(0, top);
        var clampedRight = Math.Min(TotalWidth, right);
        var clampedBottom = Math.Min(TotalHeight, bottom);
        for (var y = clampedTop; y < clampedBottom; y++)
        {
            var row = y * TotalWidth;
            for (var x = clampedLeft; x < clampedRight; x++)
            {
                pixels[row + x] = color;
            }
        }
    }

    private static uint Premultiply(byte r, byte g, byte b, byte a)
    {
        var pr = (uint)(r * a / 255);
        var pg = (uint)(g * a / 255);
        var pb = (uint)(b * a / 255);
        return ((uint)a << 24) | (pr << 16) | (pg << 8) | pb;
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
    private struct WindowPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

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
    private static extern int ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rect);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TrackMouseEventData data);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint hdc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        nint window,
        nint hdcDst,
        ref WindowPoint pptDst,
        ref WindowSize psize,
        nint hdcSrc,
        ref WindowPoint pptSrc,
        uint crKey,
        ref BlendFunction pblend,
        uint dwFlags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint hdc, ref BitmapInfo bmi, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);
}

using System.Runtime.InteropServices;
using PeerCast.Nova.Playback.Bbs;

namespace PeerCast.Nova.Windows;

internal sealed class LibMpvPlaybackBbsPopup : IDisposable
{
    private const int ColorBackground = 0x002B1D0B;
    private const int ColorBorder = 0x006D4920;
    private const int ColorSeparator = 0x00463016;
    private const int ColorHeaderName = 0x00FFBF00;
    private const int ColorHeaderMeta = 0x00CBB69A;
    private const int ColorBody = 0x00FFF7EC;

    private const uint WindowStylePopup = 0x80000000; // WS_POPUP
    private const uint ExStylePopupLayered = 0x08080080; // WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW
    private const uint LayeredAlphaFlag = 0x00000002; // LWA_ALPHA
    private const byte PopupOpacity = 236;

    private const int ShowNoActivate = 4; // SW_SHOWNOACTIVATE
    private const int HideWindow = 0; // SW_HIDE
    private const int BkModeTransparent = 1;
    private const int StockDefaultGuiFont = 17;
    private const int CursorArrow = 32512;

    private const uint WindowMessagePaint = 0x000F;
    private const uint WindowMessageEraseBkgnd = 0x0014;
    private const uint WindowMessageMouseWheel = 0x020A;

    private const uint DrawTextCalcWrap = 0x00000C10; // DT_CALCRECT | DT_WORDBREAK | DT_NOPREFIX
    private const uint DrawTextWrap = 0x00000810; // DT_WORDBREAK | DT_NOPREFIX
    private const uint DrawTextSingleLineEllipsis = 0x00008820; // DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS

    private const int PaddingX = 10;
    private const int PaddingY = 6;
    private const int PostGap = 4;
    private const int HeaderGap = 2;

    private readonly nint _instance;
    private readonly nint _ownerWindow;
    private readonly string _className;
    private readonly WindowProcedure _windowProcedure;
    private ushort _classAtom;
    private nint _window;
    private nint _backgroundBrush;
    private nint _borderBrush;
    private nint _separatorBrush;
    private IReadOnlyList<BbsPost> _posts = [];
    private string? _emptyMessage;
    private int _scrollOffsetFromBottom;
    private bool _visible;
    private bool _disposed;

    public LibMpvPlaybackBbsPopup(nint instance, nint ownerWindow)
    {
        _instance = instance;
        _ownerWindow = ownerWindow;
        _className = $"PeerCastNovaBbsPopup-{Guid.NewGuid():N}";
        _windowProcedure = ProcessWindowMessage;

        _backgroundBrush = CreateSolidBrush(ColorBackground);
        _borderBrush = CreateSolidBrush(ColorBorder);
        _separatorBrush = CreateSolidBrush(ColorSeparator);

        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            BackgroundBrush = _backgroundBrush,
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
            420,
            320,
            ownerWindow,
            0,
            instance,
            0);
        if (_window != 0)
        {
            _ = SetLayeredWindowAttributes(_window, 0, PopupOpacity, LayeredAlphaFlag);
        }
    }

    public bool IsVisible => _visible && _window != 0;

    public nint Handle => _window;

    public void UpdatePosts(IReadOnlyList<BbsPost> posts, string? emptyMessage)
    {
        _posts = posts;
        _emptyMessage = emptyMessage;
        var maxOffset = Math.Max(0, _posts.Count - 1);
        if (_scrollOffsetFromBottom > maxOffset)
        {
            _scrollOffsetFromBottom = maxOffset;
        }

        if (_visible && _window != 0)
        {
            _ = InvalidateRect(_window, 0, true);
        }
    }

    public void ShowAbove(int anchorScreenLeft, int anchorScreenTop, int anchorWidth, int videoHeight)
    {
        if (_disposed || _window == 0)
        {
            return;
        }

        var popupWidth = Math.Clamp((int)(anchorWidth * 0.58), 340, Math.Max(340, Math.Min(680, anchorWidth - 16)));
        var popupHeight = Math.Clamp((int)(videoHeight * 0.78), 180, Math.Max(180, Math.Min(560, videoHeight - 8)));
        var x = anchorScreenLeft + ((anchorWidth - popupWidth) / 2);
        var y = anchorScreenTop - popupHeight;

        _ = MoveWindow(_window, x, y, popupWidth, popupHeight, true);
        if (!_visible)
        {
            _scrollOffsetFromBottom = 0;
            _visible = true;
            _ = ShowWindow(_window, ShowNoActivate);
        }

        _ = InvalidateRect(_window, 0, true);
    }

    public void Hide()
    {
        if (!_visible || _window == 0)
        {
            return;
        }

        _visible = false;
        _ = ShowWindow(_window, HideWindow);
    }

    public void ScrollByNotches(int notches)
    {
        if (_posts.Count <= 1 || notches == 0)
        {
            return;
        }

        // Wheel up (positive) scrolls up toward older posts; wheel down returns toward latest.
        var maxOffset = Math.Max(0, _posts.Count - 1);
        var next = Math.Clamp(_scrollOffsetFromBottom + (notches * 2), 0, maxOffset);
        if (next != _scrollOffsetFromBottom)
        {
            _scrollOffsetFromBottom = next;
            if (_visible && _window != 0)
            {
                _ = InvalidateRect(_window, 0, true);
            }
        }
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

        DeleteBrush(ref _backgroundBrush);
        DeleteBrush(ref _borderBrush);
        DeleteBrush(ref _separatorBrush);
    }

    private nint ProcessWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessageEraseBkgnd:
                return 1;

            case WindowMessagePaint:
                Paint(window);
                return 0;

            case WindowMessageMouseWheel:
                var delta = unchecked((short)((wParam >> 16) & 0xFFFF));
                ScrollByNotches(delta / 120);
                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private void Paint(nint window)
    {
        var hdc = BeginPaint(window, out var paint);
        try
        {
            if (hdc == 0 || !GetClientRect(window, out var client))
            {
                return;
            }

            if (_backgroundBrush != 0)
            {
                _ = FillRect(hdc, ref client, _backgroundBrush);
            }

            if (_borderBrush != 0)
            {
                _ = FrameRect(hdc, ref client, _borderBrush);
            }

            var font = GetStockObject(StockDefaultGuiFont);
            var previousFont = font != 0 ? SelectObject(hdc, font) : 0;
            try
            {
                _ = SetBkMode(hdc, BkModeTransparent);
                var contentWidth = Math.Max(40, (client.Right - client.Left) - (PaddingX * 2));
                if (_posts.Count == 0)
                {
                    var text = string.IsNullOrWhiteSpace(_emptyMessage) ? "レスはまだありません。" : _emptyMessage;
                    var emptyRect = new WindowRectangle
                    {
                        Left = PaddingX,
                        Top = PaddingY,
                        Right = client.Right - PaddingX,
                        Bottom = client.Bottom - PaddingY,
                    };
                    _ = SetTextColor(hdc, ColorHeaderMeta);
                    _ = DrawTextW(hdc, text, text.Length, ref emptyRect, DrawTextWrap);
                    return;
                }

                // Draw posts from bottom to top so the newest post sits directly above the thread bar.
                var bottomY = client.Bottom - PaddingY;
                var startIndex = Math.Clamp(_posts.Count - 1 - _scrollOffsetFromBottom, 0, _posts.Count - 1);

                for (var index = startIndex; index >= 0 && bottomY > PaddingY; index--)
                {
                    var post = _posts[index];
                    var header = post.FormatHeader();
                    var body = string.IsNullOrWhiteSpace(post.Body) ? " " : post.Body;

                    var headerMeasure = new WindowRectangle { Left = 0, Top = 0, Right = contentWidth, Bottom = 0 };
                    var headerHeight = Math.Max(14, DrawTextW(hdc, header, header.Length, ref headerMeasure, DrawTextCalcWrap));

                    var bodyMeasure = new WindowRectangle { Left = 0, Top = 0, Right = contentWidth, Bottom = 0 };
                    var bodyHeight = Math.Max(14, DrawTextW(hdc, body, body.Length, ref bodyMeasure, DrawTextCalcWrap));

                    var blockHeight = headerHeight + HeaderGap + bodyHeight + PostGap;
                    var blockTop = bottomY - blockHeight;
                    if (blockTop < PaddingY && index < startIndex)
                    {
                        break;
                    }

                    var clampedTop = Math.Max(PaddingY, blockTop);
                    var headerRect = new WindowRectangle
                    {
                        Left = PaddingX,
                        Top = clampedTop,
                        Right = client.Right - PaddingX,
                        Bottom = Math.Min(bottomY, clampedTop + headerHeight),
                    };
                    _ = SetTextColor(hdc, ColorHeaderName);
                    _ = DrawTextW(hdc, header, header.Length, ref headerRect, DrawTextSingleLineEllipsis);

                    var bodyTop = clampedTop + headerHeight + HeaderGap;
                    if (bodyTop < bottomY)
                    {
                        var bodyRect = new WindowRectangle
                        {
                            Left = PaddingX + 6,
                            Top = bodyTop,
                            Right = client.Right - PaddingX,
                            Bottom = bottomY - PostGap,
                        };
                        _ = SetTextColor(hdc, ColorBody);
                        _ = DrawTextW(hdc, body, body.Length, ref bodyRect, DrawTextWrap);
                    }

                    if (_separatorBrush != 0 && clampedTop > PaddingY + 2)
                    {
                        var sepRect = new WindowRectangle
                        {
                            Left = PaddingX,
                            Top = clampedTop - 2,
                            Right = client.Right - PaddingX,
                            Bottom = clampedTop - 1,
                        };
                        _ = FillRect(hdc, ref sepRect, _separatorBrush);
                    }

                    bottomY = clampedTop - 4;
                }
            }
            finally
            {
                if (previousFont != 0)
                {
                    _ = SelectObject(hdc, previousFont);
                }
            }
        }
        finally
        {
            _ = EndPaint(window, ref paint);
        }
    }

    private static void DeleteBrush(ref nint brush)
    {
        if (brush != 0)
        {
            _ = DeleteObject(brush);
            brush = 0;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern int ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out WindowRectangle rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rect);

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

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

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
    private static extern int SetTextColor(nint hdc, int color);
}

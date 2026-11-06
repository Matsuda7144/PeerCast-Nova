using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

// Shared by native controls and owner-draw; it never changes Windows font settings.
internal interface IGenesisUiFontNativeApi
{
    bool TryGetMessageFont(uint dpi, out GenesisUiLogFont font);
    nint CreateFont(GenesisUiLogFont font);
    bool HasJapaneseGlyphs(nint font);
    void DeleteFont(nint font);
    nint SelectFont(nint dc, nint font);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct GenesisUiLogFont
{
    public int Height, Width, Escapement, Orientation, Weight;
    public byte Italic, Underline, StrikeOut, CharacterSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string FaceName;
}

internal sealed class GenesisUiFontLease : IDisposable
{
    private readonly IGenesisUiFontNativeApi _api;
    private nint _handle;
    public nint Handle => _handle;

    private GenesisUiFontLease(IGenesisUiFontNativeApi api, nint handle)
    {
        _api = api;
        _handle = handle;
    }

    internal static GenesisUiFontLease? TryCreate(IGenesisUiFontNativeApi api, uint dpi, int minimumLogicalHeight = 0)
    {
        var hasSystemFont = api.TryGetMessageFont(dpi, out var font);
        if (!hasSystemFont)
        {
            // Only used if the system query fails. Keep a UI-sized, DPI-scaled fallback.
            font = new GenesisUiLogFont { Height = -(int)Math.Max(1, (12 * dpi + 48) / 96), Weight = 400 };
        }

        var minimumHeight = GenesisMainWindowLayout.Scale(Math.Max(0, minimumLogicalHeight), dpi);
        if (Math.Abs(font.Height) < minimumHeight) font.Height = -minimumHeight;
        if (hasSystemFont && TryCandidate(api, font) is { } system)
        {
            return system;
        }

        // Preserve the system's size/style; verify the mapped font, not just its family name.
        foreach (var family in new[] { "Yu Gothic UI", "Meiryo", "MS UI Gothic" })
        {
            font.FaceName = family;
            font.CharacterSet = 1; // DEFAULT_CHARSET: allow the font mapper to choose its charset.
            if (TryCandidate(api, font) is { } fallback)
            {
                return fallback;
            }
        }

        // No usable owned font. Leave the control's existing font in place rather than
        // applying a null/deleted handle or pretending that Japanese coverage was verified.
        return null;
    }

    private static GenesisUiFontLease? TryCandidate(IGenesisUiFontNativeApi api, GenesisUiLogFont font)
    {
        var handle = api.CreateFont(font);
        if (handle == 0)
        {
            return null;
        }

        var accepted = false;
        try
        {
            accepted = api.HasJapaneseGlyphs(handle);
            return accepted ? new GenesisUiFontLease(api, handle) : null;
        }
        finally
        {
            if (!accepted)
            {
                api.DeleteFont(handle);
            }
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
        {
            _api.DeleteFont(handle);
        }
    }
}

internal readonly struct GenesisUiFontSelection : IDisposable
{
    private readonly IGenesisUiFontNativeApi _api;
    private readonly nint _dc;
    private readonly nint _previous;

    internal GenesisUiFontSelection(IGenesisUiFontNativeApi api, nint dc, nint font)
    {
        _api = api;
        _dc = dc;
        _previous = dc == 0 || font == 0 ? 0 : api.SelectFont(dc, font);
    }

    public void Dispose()
    {
        if (_previous != 0 && _previous != -1)
        {
            _ = _api.SelectFont(_dc, _previous);
        }
    }
}

internal static class GenesisWindowUiFont
{
    private const uint SetFontMessage = 0x0030;
    private const uint GetFontMessage = 0x0031;
    private const uint NonClientDestroyMessage = 0x0082;
    private const uint DpiChangedMessage = 0x02E0;
    private const nuint SubclassId = 0x4E464F4E; // NFON; distinct from existing GUI subclasses.
    private static readonly Dictionary<nint, GenesisUiWindowFont> Fonts = new();
    private static readonly SubclassProcedure Procedure = ProcessMessage;
    private static readonly SystemUiFontNativeApi Api = new();

    // Fonts live until the owning root HWND has finished destruction. This is longer
    // than an individual bridge, whose controls may close asynchronously via WM_CLOSE.
    internal static bool Apply(nint control)
    {
        if (control == 0)
        {
            return false;
        }

        var root = GetAncestor(control, 2); // GA_ROOT; owned settings windows have their own root.
        if (root == 0)
        {
            return false;
        }

        lock (Fonts)
        {
            if (!Fonts.TryGetValue(root, out var window))
            {
                var font = GenesisUiWindowFont.TryCreate(Api, GetWindowDpi(root));
                if (font is null)
                {
                    return false;
                }

                if (!SetWindowSubclass(root, Procedure, SubclassId, 0))
                {
                    font.Dispose();
                    return false;
                }

                window = font;
                Fonts.Add(root, window);
            }

            return window.Apply(control, ApplyFont);
        }
    }

    internal static GenesisUiFontSelection SelectForDrawing(nint dc, nint control) =>
        new(Api, dc, control == 0 ? 0 : SendMessageW(control, GetFontMessage, 0, 0));

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == NonClientDestroyMessage)
        {
            _ = RemoveWindowSubclass(window, Procedure, id);
            try
            {
                return DefSubclassProc(window, message, wParam, lParam);
            }
            finally
            {
                lock (Fonts)
                {
                    if (Fonts.Remove(window, out var font))
                    {
                        font.Dispose();
                    }
                }
            }
        }

        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == DpiChangedMessage)
        {
            lock (Fonts)
            {
                if (Fonts.TryGetValue(window, out var current))
                {
                    _ = current.Refresh(
                        GetWindowDpi(window),
                        ApplyFont,
                        control => IsWindow(control) && GetAncestor(control, 2) == window);
                }
            }
        }

        return result;
    }

    private static uint GetWindowDpi(nint window)
    {
        try
        {
            var dpi = GetDpiForWindow(window);
            if (dpi != 0)
            {
                return dpi;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions do not export GetDpiForWindow.
        }

        return SystemUiFontNativeApi.GetSystemDpi();
    }

    private static void ApplyFont(nint control, nint font) =>
        _ = SendMessageW(control, SetFontMessage, (nuint)font, 1);
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}

internal sealed class SystemUiFontNativeApi : IGenesisUiFontNativeApi
{
    internal static int MetricsSize => Marshal.SizeOf<NonClientMetrics>();

    internal const string JapaneseSample = "ABCチャンネル一覧配信開始終了中断設定ログについて検索新着お気入り視聴録画更新警告情報種類日時内容情報をコピー保存参照録画保存先絶対パス実行ファイル既定に戻す未設定閉じる空欄では既存のを使います。保存した設定は次のPlayから反映されます。設定を保存できませんでした。保存場所を確認してください。絶対パスを入力してください。ファイル名はにしてください。指定した実行ファイルが見つかりません。フォルダーではなく実行ファイルを指定してください。実行ファイルを確認できません。アクセス権を確認してください。パスを確認してください。PCRPlayer設定は未設定です。次の再生では環境変数またはPATHを使います。既に保存されています。未設定に戻しました。PCRPlayer設定を保存しました。次の再生から使用します。新規編集有効化削除上へ下へ一部の保存先またはファイルを確認できません。詳細録画停止外規起効今細削止自手集除象上停動別編面有抑カけこタブへムやよラリ、回同選択ポト番号～整合計レ毎（制限無）値読み込待ち受・帯域御適後続応めと置換え処理接う最大半角状態不わ—ず世代作備切利取失完敗替流消済準·エ分由専操要求キ正常界メディ候全前可安成明果結能補許証識：あ付件功性拠期条立等＝→もサバ初在度機現異示表隠／ジ概者ビッワ［固］ソ形式申際送速元　掲載…白文字ユザ含列改以本端強公競放ドモ任働到意稼達観シ追加ノ体測↑↓解マ割当出";
    private const uint SpiGetNonClientMetrics = 0x0029;
    private const int LogPixelsX = 88;

    public bool TryGetMessageFont(uint dpi, out GenesisUiLogFont font)
    {
        var metrics = new NonClientMetrics { Size = (uint)Marshal.SizeOf<NonClientMetrics>() };
        if (TryGetMessageMetricsForDpi(ref metrics, dpi))
        {
            font = metrics.MessageFont;
            return true;
        }

        // SystemParametersInfoForDpi was added in Windows 10 version 1607.
        // Use the older system API when it is unavailable, then scale the LOGFONT
        // from system DPI to the owning window's DPI.
        metrics = new NonClientMetrics { Size = (uint)Marshal.SizeOf<NonClientMetrics>() };
        var success = SystemParametersInfoW(SpiGetNonClientMetrics, metrics.Size, ref metrics, 0);
        if (!success)
        {
            // Before Windows Vista, NONCLIENTMETRICS did not include the final
            // padded-border field. The preceding layout is otherwise identical.
            metrics = new NonClientMetrics { Size = (uint)(Marshal.SizeOf<NonClientMetrics>() - sizeof(int)) };
            success = SystemParametersInfoW(SpiGetNonClientMetrics, metrics.Size, ref metrics, 0);
        }

        font = metrics.MessageFont;
        if (success)
        {
            font = ScaleFontForDpi(font, dpi, GetSystemDpi());
        }

        return success;
    }

    internal static uint GetSystemDpi()
    {
        var dc = GetDC(0);
        if (dc == 0)
        {
            return 96;
        }

        try
        {
            var dpi = GetDeviceCaps(dc, LogPixelsX);
            return dpi > 0 ? (uint)dpi : 96;
        }
        finally
        {
            _ = ReleaseDC(0, dc);
        }
    }

    private static bool TryGetMessageMetricsForDpi(ref NonClientMetrics metrics, uint dpi)
    {
        try
        {
            return SystemParametersInfoForDpi(SpiGetNonClientMetrics, metrics.Size, ref metrics, 0, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    internal static GenesisUiLogFont ScaleFontForDpi(GenesisUiLogFont font, uint targetDpi, uint systemDpi)
    {
        if (targetDpi == 0 || systemDpi == 0 || targetDpi == systemDpi)
        {
            return font;
        }

        font.Height = ScaleMetric(font.Height, targetDpi, systemDpi);
        font.Width = ScaleMetric(font.Width, targetDpi, systemDpi);
        return font;
    }

    private static int ScaleMetric(int value, uint targetDpi, uint sourceDpi)
    {
        if (value == 0)
        {
            return 0;
        }

        var scaled = Math.Round((double)value * targetDpi / sourceDpi, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
    }

    public nint CreateFont(GenesisUiLogFont font) => CreateFontIndirectW(ref font);
    public void DeleteFont(nint font) => _ = DeleteObject(font);
    public nint SelectFont(nint dc, nint font) => SelectObject(dc, font);

    public bool HasJapaneseGlyphs(nint font)
    {
        var dc = CreateCompatibleDC(0);
        if (dc == 0)
        {
            return false;
        }

        try
        {
            var previous = SelectObject(dc, font);
            if (previous == 0 || previous == -1)
            {
                return false;
            }

            try
            {
                var indices = new ushort[JapaneseSample.Length];
                var count = GetGlyphIndicesW(dc, JapaneseSample, JapaneseSample.Length, indices, 1);
                return count == (uint)indices.Length && indices.All(static glyph => glyph != ushort.MaxValue);
            }
            finally
            {
                _ = SelectObject(dc, previous);
            }
        }
        finally
        {
            _ = DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NonClientMetrics
    {
        public uint Size;
        public int BorderWidth, ScrollWidth, ScrollHeight, CaptionWidth, CaptionHeight;
        public GenesisUiLogFont CaptionFont;
        public int SmallCaptionWidth, SmallCaptionHeight;
        public GenesisUiLogFont SmallCaptionFont;
        public int MenuWidth, MenuHeight;
        public GenesisUiLogFont MenuFont, StatusFont, MessageFont;
        public int PaddedBorderWidth;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoForDpi(uint action, uint parameter, ref NonClientMetrics metrics, uint flags, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref NonClientMetrics metrics, uint flags);
    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint dc, int index);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontIndirectW(ref GenesisUiLogFont font);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetGlyphIndicesW(nint dc, string text, int count, [Out] ushort[] indices, uint flags);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}

// Managed lifetime/rotation policy, independent of HWND creation. Native teardown
// calls Dispose only after the root WM_NCDESTROY has run through DefSubclassProc.
internal sealed class GenesisUiWindowFont : IDisposable
{
    private readonly IGenesisUiFontNativeApi _api;
    private readonly HashSet<nint> _controls = new();
    private GenesisUiFontLease _font;
    private bool _disposed;

    private GenesisUiWindowFont(IGenesisUiFontNativeApi api, GenesisUiFontLease font)
    {
        _api = api;
        _font = font;
    }

    internal static GenesisUiWindowFont? TryCreate(IGenesisUiFontNativeApi api, uint dpi) =>
        GenesisUiFontLease.TryCreate(api, dpi) is { } font ? new(api, font) : null;

    internal bool Apply(nint control, Action<nint, nint> apply)
    {
        if (_disposed || control == 0)
        {
            return false;
        }

        _controls.Add(control);
        apply(control, _font.Handle);
        return true;
    }

    internal bool Refresh(uint dpi, Action<nint, nint> apply, Func<nint, bool> belongsToWindow)
    {
        if (_disposed || GenesisUiFontLease.TryCreate(_api, dpi) is not { } replacement)
        {
            return false;
        }

        var previous = _font;
        _font = replacement;
        foreach (var control in _controls.ToArray())
        {
            if (_disposed)
            {
                break;
            }

            // A destroyed HWND may have been reused by a different root window.
            if (belongsToWindow(control))
            {
                apply(control, replacement.Handle);
            }
            else
            {
                _controls.Remove(control);
            }
        }

        previous.Dispose();
        return true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _controls.Clear();
            _font.Dispose();
        }
    }
}

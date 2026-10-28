using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

/// <summary>Owned Win32 editor. Async saving never calls native controls from its worker.</summary>
internal sealed class GenesisGuiNextPublishingSettingsWindow : IGenesisPublishingSettingsWindow
{
    private const string ClassName = "PeerCastNovaPublishingSettingsV1";
    private const uint Close = 0x0010, NcDestroy = 0x0082, Command = 0x0111, Saved = 0x8001, CloseRequested = 0x8002;
    private const int SaveId = 1020, CancelId = 1021, StatusId = 1022, NoticeId = 1023, RevisionId = 1024;
    private const uint Background = 0x00412917, EditBackground = 0x00503927, TextColor = 0x00F7F4F2;
    internal static readonly string[] FieldLabels = ["チャンネル名", "ジャンル", "概要", "配信者コメント", "コンタクトURL", "ビットレート (kbps)"];
    private static readonly ConcurrentDictionary<nint, WindowState> States = new();
    private static readonly WindowProcedure Procedure = ProcessMessage;
    private static readonly SubclassProcedure ControlProcedure = ProcessControl;
    private static readonly Lazy<bool> Registered = new(Register);
    private static long _nextToken;
    private readonly nint _window;
    private readonly WindowState _state;
    private GenesisGuiNextPublishingSettingsWindow(nint window, WindowState state) { _window = window; _state = state; }
    public bool IsOpen => !_state.Closed && States.TryGetValue(_window, out var state) && ReferenceEquals(state, _state) && IsWindow(_window);

    internal static IGenesisPublishingSettingsWindow? TryCreate(nint owner, PublishingSettingsController settings, Func<bool> isPublishing)
    {
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner) ||
            GetWindowThreadProcessId(owner, out _) != GetCurrentThreadId() || !Registered.Value) return null;
        var dpi = GetDpiForWindow(owner);
        if (dpi == 0) dpi = 96;
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(owner, 2), ref monitor)) return null;
        var work = monitor.Work;
        var width = Math.Min(Scale(700, dpi), work.Right - work.Left - 24);
        var height = Math.Min(Scale(820, dpi), work.Bottom - work.Top - 24);
        var window = CreateWindowExW(0, ClassName, "配信設定", 0x02EF0000,
            work.Left + 12, work.Top + 12, width, height, owner, 0, GetModuleHandleW(null), 0);
        if (window == 0) return null;
        WindowState? state = null;
        try
        {
            state = new(window, owner, settings, isPublishing, dpi);
            if (!States.TryAdd(window, state) || !state.CreateControls()) throw new InvalidOperationException("Cannot create publishing settings controls.");
            state.Initialize();
            state.ApplyLayout();
            _ = ShowWindow(window, 1);
            _ = SetForegroundWindow(window);
            _ = SetFocus(GetDlgItem(window, 1005));
            return new GenesisGuiNextPublishingSettingsWindow(window, state);
        }
        catch
        {
            _ = DestroyWindow(window);
            state?.MarkClosed();
            return null;
        }
    }

    private static bool Register()
    {
        // One process-lifetime class, with no class-owned GDI resources.
        var wc = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Instance = GetModuleHandleW(null),
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Cursor = LoadCursorW(0, (nint)32512),
            ClassName = ClassName,
        };
        return RegisterClassExW(ref wc) != 0;
    }
    internal static int Scale(int value, uint dpi) => GenesisGuiNextConnectionSettingsWindow.Scale(value, dpi);
    public void Activate() { if (IsOpen) { _ = ShowWindow(_window, 1); _ = SetForegroundWindow(_window); } }
    public void Dispose()
    {
        if (!IsOpen) return;
        if (_state.Thread == GetCurrentThreadId()) _state.CloseNative(restoreFocus: false);
        else _ = PostMessageW(_window, CloseRequested, _state.Token, 0);
    }

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == NcDestroy && States.TryRemove(window, out var removed)) removed.MarkClosed();
        if (!States.TryGetValue(window, out var state)) return DefWindowProcW(window, message, wParam, lParam);
        try
        {
            switch (message)
            {
                case Close: state.CloseNative(restoreFocus: true); return 0;
                case CloseRequested: if (wParam == state.Token) state.CloseNative(restoreFocus: false); return 0;
                case Command: state.Command((int)(wParam & 0xffff), (int)(wParam >> 16)); return 0;
                case Saved: state.CompleteSave(wParam); return 0;
                case 0x0014: _ = GetClientRect(window, out var area); _ = FillRect((nint)wParam, ref area, state.Brush); return 1;
                case 0x0005: state.ApplyLayout(); return 0;
                case 0x0115: state.Scroll((int)(wParam & 0xffff)); return 0;
                case 0x020A: state.ScrollBy(-((short)(wParam >> 16) * Scale(48, state.Dpi) / 120)); return 0;
                case 0x0133: return state.Color((nint)wParam, lParam, edit: true);
                case 0x0138: return state.Color((nint)wParam, lParam, edit: false);
                case 0x002B: WindowState.DrawButton(lParam); return 1;
                case 0x02E0:
                    state.Dpi = (uint)(wParam & 0xffff);
                    var bounds = Marshal.PtrToStructure<Rectangle>(lParam);
                    _ = SetWindowPos(window, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0014);
                    state.ApplyLayout();
                    return 0;
                case 0x0024:
                    var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    limits.MinWidth = Scale(460, state.Dpi);
                    limits.MinHeight = Scale(280, state.Dpi);
                    Marshal.StructureToPtr(limits, lParam, false);
                    return 0;
            }
        }
        catch { state.ShowFailure(); } // Native callbacks must not leak managed exceptions.
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static nint ProcessControl(nint control, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == NcDestroy) _ = RemoveWindowSubclass(control, ControlProcedure, id);
        if (!States.TryGetValue(unchecked((nint)data), out var state) || state.Closed) return DefSubclassProc(control, message, wParam, lParam);
        if (message == 0x0007) state.EnsureVisible(control);
        if (message == 0x020A) { _ = PostMessageW(state.Window, message, wParam, lParam); return 0; }
        var action = GenesisGuiNextConnectionSettingsWindow.ResolveKey(message, wParam, GetKeyState(0x10) < 0, GetDlgCtrlID(control) is SaveId or CancelId);
        switch (action)
        {
            case GenesisConnectionSettingsKeyAction.Next:
            case GenesisConnectionSettingsKeyAction.Previous:
                var next = GetNextDlgTabItem(state.Window, control, action == GenesisConnectionSettingsKeyAction.Previous);
                if (next != 0) _ = SetFocus(next);
                return 0;
            case GenesisConnectionSettingsKeyAction.Close: _ = PostMessageW(state.Window, Close, 0, 0); return 0;
            case GenesisConnectionSettingsKeyAction.ActivateButton: _ = SendMessageW(control, 0x00F5, 0, 0); return 0;
        }
        return DefSubclassProc(control, message, wParam, lParam);
    }

    private sealed record ControlBounds(nint Handle, int X, int Y, int Width, int Height, bool Stretch);

    private sealed class WindowState(nint window, nint owner, PublishingSettingsController settings, Func<bool> isPublishing, uint dpi) : IDisposable
    {
        internal nint Window => window;
        internal uint Thread { get; } = GetCurrentThreadId();
        internal nuint Token { get; } = unchecked((nuint)Interlocked.Increment(ref _nextToken));
        internal uint Dpi { get; set; } = dpi;
        private readonly PublishingSettingsEditor _editor = new(settings);
        internal nint Brush { get; } = CreateSolidBrush(Background);
        private readonly nint _editBrush = CreateSolidBrush(EditBackground);
        private readonly List<ControlBounds> _controls = [];
        private readonly CancellationTokenSource _stop = new();
        private Task _save = Task.CompletedTask;
        private bool _ready, _saving;
        private volatile bool _saveFailed;
        private int _scroll, _page;
        private uint _statusColor = 0x0000A5FF;
        private int _closed;
        internal bool Closed => Volatile.Read(ref _closed) != 0;

        internal bool CreateControls()
        {
            bool Add(string kind, string text, int id, int x, int y, int width, int height, bool stretch = false, uint style = 0)
            {
                var control = CreateWindowExW(kind == "EDIT" ? 0x200u : 0, kind, text, 0x50000000 | style,
                    0, 0, 1, 1, window, (nint)id, GetModuleHandleW(null), 0);
                if (control == 0 || !GenesisWindowUiFont.Apply(control)) return false;
                if ((style & 0x10000) != 0 && !SetWindowSubclass(control, ControlProcedure, 0x50534554, unchecked((nuint)window))) return false;
                _controls.Add(new(control, x, y, width, height, stretch));
                if (kind == "EDIT") _ = SendMessageW(control, 0x00C5, 8192, 0); // bounded draft; validation enforces protocol limits
                return true;
            }
            if (Brush == 0 || _editBrush == 0 ||
                !Add("STATIC", "ストリーム設定", 1030, 20, 16, 640, 24, true) ||
                !Add("STATIC", "ネットワーク：このPCのみ（IPv4）［固定］\r\nソース：OBS（RTMP）［固定］\r\n配信形式：FLV［固定］", 0, 20, 50, 640, 66, true) ||
                !Add("STATIC", FieldLabels[5], 0, 20, 130, 160, 24) ||
                !Add("EDIT", "", 1005, 192, 126, 160, 28, style: 0x10080) ||
                !Add("STATIC", "配信情報に申告する値です。実際のOBS送信速度ではありません。\r\n空欄にすると申告しません。", 0, 20, 168, 640, 44, true) ||
                !Add("STATIC", "元チャンネル：未対応　　掲載YP：未対応", 0, 20, 226, 640, 24, true) ||
                !Add("STATIC", "チャンネル情報", 1031, 20, 264, 640, 24, true)) return false;
            for (var index = 0; index < 5; index++)
                if (!Add("STATIC", FieldLabels[index], 0, 20, 304 + index * 48, 136, 24) ||
                    !Add("EDIT", "", 1000 + index, 164, 300 + index * 48, 496, 28, true, 0x10080)) return false;
            return Add("STATIC", "", NoticeId, 20, 548, 640, 44, true) &&
                Add("STATIC", "", RevisionId, 20, 600, 640, 24, true) &&
                Add("BUTTON", "保存", SaveId, 20, 634, 120, 32, style: 0x1000B) &&
                Add("BUTTON", "キャンセル", CancelId, 160, 634, 120, 32, style: 0x1000B) &&
                Add("STATIC", "", StatusId, 20, 680, 640, 54, true);
        }
        internal void Initialize()
        {
            _ready = false;
            var input = _editor.Input;
            var values = new[] { input.ChannelName, input.Genre, input.Description, input.Comment, input.ContactUrl, input.BitrateKbps };
            for (var index = 0; index < values.Length; index++) SetText(1000 + index, values[index]);
            SetText(NoticeId, "保存した設定は次の配信開始から適用されます。" + (isPublishing() ? "\r\n現在の配信には反映されません。" : ""));
            SetText(RevisionId, $"保存済み Revision: {settings.Saved.SettingsRevision} / 適用済み: {settings.Applied?.SettingsRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "未開始"}");
            SetText(StatusId, _editor.Result.Message);
            _ready = true;
        }
        internal void Command(int id, int notification)
        {
            if (!_ready || Closed) return;
            if (notification == 0x0300 && id is >= 1000 and <= 1005 && !_saving)
            {
                _editor.Edit(new(GetText(1000), GetText(1001), GetText(1002), GetText(1003), GetText(1004), GetText(1005)));
                return;
            }
            if (notification != 0) return;
            if (id == CancelId) { CloseNative(restoreFocus: true); return; }
            if (id != SaveId || _saving) return;
            _saving = true;
            _saveFailed = false;
            EnableInputs(false);
            SetText(StatusId, "保存しています…");
            _save = SaveAsync();
        }
        private async Task SaveAsync()
        {
            try { await _editor.SaveAsync(_stop.Token).ConfigureAwait(false); }
            catch { _saveFailed = true; }
            if (!Closed && States.TryGetValue(window, out var active) && ReferenceEquals(active, this))
                _ = PostMessageW(window, Saved, Token, 0);
        }
        internal void CompleteSave(nuint token)
        {
            if (Closed || !_saving || token != Token) return;
            _saving = false;
            EnableInputs(true);
            if (_saveFailed)
            {
                _statusColor = 0x006078FF;
                SetText(StatusId, "保存の完了を確認できませんでした。保存済みの値を再確認してください。");
                return;
            }
            _statusColor = _editor.Result.Saved ? 0x008BC935u : 0x006078FFu;
            Initialize();
            if (_editor.Result.Field is { } field)
            {
                var fields = new[] { "ChannelName", "Genre", "Description", "Comment", "ContactUrl", "BitrateKbps" };
                var index = Array.IndexOf(fields, field);
                if (index >= 0)
                {
                    SetText(StatusId, FieldLabels[index] + "：" + _editor.Result.Message);
                    _ = SetFocus(GetDlgItem(window, 1000 + index));
                }
            }
        }
        private void EnableInputs(bool enabled)
        {
            for (var index = 0; index < 6; index++) _ = EnableWindow(GetDlgItem(window, 1000 + index), enabled);
            _ = EnableWindow(GetDlgItem(window, SaveId), enabled);
        }
        internal void ApplyLayout()
        {
            if (Closed || !GetClientRect(window, out var area)) return;
            _page = area.Bottom;
            _scroll = PublishingSettingsWindowLayout.ClampScroll(_scroll, _page, Dpi);
            var info = new ScrollInfo
            {
                Size = (uint)Marshal.SizeOf<ScrollInfo>(),
                Mask = 7,
                Maximum = Scale(PublishingSettingsWindowLayout.ContentHeight, Dpi) - 1,
                Page = (uint)Math.Max(1, _page),
                Position = _scroll
            };
            _ = SetScrollInfo(window, 1, ref info, true);
            foreach (var item in _controls)
                _ = MoveWindow(item.Handle, Scale(item.X, Dpi), Scale(item.Y, Dpi) - _scroll,
                    item.Stretch ? Math.Max(1, area.Right - Scale(item.X + 20, Dpi)) : Scale(item.Width, Dpi), Scale(item.Height, Dpi), true);
            _ = InvalidateRect(window, 0, true);
        }
        internal void ScrollBy(int change) { _scroll += change; ApplyLayout(); }
        internal void Scroll(int code)
        {
            var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x10 };
            _ = GetScrollInfo(window, 1, ref info);
            ScrollBy(code switch
            {
                0 => -Scale(24, Dpi),
                1 => Scale(24, Dpi),
                2 => -_page,
                3 => _page,
                4 or 5 => info.TrackPosition - _scroll,
                6 => -_scroll,
                7 => int.MaxValue - _scroll,
                _ => 0
            });
        }
        internal void EnsureVisible(nint control)
        {
            var item = _controls.FirstOrDefault(item => item.Handle == control);
            if (item is null) return;
            var top = Scale(item.Y, Dpi);
            var bottom = Scale(item.Y + item.Height + 8, Dpi);
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + _page) _scroll = bottom - _page;
            ApplyLayout();
        }
        internal nint Color(nint dc, nint control, bool edit)
        {
            var id = GetDlgCtrlID(control);
            _ = SetTextColor(dc, id == StatusId ? _statusColor : id is 1030 or 1031 ? 0x00FF8DADu : TextColor);
            _ = SetBkColor(dc, edit ? EditBackground : Background);
            return edit ? _editBrush : Brush;
        }
        internal static void DrawButton(nint parameter)
        {
            var item = Marshal.PtrToStructure<DrawItem>(parameter);
            if (item.ControlId is not (SaveId or CancelId)) return;
            var brush = CreateSolidBrush((item.State & 4) != 0 ? EditBackground : 0x00995D25);
            try { _ = FillRect(item.Dc, ref item.Bounds, brush); }
            finally { if (brush != 0) _ = DeleteObject(brush); }
            _ = SetBkMode(item.Dc, 1);
            _ = SetTextColor(item.Dc, TextColor);
            var previousFont = SelectObject(item.Dc, SendMessageW(item.Window, 0x0031, 0, 0));
            _ = DrawTextW(item.Dc, item.ControlId == SaveId ? "保存" : "キャンセル", -1, ref item.Bounds, 0x0825);
            if (previousFont != 0 && previousFont != -1) _ = SelectObject(item.Dc, previousFont);
            if ((item.State & 0x10) != 0) _ = DrawFocusRect(item.Dc, ref item.Bounds);
        }
        internal void ShowFailure() { if (!Closed) { _statusColor = 0x006078FF; SetText(StatusId, "操作を完了できませんでした。入力内容を確認してください。"); } }
        internal void CloseNative(bool restoreFocus)
        {
            if (Closed) return;
            var destroyed = DestroyWindow(window);
            if (destroyed && restoreFocus && IsWindow(owner)) { _ = SetForegroundWindow(owner); _ = SetFocus(owner); }
        }
        public void Dispose() => CloseNative(restoreFocus: false);
        internal void MarkClosed()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            _editor.Dispose();
            _ = DeleteObject(Brush);
            _ = DeleteObject(_editBrush);
            _ = ReleaseAsync();
        }
        private async Task ReleaseAsync()
        {
            try { await _stop.CancelAsync().ConfigureAwait(false); await _save.ConfigureAwait(false); }
            catch { }
            finally { _stop.Dispose(); }
        }
        private void SetText(int id, string text) => _ = SetWindowTextW(GetDlgItem(window, id), text);
        private string GetText(int id)
        {
            var control = GetDlgItem(window, id);
            var buffer = new char[GetWindowTextLengthW(control) + 1];
            return new string(buffer, 0, GetWindowTextW(control, buffer, buffer.Length));
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rectangle Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public int ReservedX, ReservedY, MaxWidth, MaxHeight, MaxX, MaxY, MinWidth, MinHeight, MaxTrackWidth, MaxTrackHeight; }
    [StructLayout(LayoutKind.Sequential)] private struct ScrollInfo { public uint Size, Mask; public int Minimum, Maximum; public uint Page; public int Position, TrackPosition; }
    [StructLayout(LayoutKind.Sequential)] private struct DrawItem { public uint Type, ControlId, ItemId, Action, State; public nint Window, Dc; public Rectangle Bounds; public nuint Data; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, Menu;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint extended, string className, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint control);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint window, int id);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint control);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetNextDlgTabItem(nint window, nint control, [MarshalAs(UnmanagedType.Bool)] bool previous);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rectangle rectangle);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(nint control, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(nint control);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint control, [Out] char[] text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowTextW(nint control, string text);
    [DllImport("user32.dll")] private static extern int SetScrollInfo(nint window, int bar, ref ScrollInfo info, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rectangle rectangle, nint brush);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(nint dc, string text, int length, ref Rectangle rectangle, uint format);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DrawFocusRect(nint dc, ref Rectangle rectangle);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}

internal static class PublishingSettingsWindowLayout
{
    internal const int ContentHeight = 748;
    internal static int ClampScroll(int requested, int page, uint dpi) =>
        Math.Clamp(requested, 0, Math.Max(0, GenesisGuiNextPublishingSettingsWindow.Scale(ContentHeight, dpi) - page));
}

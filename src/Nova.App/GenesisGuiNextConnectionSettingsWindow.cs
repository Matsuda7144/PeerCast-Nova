using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal enum GenesisConnectionSettingsKeyAction { None, Next, Previous, Close, ActivateButton }

/// <summary>Native next-start settings editor. Persistence runs off the GUI thread; completion is posted back.</summary>
internal sealed class GenesisGuiNextConnectionSettingsWindow : IGenesisGuiNextConnectionSettingsWindow
{
    internal const string Title = "接続 / 帯域 / リレー数";
    internal static readonly string[] FieldLabels =
        ["ポート番号", "合計(IPv4) kbps", "合計(IPv6) kbps（未対応）", "合計リレー数", "チャンネル毎(配信時)", "チャンネル毎(視聴時)"];
    private const uint Command = 0x0111, Close = 0x0010, NcDestroy = 0x0082, DpiChanged = 0x02E0, SaveCompleted = 0x8001, CloseRequested = 0x8002;
    private const int Ipv6Id = 1010, WanId = 1011, SaveId = 1020, CloseId = 1021, RuntimeId = 1022, StatusId = 1023, StartupId = 1024;
    private const uint ChildVisible = 0x50000000, TabStop = 0x00010000;
    private static readonly ConcurrentDictionary<nint, WindowState> States = new();
    private static readonly WindowProcedure Procedure = ProcessMessage;
    private static readonly SubclassProcedure ControlProcedure = ProcessControlMessage;
    private static int _nextCompletionToken;
    private readonly nint _window;
    private readonly WindowState _state;
    private int _disposed;

    private GenesisGuiNextConnectionSettingsWindow(nint window, WindowState state)
    { _window = window; _state = state; }

    public bool IsOpen => Volatile.Read(ref _disposed) == 0 && !_state.IsClosed &&
        States.TryGetValue(_window, out var active) && ReferenceEquals(active, _state) && IsWindow(_window);

    internal static GenesisGuiNextConnectionSettingsWindow? TryCreate(nint owner,
        GenesisGuiNextConnectionSettings settings, Action showRuntimeStatus)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showRuntimeStatus);
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner) ||
            GetWindowThreadProcessId(owner, out _) != GetCurrentThreadId()) return null;
        var instance = GetModuleHandleW(null);
        if (instance == 0) return null;
        var className = $"PeerCastNovaConnectionSettings-{Guid.NewGuid():N}";
        var wc = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)32512),
            Background = (nint)6,
            ClassName = className,
        };
        // Stock COLOR_WINDOW brush is borrowed; there is no caller-owned brush to delete.
        var classLifetime = new GenesisNetworkSettingsWindowClassLifetime(
            () => RegisterClassExW(ref wc) != 0, () => UnregisterClassW(className, instance), static () => true);
        if (!classLifetime.TryRegister())
        {
            if (!classLifetime.TryRelease()) throw new GenesisNetworkSettingsWindowCleanupException(classLifetime);
            return null;
        }
        var dpi = GetDpiForWindow(owner);
        if (dpi == 0) dpi = 96;
        var window = CreateWindowExW(0, className, Title, 0x02C80000,
            unchecked((int)0x80000000), unchecked((int)0x80000000), Scale(700, dpi), Scale(630, dpi), owner, 0, instance, 0);
        if (window == 0)
        {
            if (!classLifetime.TryRelease()) throw new GenesisNetworkSettingsWindowCleanupException(classLifetime);
            return null;
        }
        var state = new WindowState(window, settings, showRuntimeStatus, classLifetime);
        if (!States.TryAdd(window, state)) throw new GenesisNetworkSettingsWindowCleanupException(state);
        if (!GenesisNetworkSettingsWindowInitialization.TryInitialize(
            () => state.CreateControls(instance, dpi), state.Initialize, static () => true, state.AbortCreation)) return null;
        _ = ShowWindow(window, 1);
        _ = SetForegroundWindow(window);
        _ = SetFocus(GetDlgItem(window, 1000));
        return new(window, state);
    }

    internal static int Scale(int value, uint dpi) => checked((int)((value * (long)dpi + 48) / 96));

    internal static GenesisConnectionSettingsKeyAction ResolveKey(uint message, nuint key, bool shift, bool button) =>
        message != 0x0100 ? GenesisConnectionSettingsKeyAction.None : key switch
        {
            9 => shift ? GenesisConnectionSettingsKeyAction.Previous : GenesisConnectionSettingsKeyAction.Next,
            27 => GenesisConnectionSettingsKeyAction.Close,
            13 when button => GenesisConnectionSettingsKeyAction.ActivateButton,
            _ => GenesisConnectionSettingsKeyAction.None,
        };

    private static nint ProcessControlMessage(nint control, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var owner = unchecked((nint)data);
        if (message == NcDestroy) _ = RemoveWindowSubclass(control, ControlProcedure, id);
        if (!States.TryGetValue(owner, out var state) || state.IsClosed)
            return DefSubclassProc(control, message, wParam, lParam);
        var controlId = GetDlgCtrlID(control);
        switch (ResolveKey(message, wParam, GetKeyState(0x10) < 0, controlId is SaveId or CloseId or RuntimeId))
        {
            case GenesisConnectionSettingsKeyAction.Next:
            case GenesisConnectionSettingsKeyAction.Previous:
                var next = GetNextDlgTabItem(owner, control, GetKeyState(0x10) < 0);
                if (next != 0) _ = SetFocus(next);
                return 0;
            case GenesisConnectionSettingsKeyAction.Close:
                _ = PostMessageW(owner, Close, 0, 0);
                return 0;
            case GenesisConnectionSettingsKeyAction.ActivateButton:
                _ = SendMessageW(control, 0x00f5, 0, 0);
                return 0;
        }
        return DefSubclassProc(control, message, wParam, lParam);
    }

    public void Activate()
    { if (IsOpen) { _ = ShowWindow(_window, 1); _ = SetForegroundWindow(_window); } }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return _state.DisposeAsync();
    }

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == NcDestroy && States.TryRemove(window, out var removed)) removed.MarkClosed();
        if (States.TryGetValue(window, out var state))
        {
            if (message == Close || (message == CloseRequested && state.AcceptsToken(wParam)))
            { state.CloseNative(); return 0; }
            if (message == Command && (wParam >> 16) == 0) state.ProcessCommand((int)(wParam & 0xffff));
            if (message == SaveCompleted) state.CompleteSave(wParam);
            if (message == DpiChanged)
            {
                var rectangle = Marshal.PtrToStructure<NativeRectangle>(lParam);
                _ = SetWindowPos(window, 0, rectangle.Left, rectangle.Top,
                    rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top, 0x0014);
                state.ApplyLayout((uint)(wParam & 0xffff));
            }
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed record ControlLayout(nint Control, int X, int Y, int Width, int Height);

    private sealed class WindowState(nint window, GenesisGuiNextConnectionSettings settings, Action showRuntimeStatus,
        GenesisNetworkSettingsWindowClassLifetime classLifetime) : IAsyncDisposable
    {
        private readonly GenesisGuiNextConnectionEditor _editor = new(settings);
        private readonly List<ControlLayout> _layout = [];
        private readonly nuint _completionToken = unchecked((nuint)Interlocked.Increment(ref _nextCompletionToken));
        private volatile bool _closed;
        private readonly uint _ownerThread = GetCurrentThreadId();
        private readonly object _disposalGate = new();
        private readonly TaskCompletionSource _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _disposal;
        private bool _closingNative;
        private bool _saving;
        private string _completedStatus = string.Empty;
        internal bool IsClosed => _closed;
        internal bool AcceptsToken(nuint token) => token == _completionToken;

        internal bool CreateControls(nint instance, uint dpi)
        {
            bool Add(string kind, string text, int id, int x, int y, int width, int height, uint extra = 0)
            {
                var control = CreateWindowExW(kind == "EDIT" ? 0x00000200u : 0, kind, text,
                    ChildVisible | extra, Scale(x, dpi), Scale(y, dpi), Scale(width, dpi), Scale(height, dpi),
                    window, (nint)id, instance, 0);
                if (control == 0 || !GenesisWindowUiFont.Apply(control)) return false;
                if ((extra & TabStop) != 0 && !SetWindowSubclass(control, ControlProcedure, 0x4e434f4e, unchecked((nuint)window))) return false;
                _layout.Add(new(control, x, y, width, height));
                return true;
            }

            if (!Add("STATIC", GenesisGuiNextConnectionSettings.NextStartNotice, 0, 16, 12, 652, 42) ||
                !Add("STATIC", "接続待ち受け", 0, 16, 66, 652, 22) ||
                !Add("STATIC", FieldLabels[0], 0, 28, 96, 214, 22) ||
                !Add("EDIT", string.Empty, 1000, 250, 92, 140, 26, TabStop | 0x0080) ||
                !Add("BUTTON", "IPv6を使う（未対応）", Ipv6Id, 420, 92, 180, 28, TabStop | 3) ||
                !Add("BUTTON", "WAN Relayを公開", WanId, 420, 168, 210, 28, TabStop | 3) ||
                !Add("STATIC", "最大上り帯域", 0, 16, 138, 652, 22)) return false;
            for (var index = 1; index < FieldLabels.Length; index++)
            {
                var y = index < 3 ? 168 + (index - 1) * 34 : 280 + (index - 3) * 34;
                if (!Add("STATIC", FieldLabels[index], 0, 28, y + 4, 214, 24) ||
                    !Add("EDIT", string.Empty, 1000 + index, 250, y, 140, 26, TabStop | 0x0080)) return false;
            }
            return Add("STATIC", "最大リレー数", 0, 16, 246, 652, 22) &&
                Add("STATIC", "帯域とリレー数: 空欄は制限無し、0は上限0です。\r\n半角整数で入力してください（ポート1～65535、上限値0～2147483647）。", 0, 16, 388, 652, 42) &&
                Add("STATIC", string.Empty, StartupId, 16, 440, 652, 38) &&
                Add("BUTTON", "保存", SaveId, 16, 488, 100, 32, TabStop) &&
                Add("BUTTON", "実行状態...", RuntimeId, 130, 488, 140, 32, TabStop) &&
                Add("BUTTON", "閉じる", CloseId, 568, 488, 100, 32, TabStop) &&
                Add("STATIC", string.Empty, StatusId, 16, 534, 652, 48);
        }

        internal void Initialize()
        {
            var input = _editor.Input;
            var texts = new[] { input.ListenPort, input.TotalUpstreamIpv4Kbps, input.TotalUpstreamIpv6Kbps,
                input.MaxRelaysTotal, input.MaxRelaysPerBroadcastChannel, input.MaxRelaysPerViewingChannel };
            for (var i = 0; i < texts.Length; i++) SetText(1000 + i, texts[i]);
            _ = SendMessageW(GetDlgItem(window, Ipv6Id), 0x00f1, input.UseIpv6 ? 1u : 0u, 0);
            _ = SendMessageW(GetDlgItem(window, WanId), 0x00f1, input.WanRelayEnabled ? 1u : 0u, 0);
            SetText(StatusId, _editor.Status);
            RefreshStartupNotice();
            _ = EnableWindow(GetDlgItem(window, Ipv6Id), false);
            _ = EnableWindow(GetDlgItem(window, 1002), false);
        }

        private void RefreshStartupNotice() => SetText(StartupId,
            $"今回の起動時読込値: ポート {settings.Startup.ListenPort} / IPv6 {(settings.Startup.UseIpv6 ? "使用" : "不使用")}\r\n" +
            (settings.HasPendingRestart ? "保存値が変わりました。再起動後に読み込みます。" : "今回の起動時設定と保存値は同じです。"));

        internal void ProcessCommand(int id)
        {
            if (_closed) return;
            if (id == CloseId) { _ = PostMessageW(window, Close, 0, 0); return; }
            if (id == RuntimeId) { showRuntimeStatus(); return; }
            if (id != SaveId || _saving) return;
            _editor.Input = new(GetText(1000), SendMessageW(GetDlgItem(window, Ipv6Id), 0x00f0, 0, 0) == 1,
                GetText(1001), GetText(1002), GetText(1003), GetText(1004), GetText(1005),
                SendMessageW(GetDlgItem(window, WanId), 0x00f0, 0, 0) == 1);
            _saving = true;
            EnableInputs(false);
            SetText(StatusId, "保存しています...");
            _ = SaveAsync();
        }

        private async Task SaveAsync()
        {
            try
            {
                await Task.Run(async () => await _editor.SaveAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Volatile.Write(ref _completedStatus, _editor.Status);
            }
            catch
            {
                Volatile.Write(ref _completedStatus, "設定を保存できませんでした。設定は変更されていません。");
            }
            if (!_closed && States.TryGetValue(window, out var active) && ReferenceEquals(active, this))
                _ = PostMessageW(window, SaveCompleted, _completionToken, 0);
        }

        internal void CompleteSave(nuint token)
        {
            if (_closed || !_saving || token != _completionToken) return;
            _saving = false;
            EnableInputs(true);
            SetText(StatusId, Volatile.Read(ref _completedStatus));
            RefreshStartupNotice();
        }

        private void EnableInputs(bool enabled)
        {
            for (var i = 0; i < FieldLabels.Length; i++) _ = EnableWindow(GetDlgItem(window, 1000 + i), i != 2 && enabled);
            _ = EnableWindow(GetDlgItem(window, Ipv6Id), false);
            _ = EnableWindow(GetDlgItem(window, WanId), enabled);
            _ = EnableWindow(GetDlgItem(window, SaveId), enabled);
        }

        internal void ApplyLayout(uint dpi)
        {
            foreach (var item in _layout)
                _ = MoveWindow(item.Control, Scale(item.X, dpi), Scale(item.Y, dpi), Scale(item.Width, dpi), Scale(item.Height, dpi), true);
        }
        internal void MarkClosed()
        {
            _closed = true;
            if (!_closingNative) _destroyed.TrySetResult();
        }

        internal bool CloseNative()
        {
            _closed = true;
            _closingNative = true;
            try { return DestroyWindow(window); }
            finally
            {
                _closingNative = false;
                if (!IsWindow(window)) _destroyed.TrySetResult();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposalGate)
            {
                _closed = true;
                return new(_disposal ??= DisposeCoreAsync());
            }
        }

        private async Task DisposeCoreAsync()
        {
            States.TryGetValue(window, out var active);
            if (GenesisNetworkSettingsWindowInitialization.ShouldCloseNative(
                _destroyed.Task.IsCompleted, active, this, () => IsWindow(window)))
            {
                var requested = _ownerThread == GetCurrentThreadId() ? CloseNative() :
                    PostMessageW(window, CloseRequested, _completionToken, 0);
                if (!requested) throw new GenesisNetworkSettingsWindowCleanupException(this);
            }
            else _destroyed.TrySetResult();
            await _destroyed.Task.ConfigureAwait(false);
            if (!classLifetime.TryRelease()) throw new GenesisNetworkSettingsWindowCleanupException(this);
        }

        internal void AbortCreation()
        {
            var cleanup = DisposeAsync();
            if (!cleanup.IsCompleted) throw new GenesisNetworkSettingsWindowCleanupException(this);
            cleanup.GetAwaiter().GetResult();
        }
        private void SetText(int id, string text) => _ = SetWindowTextW(GetDlgItem(window, id), text);
        private string GetText(int id)
        {
            var control = GetDlgItem(window, id);
            var buffer = new char[GetWindowTextLengthW(control) + 1];
            var length = GetWindowTextW(control, buffer, buffer.Length);
            return new string(buffer, 0, length);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, Menu;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint control);
    [DllImport("user32.dll")] private static extern nint GetNextDlgTabItem(nint window, nint control, [MarshalAs(UnmanagedType.Bool)] bool previous);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WindowClassEx wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClassW(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint extended, string className, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint control);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint window, int id);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(nint control, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(nint control);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint control, [Out] char[] text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowTextW(nint control, string text);
}

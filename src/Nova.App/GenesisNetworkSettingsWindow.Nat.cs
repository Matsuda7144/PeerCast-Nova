using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisNetworkSettingsWindow
{
    internal const string NatCheckboxLabel = "ポート自動開放（UPnP / NAT-PMP）";
    internal const int NatRefreshIntervalMilliseconds = 1_000;
    private const int NatCheckboxId = 2201;
    private const uint NatCompletedMessage = 0x8000 + 0x73B;
    private const uint NatCloseMessage = 0x8000 + 0x73C;
    private const uint NatTimerMessage = 0x0113;
    private const uint NatTimerId = 0x73B;
    private const uint NatBackground = 0x00412917;
    private const uint NatText = 0x00F7F4F2;
    private static readonly ConcurrentDictionary<nint, NatWindowState> NatStates = new();
    private static readonly SubclassProcedure NatKeyboardProcedure = ProcessNatKeyboard;
    private NatWindowState? _natState;

    public static GenesisNetworkSettingsWindow? TryCreate(nint owner, IGuiNextNetworkSettingsAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner) ||
            GetWindowThreadProcessId(owner, out _) != GetCurrentThreadId()) return null;
        var instance = GetModuleHandleW(null);
        if (instance == 0) return null;
        var brush = CreateSolidBrush(NatBackground);
        if (brush == 0) return null;
        var className = $"PeerCastNovaNetworkNat-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)ArrowCursor),
            BackgroundBrush = brush,
            ClassName = className,
        };
        var classLifetime = new GenesisNetworkSettingsWindowClassLifetime(
            () => RegisterClassExW(ref windowClass) != 0,
            () => UnregisterClassW(className, instance),
            () => DeleteObject(brush));
        if (!classLifetime.TryRegister())
        {
            if (!classLifetime.TryRelease())
                throw new GenesisNetworkSettingsWindowCleanupException(classLifetime);
            return null;
        }
        var window = CreateWindowExW(0x00010000, className, "通信 / ポート自動開放",
            WindowStyleCaption | WindowStyleSysMenu | WindowStyleClipChildren,
            UseDefaultPosition, UseDefaultPosition, 820, 760, owner, 0, instance, 0);
        if (window == 0)
        {
            if (!classLifetime.TryRelease())
                throw new GenesisNetworkSettingsWindowCleanupException(classLifetime);
            return null;
        }
        var state = new NatWindowState(window, instance, classLifetime, brush, adapter);
        NatStates[window] = state;
        if (!GenesisNetworkSettingsWindowInitialization.TryInitialize(
            state.CreateControls, state.Render,
            () => SetTimer(window, NatTimerId, NatRefreshIntervalMilliseconds, 0) != 0,
            state.AbortCreation)) return null;
        _ = ShowWindow(window, ShowWindowNormal);
        _ = SetForegroundWindow(window);
        _ = SetFocus(state.InitialFocus);
        return new(window, className, instance) { _natState = state };
    }

    internal static int NextNatFocus(int current, bool reverse, IReadOnlyList<bool> enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        for (var step = 1; step <= enabled.Count; step++)
        {
            var index = ((current + (reverse ? -step : step)) % enabled.Count + enabled.Count) % enabled.Count;
            if (enabled[index]) return index;
        }
        return -1;
    }

    private static nint ProcessNatKeyboard(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        var parent = unchecked((nint)referenceData);
        if (message == WindowMessageKeyDown && NatStates.TryGetValue(parent, out var state))
        {
            if (wParam == 0x09) { state.MoveFocus(window, GetKeyState(0x10) < 0); return 0; }
            if (wParam == VirtualKeyEscape ||
                (wParam == VirtualKeyEnter && window == state.CloseControl))
            { _ = PostMessageW(parent, WindowMessageClose, 0, 0); return 0; }
            // Space is deliberately left to native BS_AUTOCHECKBOX/button behavior.
        }
        if (message == WindowMessageNcDestroy)
            _ = RemoveWindowSubclass(window, NatKeyboardProcedure, subclassId);
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static bool TryProcessNatMessage(nint window, uint message, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (!NatStates.TryGetValue(window, out var state)) return false;
        switch (message)
        {
            case WindowMessageClose:
                state.CloseNative();
                return true;
            case WindowMessageNcDestroy:
                state.OnDestroyed();
                return false;
            case NatCloseMessage:
                if ((long)wParam == state.Model.Generation) state.CloseNative();
                return true;
            case NatCompletedMessage:
                if (state.Model.CanAcceptCallback((long)wParam)) state.Render();
                return true;
            case NatTimerMessage:
                if (wParam == NatTimerId && state.Model.CanAcceptCallback(state.Model.Generation)) state.Render();
                return true;
            case WindowMessageCommand:
                state.Command(wParam);
                return true;
            case 0x0133: // CTLCOLOR edit
            case 0x0135: // CTLCOLOR button
            case 0x0138: // CTLCOLOR static / read-only edit
                result = state.ControlColor(unchecked((nint)wParam), lParam);
                return true;
            case 0x002B: // DRAWITEM: close button
                result = NatWindowState.DrawClose(lParam) ? 1 : 0;
                return result != 0;
            default:
                return false;
        }
    }

    private sealed class NatWindowState : IAsyncDisposable
    {
        private readonly nint window;
        private readonly nint instance;
        private readonly GenesisNetworkSettingsWindowClassLifetime classLifetime;
        private readonly nint brush;
        private readonly uint threadId = GetCurrentThreadId();
        private readonly object disposalGate = new();
        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? disposal;
        private bool closingNative;
        private nint checkbox, settings, listener, mapping, diagnostics, notice, close;
        private GuiNextNetworkSettingsPresentation? last;
        public NatWindowState(nint window, nint instance, GenesisNetworkSettingsWindowClassLifetime classLifetime, nint brush, IGuiNextNetworkSettingsAdapter adapter)
        {
            this.window = window; this.instance = instance; this.classLifetime = classLifetime; this.brush = brush;
            Model = new GuiNextNetworkSettingsViewModel(adapter, generation =>
            {
                if (NatStates.TryGetValue(window, out var active) && ReferenceEquals(active, this))
                    _ = PostMessageW(window, NatCompletedMessage, (nuint)generation, 0);
            });
        }
        public GuiNextNetworkSettingsViewModel Model { get; }
        public nint CloseControl => close;
        public nint InitialFocus => Model.Refresh().CanEdit ? checkbox : close;

        public bool CreateControls()
        {
            checkbox = Add("BUTTON", NatCheckboxLabel, 20, 20, 750, 30, NatCheckboxId, WindowStyleTabStop | 0x00000003);
            settings = Add("STATIC", "", 20, 60, 760, 100, 2202);
            listener = Add("STATIC", "", 20, 175, 760, 65, 2203);
            mapping = Add("STATIC", "", 20, 250, 760, 195, 2204);
            var label = Add("STATIC", "診断情報（読み取り専用）", 20, 455, 760, 22, 2207);
            diagnostics = Add("EDIT", "", 20, 480, 760, 125, 2205,
                WindowStyleTabStop | 0x00000004 | 0x00000800 | 0x00200000 | 0x00000040);
            notice = Add("STATIC", "", 20, 620, 760, 42, 2206);
            close = Add("BUTTON", "閉じる", 675, 670, 105, 30, CloseControlId, WindowStyleTabStop | 0x0000000B);
            if (new[] { checkbox, settings, listener, mapping, diagnostics, notice, close, label }.Any(handle => handle == 0)) return false;
            foreach (var control in new[] { checkbox, diagnostics, close })
                if (!SetWindowSubclass(control, NatKeyboardProcedure, 0x73B0, unchecked((nuint)window))) return false;
            return true;
        }

        private nint Add(string type, string text, int x, int y, int width, int height, int id, uint style = 0) =>
            CreateControl(type, text, WindowStyleChild | WindowStyleVisible | style, x, y, width, height, window, id, instance);

        public void Render()
        {
            // Only WndProc/initial creation call this method, on the owning UI thread.
            var current = Model.Refresh();
            _ = EnableWindow(checkbox, current.CanEdit);
            _ = SendMessageW(checkbox, 0x00F1, current.Checked ? 1u : 0u, 0);
            SetChanged(settings, last?.Settings, current.Settings);
            SetChanged(listener, last?.Listener, current.Listener);
            SetChanged(mapping, last?.Mapping, current.Mapping);
            SetChanged(diagnostics, last?.Diagnostics, current.Diagnostics);
            SetChanged(notice, last?.Notice, current.Notice);
            if (last?.SettingsTone != current.SettingsTone || last?.ListenerTone != current.ListenerTone ||
                last?.MappingTone != current.MappingTone) _ = InvalidateRect(window, 0, true);
            last = current;
        }

        private static void SetChanged(nint control, string? before, string after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal)) _ = SetWindowTextW(control, after);
        }

        public void Command(nuint command)
        {
            if (((command >> 16) & 0xffff) != ButtonNotificationClicked) return;
            if ((int)(command & 0xffff) == CloseControlId) { _ = PostMessageW(window, WindowMessageClose, 0, 0); return; }
            if ((int)(command & 0xffff) != NatCheckboxId || !Model.Refresh().CanEdit) return;
            var enabled = SendMessageW(checkbox, 0x00F0, 0, 0) == 1;
            // CompleteAsync consumes failures and only posts a generation-tagged message.
            _ = Model.SetEnabledAsync(enabled);
            Render();
        }

        public void MoveFocus(nint current, bool reverse)
        {
            var controls = new[] { checkbox, diagnostics, close };
            var index = NextNatFocus(Array.IndexOf(controls, current), reverse, controls.Select(IsWindowEnabled).ToArray());
            if (index >= 0) _ = SetFocus(controls[index]);
        }

        public nint ControlColor(nint dc, nint control)
        {
            var tone = control == settings ? last?.SettingsTone :
                control == listener ? last?.ListenerTone :
                control == mapping ? last?.MappingTone :
                control == diagnostics ? GuiNextNetworkTone.Purple :
                control == notice && last?.Failure != NovaApplicationNetworkFailure.None ? GuiNextNetworkTone.Orange :
                GuiNextNetworkTone.Normal;
            _ = SetBkColor(dc, NatBackground);
            _ = SetTextColor(dc, ToneColor(tone ?? GuiNextNetworkTone.Normal));
            _ = SetBkMode(dc, 1);
            return brush;
        }

        public static bool DrawClose(nint parameter)
        {
            var item = Marshal.PtrToStructure<NatDrawItem>(parameter);
            if (item.ControlId != CloseControlId) return false;
            var fill = CreateSolidBrush(0x00B87832);
            try { _ = FillRect(item.Dc, ref item.Rectangle, fill); }
            finally { if (fill != 0) _ = DeleteObject(fill); }
            _ = SetTextColor(item.Dc, NatText);
            _ = SetBkMode(item.Dc, 1);
            _ = DrawTextW(item.Dc, "閉じる", -1, ref item.Rectangle, 0x0001 | 0x0004 | 0x0020);
            if ((item.State & 0x0010) != 0) _ = DrawFocusRect(item.Dc, ref item.Rectangle);
            return true;
        }

        public void OnDestroyed()
        {
            Model.Close();
            _ = KillTimer(window, NatTimerId);
            _ = NatStates.TryRemove(window, out _);
            if (!closingNative) closed.TrySetResult();
        }

        public bool CloseNative()
        {
            closingNative = true;
            try { return DestroyWindow(window); }
            finally
            {
                closingNative = false;
                if (!IsWindow(window)) closed.TrySetResult();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (disposalGate)
            {
                Model.Close(); // invalidate completion admission before native close
                return new(disposal ??= DisposeCoreAsync());
            }
        }

        public void AbortCreation()
        {
            // Creation and DestroyWindow run synchronously on the owner thread.
            // Never block that thread waiting for a native completion message.
            var cleanup = DisposeAsync();
            if (!cleanup.IsCompleted)
                throw new GenesisNetworkSettingsWindowCleanupException(this);
            cleanup.GetAwaiter().GetResult();
        }

        private async Task DisposeCoreAsync()
        {
            // A closed HWND can be reused by another window. Never close it again.
            NatStates.TryGetValue(window, out var active);
            if (GenesisNetworkSettingsWindowInitialization.ShouldCloseNative(
                closed.Task.IsCompleted, active, this, () => IsWindow(window)))
            {
                var requested = threadId == GetCurrentThreadId()
                    ? CloseNative()
                    : PostMessageW(window, NatCloseMessage, (nuint)Model.Generation, 0);
                if (!requested) throw new GenesisNetworkSettingsWindowCleanupException(this);
            }
            else closed.TrySetResult();
            await closed.Task.ConfigureAwait(false);
            if (!ReleaseClass()) throw new GenesisNetworkSettingsWindowCleanupException(this);
        }

        public bool ReleaseClass()
        {
            return closed.Task.IsCompletedSuccessfully && classLifetime.TryRelease();
        }
    }

    private static uint ToneColor(GuiNextNetworkTone tone) => tone switch
    {
        GuiNextNetworkTone.Blue => 0x00F2BB76,
        GuiNextNetworkTone.Green => 0x008BDD83,
        GuiNextNetworkTone.Orange => 0x0052B7FF,
        GuiNextNetworkTone.Purple => 0x00E2AFCB,
        _ => NatText,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct NatRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NatDrawItem
    {
        public uint ControlType, ControlId, ItemId, Action, State;
        public nint Window, Dc;
        public NatRectangle Rectangle;
        public nuint Data;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint window, nuint id, uint interval, nint callback);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowTextW(nint window, string text);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref NatRectangle rectangle, nint brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(nint dc, string text, int length, ref NatRectangle rectangle, uint format);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DrawFocusRect(nint dc, ref NatRectangle rectangle);
}

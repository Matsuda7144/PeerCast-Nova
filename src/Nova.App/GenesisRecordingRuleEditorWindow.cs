using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PeerCast.Nova.Core.Recording;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>Native modal projection only. Drafts, validation and persistence belong to App.</summary>
internal static class GenesisRecordingRuleEditorWindow
{
    internal const string Title = "録画ルール";
    internal static readonly string[] MatchModes = ["完全一致", "一部一致", "正規表現"];
    private const uint CompletedMessage = 0x8000 + 0x731;
    private static readonly ConcurrentDictionary<nint, State> States = new();
    private static readonly WindowProcedure Procedure = WindowProc;

    public static bool ShowModal(nint owner, GenesisRecordingRuleEditor editor, GenesisRecordingRuleDraft draft)
    {
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner)) { editor.Cancel(); return false; }
        var instance = GetModuleHandleW(null);
        var className = $"PeerCastNovaRecordingRuleEditor-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)32512),
            BackgroundBrush = (nint)6,
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0) { editor.Cancel(); return false; }
        nint window = 0;
        var wasEnabled = IsWindowEnabled(owner);
        var dpi = GenesisMainWindowResizeGuard.GetDpi(owner);
        try
        {
            window = CreateWindowExW(0, className, Title,
                0x00C00000 | 0x00080000 | 0x02000000, unchecked((int)0x80000000),
                unchecked((int)0x80000000),
                GenesisMainWindowLayout.Scale(690, dpi),
                GenesisMainWindowLayout.Scale(590, dpi),
                owner, 0, instance, 0);
            if (window == 0) return false;
            var state = new State(window, instance, editor, draft, dpi);
            States[window] = state;
            if (!state.CreateControls()) return false;
            _ = EnableWindow(owner, false);
            _ = ShowWindow(window, 1);
            _ = SetForegroundWindow(window);
            _ = SetFocus(state.NameControl);
            // Native modal message pump stays responsive while Save awaits the App boundary.
            while (state.Open)
            {
                var result = GetMessageW(out var message, 0, 0, 0);
                if (result <= 0)
                {
                    if (result == 0) PostQuitMessage((int)message.WParam);
                    break;
                }
                if (!IsDialogMessageW(window, ref message))
                {
                    _ = TranslateMessage(ref message);
                    _ = DispatchMessageW(ref message);
                }
            }
            return true;
        }
        finally
        {
            editor.Cancel(); // Cancel the attempt, never dispose the application repository.
            if (window != 0) { _ = States.TryRemove(window, out _); if (IsWindow(window)) _ = DestroyWindow(window); }
            _ = UnregisterClassW(className, instance);
            if (wasEnabled && IsWindow(owner)) { _ = EnableWindow(owner, true); _ = SetForegroundWindow(owner); }
        }
    }

    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (States.TryGetValue(window, out var state))
        {
            if (message == 0x0082) { state.Close(); }
            if (message == 0x0010) { state.Close(); return 0; }
            if (message == 0x0111 && ((wParam >> 16) & 0xffff) == 0)
            {
                state.Command((int)(wParam & 0xffff));
                return 0;
            }
            if (message == CompletedMessage) { state.Complete(); return 0; }
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class State(nint window, nint instance, GenesisRecordingRuleEditor editor, GenesisRecordingRuleDraft draft, uint dpi)
    {
        private readonly Dictionary<int, nint> controls = [];
        private GenesisRecordingRuleDraft frozen = draft;
        private GenesisRecordingRuleEditorResult? completed;
        private Task? saveOperation;
        private int closed;
        public bool Open => Volatile.Read(ref closed) == 0;
        public nint NameControl => controls[10];
        private nint Add(int id, string type, string text, int x, int y, int width, int height, uint extra = 0)
        {
            var value = CreateWindowExW(type == "EDIT" ? 0x200u : 0, type, text,
                0x40000000 | 0x10000000 | (id == 0 ? 0u : 0x10000u) | extra,
                GenesisMainWindowLayout.Scale(x, dpi),
                GenesisMainWindowLayout.Scale(y, dpi),
                GenesisMainWindowLayout.Scale(width, dpi),
                GenesisMainWindowLayout.Scale(height, dpi),
                window, (nint)id, instance, 0);
            if (id != 0) controls[id] = value;
            if (value != 0) _ = GenesisWindowUiFont.Apply(value);
            return value;
        }
        private void Field(int id, string label, string text, int y)
        {
            _ = Add(0, "STATIC", label, 16, y + 4, 125, 22);
            _ = Add(id, "EDIT", text, 148, y, 500, 26, 0x80);
        }
        private void Pattern(int id, string label, GenesisRecordingPatternDraft value, int y)
        {
            _ = Add(0, "STATIC", label, 16, y + 4, 125, 22);
            _ = Add(id, "EDIT", value.Value, 148, y, 345, 26, 0x80);
            Combo(id + 1, MatchModes, (int)value.Mode, 505, y, 143);
        }
        private void Combo(int id, string[] items, int selection, int x, int y, int width)
        {
            var combo = Add(id, "COMBOBOX", "", x, y, width, 140, 0x3 | 0x00200000);
            foreach (var item in items) _ = SendMessageText(combo, 0x143, 0, item);
            _ = SendMessageW(combo, 0x14E, (nuint)selection, 0);
        }
        public bool CreateControls()
        {
            Field(10, "ルール名", frozen.Name, 16);
            _ = Add(11, "BUTTON", "有効", 148, 52, 85, 26, 0x3);
            _ = SendMessageW(controls[11], 0xF1, frozen.IsEnabled ? 1u : 0, 0);
            _ = Add(0, "STATIC", "動作", 245, 56, 45, 22);
            Combo(12, ["録画", "除外"], (int)frozen.Action, 295, 52, 110);
            _ = Add(0, "STATIC", "優先度", 425, 56, 60, 22);
            _ = Add(13, "EDIT", frozen.Priority, 505, 52, 143, 26, 0x80);
            _ = Add(14, "BUTTON", "すべてのチャンネルに一致", 148, 90, 360, 28, 0x3);
            _ = SendMessageW(controls[14], 0xF1, frozen.MatchAll ? 1u : 0, 0);
            Field(15, "ChannelId (32桁)", frozen.ChannelId, 128);
            Pattern(16, "名前", frozen.NamePattern, 166);
            Pattern(18, "説明", frozen.DescriptionPattern, 204);
            Pattern(20, "StreamType", frozen.StreamTypePattern, 242);
            Field(22, "最小 kbps", frozen.MinimumBitrateKbps, 280);
            Field(23, "最大 kbps", frozen.MaximumBitrateKbps, 318);
            _ = Add(0, "STATIC", "指定した条件はすべて一致が必要です。空欄は未指定です。\n変更は次回のチャンネル更新時に反映されます。", 16, 360, 632, 46);
            _ = Add(1, "BUTTON", "保存", 440, 422, 96, 30, 0x1);
            _ = Add(2, "BUTTON", "キャンセル", 546, 422, 102, 30);
            _ = Add(24, "STATIC", "", 16, 468, 632, 65);
            return controls.Values.All(c => c != 0);
        }
        public void Command(int id)
        {
            if (id == 2) { Close(); return; }
            if (id == 14) { SetAvailability(); return; }
            if (id != 1 || saveOperation is { IsCompleted: false } || !editor.CanSave) return;
            frozen = frozen with
            {
                Name = Text(10),
                IsEnabled = Checked(11),
                Action = (RecordingRuleAction)Selection(12),
                Priority = Text(13),
                MatchAll = Checked(14),
                ChannelId = Text(15),
                NamePattern = ReadPattern(16),
                DescriptionPattern = ReadPattern(18),
                StreamTypePattern = ReadPattern(20),
                MinimumBitrateKbps = Text(22),
                MaximumBitrateKbps = Text(23),
            };
            BeginSave(false);
        }
        private void BeginSave(bool confirmed)
        {
            foreach (var pair in controls.Where(p => p.Key != 2 && p.Key != 24)) _ = EnableWindow(pair.Value, false);
            _ = SetWindowTextW(controls[24], "保存を確認しています…");
            saveOperation = SaveAsync(confirmed);
        }
        private async Task SaveAsync(bool confirmed)
        {
            try
            {
                completed = await editor.SaveAsync(frozen, confirmed).ConfigureAwait(false);
            }
            catch
            {
                completed = new(null, "録画ルールの保存を確認できませんでした。再読み込みしてください。");
            }
            if (Open) _ = PostMessageW(window, CompletedMessage, 0, 0);
        }
        public void Complete()
        {
            if (!Open || completed is not { } result) return;
            if (result.RequiresConfirmation)
            {
                if (MessageBoxW(window, result.Message, Title, 0x24) == 6) { BeginSave(true); return; }
                _ = SetWindowTextW(controls[24], "保存を取りやめました。");
            }
            else if (result.Applied) { Close(); return; }
            else _ = SetWindowTextW(controls[24], result.Message);
            SetAvailability();
        }
        private void SetAvailability()
        {
            foreach (var pair in controls) _ = EnableWindow(pair.Value,
                pair.Key == 1 ? editor.CanSave : pair.Key is < 15 or > 23 || !Checked(14));
        }
        public void Close() { Interlocked.Exchange(ref closed, 1); editor.Cancel(); }
        private bool Checked(int id) => SendMessageW(controls[id], 0xF0, 0, 0) == 1;
        private int Selection(int id) => unchecked((int)SendMessageW(controls[id], 0x147, 0, 0));
        private GenesisRecordingPatternDraft ReadPattern(int id) => new(Text(id), (RecordingTextMatchMode)Selection(id + 1));
        private string Text(int id)
        {
            var buffer = new char[GetWindowTextLengthW(controls[id]) + 1];
            var count = GetWindowTextW(controls[id], buffer, buffer.Length);
            return count > 0 ? new string(buffer, 0, count) : "";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; public uint Private; }
    [DllImport("user32.dll")] private static extern int GetMessageW(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsDialogMessageW(nint window, ref Message message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(nint window, bool enabled);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(nint window, string text, string caption, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern nint SendMessageText(nint window, uint message, nuint wParam, string text);
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
        public nint MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, [Out] char[] text, int maximumCount);

}

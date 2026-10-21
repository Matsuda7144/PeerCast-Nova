using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace PeerCast.Nova.App;

/// <summary>
/// Small native path editor for playback and recording settings. Input normalization
/// and persistence remain owned by the corresponding application settings model.
/// </summary>
internal sealed class GenesisGuiNextPlaybackSettingsWindow : IAsyncDisposable
{
    private const uint WindowStyleCaption = 0x00C00000;
    private const uint WindowStyleSysMenu = 0x00080000;
    private const uint WindowStyleClipChildren = 0x02000000;
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint EditStyleAutoHorizontalScroll = 0x0080;
    private const uint ExtendedStyleClientEdge = 0x00000200;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const int ButtonNotificationClicked = 0;
    private const int ArrowCursor = 32512;
    private const int ColorWindow = 5;
    private const int ShowWindowNormal = 1;
    private const int UseDefaultPosition = unchecked((int)0x80000000);
    private const int FileMustExist = 0x00001000;
    private const int PathMustExist = 0x00000800;
    private const int NoChangeDirectory = 0x00000008;
    private const int PathControlId = 1001;
    private const int BrowseControlId = 1002;
    private const int SaveControlId = 1003;
    private const int ClearControlId = 1004;
    private const int CloseControlId = 1005;
    private const int StatusControlId = 1006;
    private const int WindowWidth = 650;
    private const int WindowHeight = 245;
    private static readonly ConcurrentDictionary<nint, WindowState> StatesByWindow = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;

    private readonly nint _window;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisGuiNextPlaybackSettingsWindow(
        nint window,
        string className,
        nint instance)
    {
        _window = window;
        _className = className;
        _instance = instance;
    }

    public bool IsOpen =>
        Volatile.Read(ref _disposed) == 0 &&
        _window != 0 &&
        IsWindow(_window);

    public static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner,
        GenesisGuiNextPlaybackSettings settings) => TryCreate(owner, settings, null);

    internal static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner, GenesisRecordingOutputDirectorySettings settings) => TryCreate(owner, null, settings);

    private static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner, GenesisGuiNextPlaybackSettings? settings,
        GenesisRecordingOutputDirectorySettings? recordingSettings)
    {
        ArgumentNullException.ThrowIfNull((object?)settings ?? recordingSettings);
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner))
        {
            return null;
        }

        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNova{(recordingSettings is null ? "Playback" : "Recording")}Settings-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)ArrowCursor),
            BackgroundBrush = (nint)(ColorWindow + 1),
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            return null;
        }

        var window = CreateWindowExW(
            0,
            className,
            recordingSettings is null ? "Playback / PCRPlayer" : "録画保存先",
            WindowStyleCaption |
                WindowStyleSysMenu |
                WindowStyleClipChildren |
                WindowStyleVisible,
            UseDefaultPosition,
            UseDefaultPosition,
            WindowWidth,
            recordingSettings is null ? WindowHeight : 315,
            owner,
            0,
            instance,
            0);
        if (window == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var controls = CreateControls(window, instance, recordingSettings is not null);
        if (controls is null)
        {
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var state = new WindowState(window, controls, settings, recordingSettings);
        if (!StatesByWindow.TryAdd(window, state))
        {
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        state.Initialize();
        _ = ShowWindow(window, ShowWindowNormal);
        if (!IsWindowVisible(window))
        {
            _ = StatesByWindow.TryRemove(window, out _);
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        _ = SetForegroundWindow(window);
        _ = SetFocus(controls.Path);
        return new(window, className, instance);
    }

    public void Activate()
    {
        if (IsOpen)
        {
            _ = ShowWindow(_window, ShowWindowNormal);
            _ = SetForegroundWindow(_window);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _ = StatesByWindow.TryRemove(_window, out _);
        if (_window != 0 && IsWindow(_window))
        {
            _ = DestroyWindow(_window);
        }

        _ = UnregisterClassW(_className, _instance);
        return ValueTask.CompletedTask;
    }

    internal static bool TryResolveBrowsePath(string? selectedPath, out string? normalizedPath)
    {
        normalizedPath = GenesisGuiNextPlaybackSettings.NormalizePath(selectedPath);
        return !string.IsNullOrWhiteSpace(normalizedPath);
    }

    internal static string GetPcrPlayerSaveFeedback(
        GenesisGuiNextPlaybackSettingsUpdateResult result,
        string? requestedPath) =>
        result switch
        {
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed =>
                "設定を保存できませんでした。保存場所を確認してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath =>
                "絶対パスを入力してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedExecutableName =>
                "ファイル名は PCRPlayer64.exe にしてください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound =>
                "指定した実行ファイルが見つかりません。",
            GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected =>
                "フォルダーではなく実行ファイルを指定してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.AccessDenied =>
                "実行ファイルを確認できません。アクセス権を確認してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.PathCheckFailed =>
                "実行ファイルを確認できません。パスを確認してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged when
                string.IsNullOrWhiteSpace(requestedPath) =>
                "PCRPlayer設定は未設定です。次の再生では環境変数またはPATHを使います。",
            GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged =>
                "PCRPlayer設定は既に保存されています。次の再生から使用します。",
            _ when string.IsNullOrWhiteSpace(requestedPath) =>
                "未設定に戻しました。次の再生では環境変数またはPATHを使います。",
            _ => "PCRPlayer設定を保存しました。次の再生から使用します。",
        };

    private static Controls? CreateControls(nint window, nint instance, bool recordingMode)
    {
        var label = CreateControl(window, instance, "STATIC", recordingMode ? "録画保存先 (絶対パス)" : "PCRPlayer 実行ファイル", 16, 16, 580, 20, 0);
        var path = CreateControl(
            window,
            instance,
            "EDIT",
            string.Empty,
            16,
            42,
            recordingMode ? 610 : 505,
            26,
            PathControlId,
            WindowStyleTabStop | EditStyleAutoHorizontalScroll,
            ExtendedStyleClientEdge);
        var browse = recordingMode ? 0 : CreateControl(window, instance, "BUTTON", "参照...", 530, 41, 96, 28, BrowseControlId,
            WindowStyleTabStop);
        var info = recordingMode
            ? CreateControl(window, instance, "STATIC", string.Empty, 16, 78, 610, 58, 0)
            : 0;
        var help = CreateControl(
            window,
            instance,
            "STATIC",
            recordingMode ? GenesisRecordingOutputDirectorySettings.NextStartNotice :
                "空欄では既存の NOVA_PCRPLAYER_EXE または PATH を使います。保存した設定は次のPlayから反映されます。",
            16,
            recordingMode ? 142 : 78,
            610,
            38,
            0);
        var save = CreateControl(window, instance, "BUTTON", "保存", 16, recordingMode ? 182 : 130, 90, 30, SaveControlId,
            WindowStyleTabStop);
        var clear = CreateControl(window, instance, "BUTTON", recordingMode ? "既定に戻す" : "未設定に戻す", 116, recordingMode ? 182 : 130, 125, 30, ClearControlId,
            WindowStyleTabStop);
        var close = CreateControl(window, instance, "BUTTON", "閉じる", 536, recordingMode ? 182 : 130, 90, 30, CloseControlId,
            WindowStyleTabStop);
        var status = CreateControl(window, instance, "STATIC", string.Empty, 16, recordingMode ? 224 : 170, 610, recordingMode ? 42 : 20, StatusControlId);
        if (label == 0 || path == 0 || (!recordingMode && browse == 0) || (recordingMode && info == 0) || help == 0 || save == 0 || clear == 0 || close == 0 || status == 0)
        {
            return null;
        }

        foreach (var control in new[] { label, path, browse, info, help, save, clear, close, status })
        {
            if (control != 0) ApplyDefaultFont(control);
        }

        return new(path, browse, save, clear, close, status, info);
    }

    private static nint CreateControl(
        nint parent,
        nint instance,
        string className,
        string text,
        int x,
        int y,
        int width,
        int height,
        int controlId,
        uint additionalStyle = 0,
        uint extendedStyle = 0) =>
        CreateWindowExW(
            extendedStyle,
            className,
            text,
            WindowStyleChild | WindowStyleVisible | additionalStyle,
            x,
            y,
            width,
            height,
            parent,
            (nint)controlId,
            instance,
            0);

    private static void ApplyDefaultFont(nint control) => _ = GenesisWindowUiFont.Apply(control);

    private static nint ProcessWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WindowMessageNcDestroy)
        {
            _ = StatesByWindow.TryRemove(window, out _);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageCommand && StatesByWindow.TryGetValue(window, out var state))
        {
            state.ProcessCommand(wParam, lParam);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class WindowState(nint window, Controls controls, GenesisGuiNextPlaybackSettings? settings,
        GenesisRecordingOutputDirectorySettings? recordingSettings)
    {
        public void Initialize()
        {
            if (recordingSettings is null)
            {
                SetText(controls.Path, settings!.PcrPlayerExecutablePath ?? string.Empty);
                return;
            }
            RefreshRecording();
            SetText(controls.Status, recordingSettings.Presentation.Error ?? string.Empty);
        }

        private void RefreshRecording()
        {
            var presentation = recordingSettings!.Presentation;
            SetText(controls.Path, presentation.ConfiguredDirectory ?? presentation.EffectiveDirectory);
            SetText(controls.Info, $"今回の保存先: {presentation.CurrentRuntimeDirectory}\r\n次回の保存先 ({(presentation.UsesDefault ? "既定" : "カスタム")}): {presentation.EffectiveDirectory}");
        }

        public void ProcessCommand(nuint wParam, nint lParam)
        {
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode != ButtonNotificationClicked)
            {
                return;
            }

            switch ((int)(wParam & 0xffff))
            {
                case BrowseControlId:
                    Browse();
                    break;
                case SaveControlId:
                    Save(GetText(controls.Path));
                    break;
                case ClearControlId:
                    Save(null);
                    break;
                case CloseControlId:
                    _ = PostMessageW(window, 0x0010, 0, 0);
                    break;
            }
        }

        private void Browse()
        {
            var fileBuffer = new StringBuilder(32768);
            var current = GenesisGuiNextPlaybackSettings.NormalizePath(GetText(controls.Path));
            if (!string.IsNullOrWhiteSpace(current))
            {
                fileBuffer.Append(current);
            }

            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = window,
                Filter = "Executable files (*.exe)\0*.exe\0All files (*.*)\0*.*\0\0",
                File = fileBuffer,
                MaximumFile = fileBuffer.Capacity,
                Title = "PCRPlayer executable",
                Flags = FileMustExist | PathMustExist | NoChangeDirectory,
                DefaultExtension = "exe",
            };
            if (GetOpenFileNameW(ref dialog))
            {
                _ = TryResolveBrowsePath(dialog.File?.ToString(), out var selectedPath);
                SetText(controls.Path, selectedPath ?? string.Empty);
            }
        }

        private void Save(string? path)
        {
            if (recordingSettings is not null)
            {
                var saveResult = path is null ? recordingSettings.ResetToDefault() : recordingSettings.Save(path);
                SetText(controls.Status, saveResult == GenesisRecordingOutputDirectorySaveResult.Saved
                    ? "設定を保存しました。" + GenesisRecordingOutputDirectorySettings.NextStartNotice
                    : recordingSettings.Presentation.Error ?? "設定を保存できませんでした。");
                if (saveResult == GenesisRecordingOutputDirectorySaveResult.Saved) RefreshRecording();
                return;
            }
            var result = settings!.SetPcrPlayerExecutablePath(path);
            SetText(controls.Status, GetPcrPlayerSaveFeedback(result, path));
            if (result is GenesisGuiNextPlaybackSettingsUpdateResult.Saved or
                GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged)
            {
                SetText(controls.Path, settings.PcrPlayerExecutablePath ?? string.Empty);
            }
        }
    }

    private static string GetText(nint control)
    {
        var length = GetWindowTextLengthW(control);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(control, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    private static void SetText(nint control, string text) => _ = SetWindowTextW(control, text);

    private sealed record Controls(nint Path, nint Browse, nint Save, nint Clear, nint Close, nint Status, nint Info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public nint Owner;
        public nint Instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Filter;
        public nint CustomFilter;
        public int MaximumCustomFilter;
        public int FilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)] public StringBuilder? File;
        public int MaximumFile;
        public nint FileTitle;
        public int MaximumFileTitle;
        public nint InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DefaultExtension;
        public nint CustomData;
        public nint Hook;
        public nint TemplateName;
        public nint Reserved;
        public int ReservedValue;
        public int ExtendedFlags;
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
        public nint MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern int ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, [Out] char[] text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint window, string text);

    [DllImport("user32.dll")]
    private static extern nuint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName openFileName);
}

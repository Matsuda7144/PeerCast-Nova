using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PeerCast.Nova.Foundation.Configuration;

using static PeerCast.Nova.App.Win32NativeMethods;

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
    internal const int BrowseFileCapacity = 32768;
    internal const string BrowseFailureText = "実行ファイルを確認できません。パスを確認してください。";
    private static readonly ConcurrentDictionary<nint, WindowState> StatesByWindow = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;

    private readonly nint _window;
    private readonly WindowState _state;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisGuiNextPlaybackSettingsWindow(
        nint window,
        string className,
        nint instance, WindowState state)
    {
        _state = state;
        _window = window;
        _className = className;
        _instance = instance;
    }

    public bool IsOpen =>
        Volatile.Read(ref _disposed) == 0 &&
        _window != 0 && StatesByWindow.TryGetValue(_window, out var active) && ReferenceEquals(active, _state) &&
        IsWindow(_window);

    public static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner,
        GenesisGuiNextPlaybackSettings settings) => TryCreate(owner, settings, null);

    internal static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner, GenesisRecordingOutputDirectorySettings settings) => TryCreate(owner, null, settings);

    internal static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner, GenesisAppearanceSettings appearance) => TryCreate(owner, null, null, appearance);

    private static GenesisGuiNextPlaybackSettingsWindow? TryCreate(
        nint owner, GenesisGuiNextPlaybackSettings? settings,
        GenesisRecordingOutputDirectorySettings? recordingSettings, GenesisAppearanceSettings? appearance = null)
    {
        ArgumentNullException.ThrowIfNull((object?)settings ?? recordingSettings ?? (object?)appearance);
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner))
        {
            return null;
        }

        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNova{(appearance is not null ? "Appearance" : recordingSettings is null ? "Playback" : "Recording")}Settings-{Guid.NewGuid():N}";
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

        var dpi = GenesisMainWindowResizeGuard.GetDpi(owner);
        var window = CreateWindowExW(
            0,
            className,
            appearance is not null ? "外観設定" : recordingSettings is null ? "再生 / 外部プレイヤー" : "録画保存先",
            WindowStyleCaption |
                WindowStyleSysMenu |
                WindowStyleClipChildren |
                WindowStyleVisible,
            UseDefaultPosition,
            UseDefaultPosition,
            GenesisMainWindowLayout.Scale(WindowWidth, dpi),
            GenesisMainWindowLayout.Scale(appearance is not null ? 280 : recordingSettings is null ? WindowHeight : 315, dpi),
            owner,
            0,
            instance,
            0);
        if (window == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var controls = appearance is not null ? CreateAppearanceControls(window, instance, dpi) :
            CreateControls(window, instance, recordingSettings is not null, dpi);
        if (controls is null)
        {
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var state = new WindowState(window, controls, settings, recordingSettings, appearance);
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
        return new(window, className, instance, state);
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

        // An already closed editor's HWND may have been reused by another editor.
        // WM_NCDESTROY removes only this live state after native child/font cleanup.
        if (StatesByWindow.TryGetValue(_window, out var active) && ReferenceEquals(active, _state) && IsWindow(_window))
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

    internal delegate bool ShowOpenFileDialog(ref OpenFileName dialog);

    internal readonly record struct BrowseResult(string? SelectedPath, bool Failed);

    internal static BrowseResult BrowseForPlayerFile(nint owner, string? currentPath, bool library = false) =>
        BrowseForPlayerFile(owner, currentPath, GetOpenFileNameW, CommDlgExtendedError, Marshal.FreeHGlobal, Console.Error.WriteLine, library);

    internal static BrowseResult BrowseForPlayerFile(
        nint owner,
        string? currentPath,
        ShowOpenFileDialog showDialog,
        Func<uint> getExtendedError,
        Action<nint> releaseBuffer,
        Action<string> reportFailure,
        bool library = false)
    {
        nint fileBuffer = 0;
        try
        {
            var characters = new char[BrowseFileCapacity];
            var current = GenesisGuiNextPlaybackSettings.NormalizePath(currentPath);
            if (current is not null && current.Length < characters.Length &&
                !current.Any(static character => character < ' ' || character is '"' or '<' or '>' or '|' or '?' or '*'))
            {
                current.CopyTo(0, characters, 0, current.Length);
            }

            fileBuffer = Marshal.AllocHGlobal(checked(characters.Length * sizeof(char)));
            Marshal.Copy(characters, 0, fileBuffer, characters.Length);
            var dialog = new OpenFileName
            {
                StructSize = (uint)Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = library ? "libmpv DLL (*.dll)\0*.dll\0\0" :
                    "Executable files (*.exe)\0*.exe\0All files (*.*)\0*.*\0\0",
                File = fileBuffer,
                MaximumFile = BrowseFileCapacity,
                Title = library ? "内蔵プレイヤーの libmpv DLL を選択" : "外部プレイヤーの実行ファイルを選択",
                Flags = FileMustExist | PathMustExist | NoChangeDirectory,
                DefaultExtension = library ? "dll" : "exe",
            };
            if (!showDialog(ref dialog))
            {
                // Read the common-dialog error before cleanup or another native call.
                var error = getExtendedError();
                if (error == 0) return default;
                ReportBrowseFailure($"CommonDialogError=0x{error:X8}", reportFailure, library);
                return new(null, true);
            }

            // Read only our owned allocation, even if the returned struct was changed.
            Marshal.Copy(fileBuffer, characters, 0, characters.Length);
            var terminator = Array.IndexOf(characters, '\0');
            if (terminator <= 0 ||
                !TryResolveBrowsePath(new string(characters, 0, terminator), out var selectedPath))
            {
                ReportBrowseFailure("InvalidSelectionBuffer", reportFailure, library);
                return new(null, true);
            }
            return new(selectedPath, false);
        }
        catch (Exception exception) when (exception is ArgumentException or
            MarshalDirectiveException or TypeLoadException or NotSupportedException or
            System.ComponentModel.Win32Exception or COMException)
        {
            ReportBrowseFailure(exception.GetType().Name, reportFailure, library);
            return new(null, true);
        }
        finally
        {
            if (fileBuffer != 0) releaseBuffer(fileBuffer);
        }
    }

    private static void ReportBrowseFailure(string diagnostic, Action<string> reportFailure, bool library)
    {
        try
        {
            var file = library ? "libmpv DLL" : "External player";
            reportFailure($"{file} browse failed: {diagnostic}.");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // A closed diagnostic stream must not turn a recoverable dialog failure fatal.
        }
    }

    internal static string GetPlayerPathSaveFeedback(
        GenesisGuiNextPlaybackSettingsUpdateResult result,
        string? requestedPath,
        bool library = false)
    {
        var fileLabel = library ? "libmpv DLL" : "実行ファイル";
        return result switch
        {
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed =>
                "設定を保存できませんでした。保存場所を確認してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath =>
                "有効な絶対パスを入力してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.LibraryRequired =>
                "内蔵プレイヤーで使用する libmpv DLL（.dll）を指定してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedFileExtension =>
                library ? "libmpv DLL（.dll）を指定してください。" : ".exeファイルを指定してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.FileNotFound =>
                $"指定した{fileLabel}が見つかりません。",
            GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected =>
                library ? "libmpv DLL のファイルを指定してください。" : "フォルダーではなく実行ファイルを指定してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.AccessDenied =>
                $"{fileLabel}を確認できません。アクセス権を確認してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.PathCheckFailed =>
                $"{fileLabel}を確認できません。パスを確認してください。",
            GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged when
                GenesisGuiNextPlaybackSettings.NormalizePath(requestedPath) is null =>
                "外部プレイヤーは未設定です。次の再生では環境変数またはPATHを使います。",
            GenesisGuiNextPlaybackSettingsUpdateResult.Unchanged =>
                "外部プレイヤーの設定は既に保存されています。次の再生から使用します。",
            _ when GenesisGuiNextPlaybackSettings.NormalizePath(requestedPath) is null =>
                "未設定に戻しました。次の再生では環境変数またはPATHを使います。",
            _ => "外部プレイヤーの設定を保存しました。次の再生から使用します。",
        };
    }

    private static Controls? CreateAppearanceControls(nint window, nint instance, uint dpi)
    {
        var label = CreateControl(window, instance, "STATIC", "表示モード", 16, 16, 610, 22, 0, dpi: dpi);
        var classic = CreateControl(window, instance, "BUTTON", "クラシック", 24, 46, 200, 28, PathControlId,
            WindowStyleTabStop | 0x00020000 | 9, dpi: dpi); // WS_GROUP | BS_AUTORADIOBUTTON
        var modern = CreateControl(window, instance, "BUTTON", "モダン", 240, 46, 200, 28, BrowseControlId,
            WindowStyleTabStop | 9, dpi: dpi);
        var help = CreateControl(window, instance, "STATIC", GenesisAppearanceSettings.NextStartNotice, 16, 90, 610, 40, 0, dpi: dpi);
        var save = CreateControl(window, instance, "BUTTON", "保存", 16, 142, 90, 30, SaveControlId,
            WindowStyleTabStop | 0x00020000, dpi: dpi);
        var close = CreateControl(window, instance, "BUTTON", "閉じる", 536, 142, 90, 30, CloseControlId, WindowStyleTabStop, dpi: dpi);
        var status = CreateControl(window, instance, "STATIC", string.Empty, 16, 184, 610, 46, StatusControlId, dpi: dpi);
        foreach (var control in new[] { label, classic, modern, help, save, close, status })
        {
            if (control == 0) return null;
            ApplyDefaultFont(control);
        }
        return new(classic, modern, save, 0, close, status, 0);
    }

    private static Controls? CreateControls(nint window, nint instance, bool recordingMode, uint dpi)
    {
        var label = CreateControl(window, instance, "STATIC", recordingMode ? "録画保存先 (絶対パス)" : "外部プレイヤーの実行ファイルパス", 16, 16, 580, 20, 0, dpi: dpi);
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
            ExtendedStyleClientEdge,
            dpi);
        var browse = recordingMode ? 0 : CreateControl(window, instance, "BUTTON", "参照...", 530, 41, 96, 28, BrowseControlId,
            WindowStyleTabStop, dpi: dpi);
        var info = recordingMode
            ? CreateControl(window, instance, "STATIC", string.Empty, 16, 78, 610, 58, 0, dpi: dpi)
            : 0;
        var help = CreateControl(
            window,
            instance,
            "STATIC",
            recordingMode ? GenesisRecordingOutputDirectorySettings.NextStartNotice :
                ".exeファイルの絶対パスを指定します。保存した設定は次の再生から使用します。\r\n空欄では環境変数またはPATHを使います。",
            16,
            recordingMode ? 142 : 78,
            610,
            38,
            0,
            dpi: dpi);
        var save = CreateControl(window, instance, "BUTTON", "保存", 16, recordingMode ? 182 : 130, 90, 30, SaveControlId,
            WindowStyleTabStop, dpi: dpi);
        var clear = CreateControl(window, instance, "BUTTON", recordingMode ? "既定に戻す" : "未設定に戻す", 116, recordingMode ? 182 : 130, 125, 30, ClearControlId,
            WindowStyleTabStop, dpi: dpi);
        var close = CreateControl(window, instance, "BUTTON", "閉じる", 536, recordingMode ? 182 : 130, 90, 30, CloseControlId,
            WindowStyleTabStop, dpi: dpi);
        var status = CreateControl(window, instance, "STATIC", string.Empty, 16, recordingMode ? 224 : 170, 610, recordingMode ? 42 : 20, StatusControlId, dpi: dpi);
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
        uint extendedStyle = 0,
        uint dpi = 96) =>
        CreateWindowExW(
            extendedStyle,
            className,
            text,
            WindowStyleChild | WindowStyleVisible | additionalStyle,
            GenesisMainWindowLayout.Scale(x, dpi),
            GenesisMainWindowLayout.Scale(y, dpi),
            GenesisMainWindowLayout.Scale(width, dpi),
            GenesisMainWindowLayout.Scale(height, dpi),
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
        GenesisRecordingOutputDirectorySettings? recordingSettings, GenesisAppearanceSettings? appearance)
    {
        public void Initialize()
        {
            if (appearance is not null)
            {
                _ = SendMessageW(controls.Path, 0x00F1, appearance.ConfiguredMode == NovaGuiMode.Classic ? 1u : 0u, 0);
                _ = SendMessageW(controls.Browse, 0x00F1, appearance.ConfiguredMode == NovaGuiMode.Modern ? 1u : 0u, 0);
                SetText(controls.Status, appearance.Error ?? string.Empty);
                return;
            }
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
                    if (appearance is null) Browse();
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
            var result = BrowseForPlayerFile(window, GetText(controls.Path));
            if (result.SelectedPath is { } selectedPath)
            {
                SetText(controls.Path, selectedPath);
                SetText(controls.Status, string.Empty);
            }
            else if (result.Failed)
            {
                SetText(controls.Status, BrowseFailureText);
            }
        }

        private void Save(string? path)
        {
            if (appearance is not null)
            {
                var mode = SendMessageW(controls.Browse, 0x00F0, 0, 0) == 1 ? NovaGuiMode.Modern : NovaGuiMode.Classic;
                SetText(controls.Status, appearance.Save(mode)
                    ? "設定を保存しました。変更は次回起動時から適用されます。"
                    : appearance.Error ?? "設定を保存できませんでした。");
                return;
            }
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
            SetText(controls.Status, GetPlayerPathSaveFeedback(result, path));
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
    internal struct OpenFileName
    {
        public uint StructSize;
        public nint Owner;
        public nint Instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Filter;
        public nint CustomFilter;
        public uint MaximumCustomFilter;
        public uint FilterIndex;
        public nint File;
        public uint MaximumFile;
        public nint FileTitle;
        public uint MaximumFileTitle;
        public nint InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
        public uint Flags;
        public ushort FileOffset;
        public ushort FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DefaultExtension;
        public nint CustomData;
        public nint Hook;
        public nint TemplateName;
        public nint Reserved;
        public uint ReservedValue;
        public uint ExtendedFlags;
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
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, [Out] char[] text, int maximumCount);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName openFileName);

    [DllImport("comdlg32.dll", ExactSpelling = true)]
    private static extern uint CommDlgExtendedError();
}

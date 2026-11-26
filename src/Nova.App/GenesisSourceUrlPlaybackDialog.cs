using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// Small native dialog for the explicitly requested, user-provided source URL.
/// It submits through the same source interaction and view-model validation as
/// the retired Genesis edit control.
/// </summary>
internal static class GenesisSourceUrlPlaybackDialog
{
    private const uint WindowStyleCaption = 0x00C00000;
    private const uint WindowStyleSysMenu = 0x00080000;
    private const uint WindowStyleClipChildren = 0x02000000;
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowStylePopup = 0x80000000;
    private const uint EditStyleAutoHorizontalScroll = 0x0080;
    private const uint ExtendedStyleDialogModalFrame = 0x00000001;
    private const uint ExtendedStyleClientEdge = 0x00000200;
    private const uint ButtonStyleOwnerDraw = 0x0000000B;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageDrawItem = 0x002B;
    private const uint WindowMessageControlColorEdit = 0x0133;
    private const uint WindowMessageControlColorStatic = 0x0138;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageEditSetLimitText = 0x00C5;
    private const uint WindowMessagePlaybackCompleted = 0x8000 + 0x71A;
    private const nuint KeyboardSubclassIdentifier = 0x71A0;
    private const int ButtonNotificationClicked = 0;
    private const int DefaultGuiFont = 17;
    private const uint DrawItemTypeButton = 4;
    private const int ArrowCursor = 32512;
    private const int ShowWindowNormal = 1;
    private const int DialogWidth = 500;
    private const int DialogHeight = 190;
    private const int LabelControlId = 5100;
    private const int SourceControlId = 5101;
    private const int ErrorControlId = 5102;
    private const int PlayControlId = 5103;
    private const int CancelControlId = 5104;
    private const int DialogBackground = 0x00412917;
    private const int EditBackground = 0x00312318;
    private const int PlayBackground = 0x00B87832;
    private const int CancelBackground = 0x00513A2A;
    private const int TextColor = 0x00F7F4F2;
    private const int ErrorColor = 0x003A8CFF;
    private const int DisabledTextColor = 0x009D9188;
    private const int TransparentBackgroundMode = 1;
    private const uint DrawTextSingleLine = 0x0020;
    private const uint DrawTextVerticalCenter = 0x0004;
    private const uint DrawTextNoPrefix = 0x0800;
    private const uint DrawItemDisabled = 0x0004;
    private const uint DrawItemFocused = 0x0010;
    private const int UseDefaultPosition = unchecked((int)0x80000000);
    private const string WindowClassName = "PeerCastNovaSourceUrlPlaybackDialog";
    private static readonly ConcurrentDictionary<nint, DialogState> StatesByWindow = new();
    private static readonly ConcurrentDictionary<nint, DialogState> StatesByOwner = new();
    private static readonly object RegistrationSync = new();
    private static readonly WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private static readonly SubclassProcedure KeyboardSubclassProcedureDelegate =
        ProcessKeyboardMessage;
    private static nint _instance;
    private static nint _backgroundBrush;
    private static int _classRegistered;

    public static bool TryShow(
        nint owner,
        GenesisMainViewModel viewModel,
        GenesisMainWindowInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        if (interaction.IsRecordingHistoryView || !OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner))
        {
            return false;
        }

        if (StatesByOwner.TryGetValue(owner, out var existing))
        {
            existing.Activate();
            return true;
        }

        if (!EnsureWindowClass())
        {
            return false;
        }

        var dpi = GenesisMainWindowResizeGuard.GetDpi(owner);
        var scaledWidth = GenesisMainWindowLayout.Scale(DialogWidth, dpi);
        var scaledHeight = GenesisMainWindowLayout.Scale(DialogHeight, dpi);
        var rectangle = new WindowRectangle();
        var x = UseDefaultPosition;
        var y = UseDefaultPosition;
        if (GetWindowRect(owner, out rectangle))
        {
            x = rectangle.Left + Math.Max(0, (rectangle.Right - rectangle.Left - scaledWidth) / 2);
            y = rectangle.Top + Math.Max(0, (rectangle.Bottom - rectangle.Top - scaledHeight) / 2);
        }

        var window = CreateWindowExW(
            ExtendedStyleDialogModalFrame,
            WindowClassName,
            "URLを指定して再生…",
            WindowStylePopup | WindowStyleCaption | WindowStyleSysMenu | WindowStyleClipChildren,
            x,
            y,
            scaledWidth,
            scaledHeight,
            owner,
            0,
            _instance,
            0);
        if (window == 0)
        {
            return false;
        }

        var controls = CreateControls(window, _instance, dpi);
        if (controls is null)
        {
            _ = DestroyWindow(window);
            return false;
        }

        var state = new DialogState(
            window,
            owner,
            viewModel,
            interaction,
            controls.Value,
            IsWindowEnabled(owner));
        if (!StatesByWindow.TryAdd(window, state) ||
            !StatesByOwner.TryAdd(owner, state) ||
            !state.AttachKeyboard())
        {
            _ = StatesByWindow.TryRemove(window, out _);
            _ = StatesByOwner.TryRemove(owner, out _);
            state.DetachKeyboard();
            _ = DestroyWindow(window);
            return false;
        }

        if (state.OwnerWasEnabled)
        {
            _ = EnableWindow(owner, false);
        }

        _ = ShowWindow(window, ShowWindowNormal);
        _ = SetForegroundWindow(window);
        _ = SetFocus(controls.Value.SourceEdit);
        return true;
    }

    internal static string? GetValidationError(string? source) =>
        GenesisMainViewModel.GetSourceValidationError(source);

    private static bool EnsureWindowClass()
    {
        if (Volatile.Read(ref _classRegistered) != 0)
        {
            return true;
        }

        lock (RegistrationSync)
        {
            if (_classRegistered != 0)
            {
                return true;
            }

            var instance = GetModuleHandleW(null);
            var backgroundBrush = CreateSolidBrush((uint)DialogBackground);
            if (instance == 0 || backgroundBrush == 0)
            {
                if (backgroundBrush != 0)
                {
                    _ = DeleteObject(backgroundBrush);
                }

                return false;
            }

            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(
                    WindowProcedureDelegate),
                Instance = instance,
                Cursor = LoadCursorW(0, (nint)ArrowCursor),
                BackgroundBrush = backgroundBrush,
                ClassName = WindowClassName,
            };
            if (RegisterClassExW(ref windowClass) == 0)
            {
                _ = DeleteObject(backgroundBrush);
                return false;
            }

            _instance = instance;
            _backgroundBrush = backgroundBrush;
            Volatile.Write(ref _classRegistered, 1);
            return true;
        }
    }

    private static ControlHandles? CreateControls(nint window, nint instance, uint dpi)
    {
        int S(int px) => GenesisMainWindowLayout.Scale(px, dpi);
        var label = CreateControl(
            "STATIC",
            "再生 URL（pcp:// 等）",
            WindowStyleChild | WindowStyleVisible,
            S(22),
            S(14),
            S(448),
            S(22),
            window,
            LabelControlId,
            instance);
        var sourceEdit = CreateControl(
            "EDIT",
            string.Empty,
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop |
                EditStyleAutoHorizontalScroll,
            S(22),
            S(40),
            S(448),
            S(28),
            window,
            SourceControlId,
            instance,
            ExtendedStyleClientEdge);
        var error = CreateControl(
            "STATIC",
            string.Empty,
            WindowStyleChild | WindowStyleVisible,
            S(22),
            S(74),
            S(448),
            S(28),
            window,
            ErrorControlId,
            instance);
        var playButton = CreateControl(
            "BUTTON",
            "再生",
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            S(314),
            S(118),
            S(76),
            S(30),
            window,
            PlayControlId,
            instance);
        var cancelButton = CreateControl(
            "BUTTON",
            "キャンセル",
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop |
                ButtonStyleOwnerDraw,
            S(398),
            S(118),
            S(96),
            S(30),
            window,
            CancelControlId,
            instance);
        if (label == 0 || sourceEdit == 0 || error == 0 ||
            playButton == 0 || cancelButton == 0)
        {
            return null;
        }

        foreach (var control in new[] { label, sourceEdit, error, playButton, cancelButton })
        {
            _ = GenesisWindowUiFont.Apply(control);
        }

        _ = SendMessageW(
            sourceEdit,
            WindowMessageEditSetLimitText,
            (nuint)GenesisMainViewModel.MaximumSourceCharacters,
            0);
        return new(label, sourceEdit, error, playButton, cancelButton);
    }

    private static nint CreateControl(
        string className,
        string text,
        uint style,
        int left,
        int top,
        int width,
        int height,
        nint parent,
        int controlId,
        nint instance,
        uint extendedStyle = 0) =>
        CreateWindowExW(
            extendedStyle,
            className,
            text,
            style,
            left,
            top,
            width,
            height,
            parent,
            (nint)controlId,
            instance,
            0);

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (!StatesByWindow.TryGetValue(window, out var state))
        {
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageClose)
        {
            _ = DestroyWindow(window);
            return 0;
        }

        if (message == WindowMessageNcDestroy)
        {
            _ = StatesByWindow.TryRemove(window, out _);
            _ = StatesByOwner.TryRemove(state.Owner, out _);
            state.OnDestroyed();
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageCommand)
        {
            var controlId = (int)(wParam & 0xffff);
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (notificationCode == ButtonNotificationClicked &&
                controlId == PlayControlId)
            {
                state.SubmitSource();
                return 0;
            }

            if (controlId == CancelControlId)
            {
                _ = DestroyWindow(window);
                return 0;
            }
        }

        if (message == WindowMessagePlaybackCompleted)
        {
            state.CompleteSourcePlayback();
            return 0;
        }

        if (message == WindowMessageDrawItem && DialogState.DrawButton(lParam))
        {
            return 1;
        }

        if (message == WindowMessageControlColorStatic ||
            message == WindowMessageControlColorEdit)
        {
            return state.PrepareControlColor(
                wParam,
                lParam,
                message == WindowMessageControlColorEdit);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static nint ProcessKeyboardMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        if (message == WindowMessageKeyDown &&
            StatesByWindow.TryGetValue((nint)referenceData, out var state))
        {
            var virtualKey = (ushort)wParam;
            if (virtualKey == 0x1b)
            {
                _ = PostMessageW(
                    state.Window,
                    WindowMessageCommand,
                    (nuint)CancelControlId,
                    window);
                return 0;
            }

            if (virtualKey == 0x0d)
            {
                var command = window == state.CancelButton
                    ? CancelControlId
                    : PlayControlId;
                _ = PostMessageW(
                    state.Window,
                    WindowMessageCommand,
                    (nuint)command,
                    window);
                return 0;
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private sealed class DialogState
    {
        private readonly GenesisMainViewModel _viewModel;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly ControlHandles _controls;
        private readonly bool _ownerWasEnabled;
        private int _playInFlight;

        public DialogState(
            nint window,
            nint owner,
            GenesisMainViewModel viewModel,
            GenesisMainWindowInteraction interaction,
            ControlHandles controls,
            bool ownerWasEnabled)
        {
            Window = window;
            Owner = owner;
            _viewModel = viewModel;
            _interaction = interaction;
            _controls = controls;
            _ownerWasEnabled = ownerWasEnabled;
        }

        public nint Window { get; }

        public nint Owner { get; }

        public nint CancelButton => _controls.CancelButton;

        public bool OwnerWasEnabled => _ownerWasEnabled;

        public bool AttachKeyboard()
        {
            var controls = new[]
            {
                _controls.SourceEdit,
                _controls.PlayButton,
                _controls.CancelButton,
            };
            var attached = new List<nint>(controls.Length);
            foreach (var control in controls)
            {
                if (SetWindowSubclass(
                        control,
                        KeyboardSubclassProcedureDelegate,
                        KeyboardSubclassIdentifier,
                        (nuint)Window))
                {
                    attached.Add(control);
                    continue;
                }

                foreach (var prior in attached)
                {
                    _ = RemoveWindowSubclass(
                        prior,
                        KeyboardSubclassProcedureDelegate,
                        KeyboardSubclassIdentifier);
                }

                return false;
            }

            return true;
        }

        public void DetachKeyboard()
        {
            foreach (var control in new[]
            {
                _controls.SourceEdit,
                _controls.PlayButton,
                _controls.CancelButton,
            })
            {
                if (control != 0 && IsWindow(control))
                {
                    _ = RemoveWindowSubclass(
                        control,
                        KeyboardSubclassProcedureDelegate,
                        KeyboardSubclassIdentifier);
                }
            }
        }

        public void Activate()
        {
            if (IsWindow(Window))
            {
                _ = ShowWindow(Window, ShowWindowNormal);
                _ = SetForegroundWindow(Window);
                _ = SetFocus(_controls.SourceEdit);
            }
        }

        public void SubmitSource()
        {
            if (Volatile.Read(ref _playInFlight) != 0)
            {
                return;
            }

            var source = ReadWindowText(_controls.SourceEdit);
            var validationError = GetValidationError(source);
            if (validationError is not null)
            {
                _ = SetWindowTextW(_controls.ErrorLabel, validationError);
                _ = SetFocus(_controls.SourceEdit);
                return;
            }

            if (!_viewModel.CanPlay)
            {
                _ = SetWindowTextW(
                    _controls.ErrorLabel,
                    "Playback is not currently available.");
                return;
            }

            _ = SetWindowTextW(_controls.ErrorLabel, string.Empty);
            Interlocked.Exchange(ref _playInFlight, 1);
            _ = EnableWindow(_controls.SourceEdit, false);
            _ = EnableWindow(_controls.PlayButton, false);
            _ = ExecuteSourcePlaybackAsync(source);
        }

        public void CompleteSourcePlayback()
        {
            Interlocked.Exchange(ref _playInFlight, 0);
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var errorMessage = _viewModel.ErrorMessage;
            if (errorMessage is null &&
                _viewModel.PlaybackState is
                    PlaybackSessionState.Starting or PlaybackSessionState.Playing)
            {
                _ = DestroyWindow(Window);
                return;
            }

            _ = SetWindowTextW(
                _controls.ErrorLabel,
                errorMessage ?? "Playback request could not be started.");
            _ = EnableWindow(_controls.SourceEdit, true);
            _ = EnableWindow(_controls.PlayButton, true);
            _ = SetFocus(_controls.SourceEdit);
        }

        public static bool DrawButton(nint drawItemPointer)
        {
            if (drawItemPointer == 0)
            {
                return false;
            }

            var drawItem = Marshal.PtrToStructure<DrawItem>(drawItemPointer);
            if (drawItem.ControlType != DrawItemTypeButton ||
                drawItem.ControlId is not (PlayControlId or CancelControlId))
            {
                return false;
            }

            var isPlay = drawItem.ControlId == PlayControlId;
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            var pressed = (drawItem.ItemState & 0x0001) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                disabled
                    ? DialogBackground
                    : pressed
                        ? DialogBackground
                        : isPlay
                            ? PlayBackground
                            : CancelBackground);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 4;
            labelRectangle.Right -= 4;
            _ = DrawTextW(
                drawItem.DeviceContext,
                isPlay ? "再生" : "キャンセル",
                -1,
                ref labelRectangle,
                DrawTextSingleLine | DrawTextVerticalCenter | DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        public nint PrepareControlColor(
            nuint deviceContext,
            nint control,
            bool edit)
        {
            _ = SetBkMode((nint)deviceContext, edit ? 2 : TransparentBackgroundMode);
            _ = SetBkColor(
                (nint)deviceContext,
                edit ? EditBackground : DialogBackground);
            _ = SetTextColor(
                (nint)deviceContext,
                edit
                    ? TextColor
                    : control == _controls.ErrorLabel
                        ? ErrorColor
                        : TextColor);
            return _backgroundBrush;
        }

        public void OnDestroyed()
        {
            Volatile.Write(ref _disposed, 1);
            DetachKeyboard();
            if (_ownerWasEnabled && IsWindow(Owner))
            {
                _ = EnableWindow(Owner, true);
                _ = SetForegroundWindow(Owner);
            }
        }

        private async Task ExecuteSourcePlaybackAsync(string source)
        {
            try
            {
                await _interaction.PlaySourceAsync(source).ConfigureAwait(false);
            }
            catch
            {
                // The view model provides the existing bounded playback error state.
            }

            if (IsWindow(Window))
            {
                _ = PostMessageW(Window, WindowMessagePlaybackCompleted, 0, 0);
            }
        }

        private int _disposed;
    }

    private static string ReadWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(checked((length + 1) * sizeof(char)));
        try
        {
            var copied = GetWindowTextW(window, buffer, length + 1);
            return copied <= 0 ? string.Empty : Marshal.PtrToStringUni(buffer, copied);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void FillRectangle(nint deviceContext, Rectangle rectangle, int color)
    {
        var brush = CreateSolidBrush((uint)color);
        if (brush != 0)
        {
            _ = FillRect(deviceContext, ref rectangle, brush);
            _ = DeleteObject(brush);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControlHandles
    {
        public ControlHandles(
            nint label,
            nint sourceEdit,
            nint errorLabel,
            nint playButton,
            nint cancelButton)
        {
            Label = label;
            SourceEdit = sourceEdit;
            ErrorLabel = errorLabel;
            PlayButton = playButton;
            CancelButton = cancelButton;
        }

        public nint Label;
        public nint SourceEdit;
        public nint ErrorLabel;
        public nint PlayButton;
        public nint CancelButton;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
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
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
    private struct DrawItem
    {
        public uint ControlType;
        public uint ControlId;
        public uint ItemId;
        public uint ItemAction;
        public uint ItemState;
        public nint ControlWindow;
        public nint DeviceContext;
        public Rectangle ItemRectangle;
        public nuint ItemData;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint window, nint text, int maximumCharacters);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint window,
        SubclassProcedure subclassProcedure,
        nuint subclassIdentifier,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint window,
        SubclassProcedure subclassProcedure,
        nuint subclassIdentifier);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(nint deviceContext, int color);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(nint deviceContext, int color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DrawTextW(
        nint deviceContext,
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        int characterCount,
        ref Rectangle rectangle,
        uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawFocusRect(nint deviceContext, ref Rectangle rectangle);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint deviceContext, ref Rectangle rectangle, nint brush);

}

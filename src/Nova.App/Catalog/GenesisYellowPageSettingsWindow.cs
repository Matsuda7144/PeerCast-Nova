using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// Modeless Windows editor for the application-owned Yellow Page configuration.
/// Native controls only collect and project user input; validation and mutation
/// remain owned by <see cref="GenesisYellowPageSettingsModel"/>.
/// </summary>
internal sealed class GenesisYellowPageSettingsWindow : IAsyncDisposable
{
    private const uint WindowStyleCaption = 0x00C00000;
    private const uint WindowStyleSysMenu = 0x00080000;
    private const uint WindowStyleMinimizeBox = 0x00020000;
    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleTabStop = 0x00010000;
    private const uint WindowStyleVerticalScroll = 0x00200000;
    private const uint WindowStyleBorder = 0x00800000;
    private const uint WindowStyleClipChildren = 0x02000000;
    private const uint ListBoxStyleNotify = 0x0001;
    private const uint ListBoxStyleNoIntegralHeight = 0x0100;
    private const uint EditStyleAutoHorizontalScroll = 0x0080;
    private const uint ButtonStyleAutoCheckBox = 0x00000003;
    private const uint ExtendedStyleClientEdge = 0x00000200;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint ListBoxAddString = 0x0180;
    private const uint ListBoxResetContent = 0x0184;
    private const uint ListBoxSetCurrentSelection = 0x0186;
    private const uint ListBoxGetCurrentSelection = 0x0188;
    private const uint ButtonGetCheck = 0x00F0;
    private const uint ButtonSetCheck = 0x00F1;
    private const nuint VirtualKeyTab = 0x09;
    private const nuint VirtualKeyEnter = 0x0d;
    private const nuint VirtualKeyShift = 0x10;
    private const nuint VirtualKeyEscape = 0x1b;
    private const nuint KeyboardControlSubclassId = 1;
    private const int ButtonChecked = 1;
    private const int ButtonUnchecked = 0;
    private const int ButtonNotificationClicked = 0;
    private const int ListBoxNotificationSelectionChanged = 1;
    private const int ListBoxError = -1;
    private const int DefaultGuiFont = 17;
    private const int ArrowCursor = 32512;
    private const int ColorWindow = 5;
    private const int ShowWindowNormal = 1;
    private const int MessageBoxOk = 0x00000000;
    private const int MessageBoxYesNo = 0x00000004;
    private const int MessageBoxIconWarning = 0x00000030;
    private const int MessageBoxIconQuestion = 0x00000020;
    private const int MessageBoxResultYes = 6;
    private const int UseDefaultPosition = unchecked((int)0x80000000);
    private const int WindowWidth = 760;
    private const int WindowHeight = 410;
    private const int ListControlId = 1001;
    private const int IdControlId = 1002;
    private const int NameControlId = 1003;
    private const int IndexControlId = 1004;
    private const int TrackerControlId = 1005;
    private const int NamespaceControlId = 1006;
    private const int EnabledControlId = 1007;
    private const int NewControlId = 1008;
    private const int SaveControlId = 1009;
    private const int DeleteControlId = 1010;
    private const int CloseControlId = 1011;
    private static readonly int[] KeyboardControlIds =
    [
        ListControlId,
        IdControlId,
        NameControlId,
        IndexControlId,
        TrackerControlId,
        NamespaceControlId,
        EnabledControlId,
        NewControlId,
        SaveControlId,
        DeleteControlId,
        CloseControlId,
    ];
    private static readonly ConcurrentDictionary<nint, WindowState> StatesByWindow = new();
    private static readonly Win32WindowProcedure WindowProcedureDelegate = ProcessWindowMessage;
    private static readonly Win32SubclassProcedure KeyboardControlSubclassProcedureDelegate =
        ProcessKeyboardControlMessage;

    private readonly nint _window;
    private readonly string _className;
    private readonly nint _instance;
    private int _disposed;

    private GenesisYellowPageSettingsWindow(
        nint window,
        string className,
        nint instance)
    {
        _window = window;
        _className = className;
        _instance = instance;
    }

    internal enum KeyboardAction
    {
        None = 0,
        FocusNext = 1,
        FocusPrevious = 2,
        ActivateButton = 3,
        Close = 4,
    }

    public bool IsOpen =>
        Volatile.Read(ref _disposed) == 0 &&
        _window != 0 &&
        IsWindow(_window);

    public static GenesisYellowPageSettingsWindow? TryCreate(
        nint owner,
        GenesisYellowPageSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!OperatingSystem.IsWindows() || owner == 0 || !IsWindow(owner))
        {
            return null;
        }

        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            return null;
        }

        var className = $"PeerCastNovaYellowPages-{Guid.NewGuid():N}";
        var windowClass = new Win32WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<Win32WindowClassEx>(),
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
            "YP 設定",
            WindowStyleCaption |
                WindowStyleSysMenu |
                WindowStyleMinimizeBox |
                WindowStyleClipChildren,
            UseDefaultPosition,
            UseDefaultPosition,
            GenesisMainWindowLayout.Scale(WindowWidth, dpi),
            GenesisMainWindowLayout.Scale(WindowHeight, dpi),
            owner,
            0,
            instance,
            0);
        if (window == 0)
        {
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var controls = TryCreateControls(window, instance, dpi);
        if (controls is null || !TryAttachKeyboardNavigation(window))
        {
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        var state = new WindowState(window, controls, model);
        if (!StatesByWindow.TryAdd(window, state))
        {
            _ = DestroyWindow(window);
            _ = UnregisterClassW(className, instance);
            return null;
        }

        state.Initialize();
        _ = ShowWindow(window, ShowWindowNormal);
        _ = SetForegroundWindow(window);
        var initialFocus = GetDlgItem(window, ListControlId);
        if (initialFocus != 0)
        {
            _ = SetFocus(initialFocus);
        }

        return new(window, className, instance);
    }

    public void Activate()
    {
        if (!IsOpen)
        {
            return;
        }

        _ = ShowWindow(_window, ShowWindowNormal);
        _ = SetForegroundWindow(_window);
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

    internal static string BuildEntryLabel(GenesisYellowPageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var enabled = definition.IsEnabled ? "[x]" : "[ ]";
        return $"{enabled} {definition.Name} ({definition.Id})";
    }

    internal static KeyboardAction ResolveKeyboardAction(
        uint message,
        nuint virtualKey,
        bool actionButton,
        bool shiftPressed)
    {
        if (message != WindowMessageKeyDown)
        {
            return KeyboardAction.None;
        }

        if (virtualKey == VirtualKeyTab)
        {
            return shiftPressed
                ? KeyboardAction.FocusPrevious
                : KeyboardAction.FocusNext;
        }

        if (virtualKey == VirtualKeyEscape)
        {
            return KeyboardAction.Close;
        }

        return virtualKey == VirtualKeyEnter && actionButton
            ? KeyboardAction.ActivateButton
            : KeyboardAction.None;
    }

    private static bool TryAttachKeyboardNavigation(nint window)
    {
        foreach (var controlId in KeyboardControlIds)
        {
            var control = GetDlgItem(window, controlId);
            if (control == 0 ||
                !SetWindowSubclass(
                    control,
                    KeyboardControlSubclassProcedureDelegate,
                    KeyboardControlSubclassId,
                    unchecked((nuint)window)))
            {
                return false;
            }
        }

        return true;
    }

    private static nint ProcessKeyboardControlMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WindowMessageNcDestroy)
        {
            _ = RemoveWindowSubclass(
                window,
                KeyboardControlSubclassProcedureDelegate,
                subclassId);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        if (message != WindowMessageKeyDown)
        {
            return DefSubclassProc(window, message, wParam, lParam);
        }

        var parentWindow = unchecked((nint)referenceData);
        var controlId = GetDlgCtrlID(window);
        var shiftPressed =
            wParam == VirtualKeyTab && GetKeyState((int)VirtualKeyShift) < 0;
        var action = ResolveKeyboardAction(
            message,
            wParam,
            IsActionButton(controlId),
            shiftPressed);
        switch (action)
        {
            case KeyboardAction.FocusNext:
            case KeyboardAction.FocusPrevious:
                {
                    var next = GetNextDlgTabItem(
                        parentWindow,
                        window,
                        action == KeyboardAction.FocusPrevious);
                    if (next != 0)
                    {
                        _ = SetFocus(next);
                        return 0;
                    }

                    break;
                }
            case KeyboardAction.ActivateButton:
                if (controlId > 0 &&
                    PostMessageW(
                        parentWindow,
                        WindowMessageCommand,
                        (nuint)(uint)controlId,
                        window))
                {
                    return 0;
                }

                break;
            case KeyboardAction.Close:
                if (parentWindow != 0 &&
                    PostMessageW(parentWindow, WindowMessageClose, 0, 0))
                {
                    return 0;
                }

                break;
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static bool IsActionButton(int controlId) =>
        controlId is NewControlId or SaveControlId or DeleteControlId or CloseControlId;

    private static WindowControls? TryCreateControls(nint window, nint instance, uint dpi)
    {
        int S(int px) => GenesisMainWindowLayout.Scale(px, dpi);
        var list = CreateControl(
            ExtendedStyleClientEdge,
            "LISTBOX",
            string.Empty,
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                WindowStyleVerticalScroll |
                WindowStyleBorder |
                ListBoxStyleNotify |
                ListBoxStyleNoIntegralHeight,
            S(12),
            S(12),
            S(250),
            S(310),
            window,
            ListControlId,
            instance);
        if (list == 0)
        {
            return null;
        }

        if (CreateLabel(window, instance, "ID", S(282), S(15), S(105), S(22)) == 0)
        {
            return null;
        }

        var id = CreateEdit(window, instance, IdControlId, S(400), S(12), S(330), S(24));
        if (id == 0)
        {
            return null;
        }

        if (CreateLabel(window, instance, "YP 名", S(282), S(51), S(105), S(22)) == 0)
        {
            return null;
        }

        var name = CreateEdit(window, instance, NameControlId, S(400), S(48), S(330), S(24));
        if (name == 0)
        {
            return null;
        }

        if (CreateLabel(window, instance, "Index URL", S(282), S(87), S(105), S(22)) == 0)
        {
            return null;
        }

        var index = CreateEdit(window, instance, IndexControlId, S(400), S(84), S(330), S(24));
        if (index == 0)
        {
            return null;
        }

        if (CreateLabel(window, instance, "Tracker URL", S(282), S(123), S(112), S(22)) == 0)
        {
            return null;
        }

        var tracker = CreateEdit(window, instance, TrackerControlId, S(400), S(120), S(330), S(24));
        if (tracker == 0)
        {
            return null;
        }

        if (CreateLabel(window, instance, "Namespace", S(282), S(159), S(105), S(22)) == 0)
        {
            return null;
        }

        var yellowPageNamespace = CreateEdit(
            window,
            instance,
            NamespaceControlId,
            S(400),
            S(156),
            S(330),
            S(24));
        if (yellowPageNamespace == 0)
        {
            return null;
        }

        var enabled = CreateControl(
            0,
            "BUTTON",
            "有効",
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                ButtonStyleAutoCheckBox,
            S(400),
            S(194),
            S(120),
            S(24),
            window,
            EnabledControlId,
            instance);
        if (enabled == 0)
        {
            return null;
        }

        var status = CreateControl(
            0,
            "STATIC",
            "変更は次回のチャンネル一覧更新時に反映されます。",
            WindowStyleChild | WindowStyleVisible,
            S(282),
            S(232),
            S(448),
            S(42),
            window,
            0,
            instance);
        if (status == 0)
        {
            return null;
        }

        var newButton = CreateButton(
            window,
            instance,
            NewControlId,
            "新規",
            S(282),
            S(292),
            S(84),
            S(28));
        var saveButton = CreateButton(
            window,
            instance,
            SaveControlId,
            "保存",
            S(374),
            S(292),
            S(84),
            S(28));
        var deleteButton = CreateButton(
            window,
            instance,
            DeleteControlId,
            "削除",
            S(466),
            S(292),
            S(84),
            S(28));
        var closeButton = CreateButton(
            window,
            instance,
            CloseControlId,
            "閉じる",
            S(646),
            S(292),
            S(84),
            S(28));
        if (newButton == 0 ||
            saveButton == 0 ||
            deleteButton == 0 ||
            closeButton == 0)
        {
            return null;
        }

        return new(
            list,
            id,
            name,
            index,
            tracker,
            yellowPageNamespace,
            enabled,
            status,
            deleteButton);
    }

    private static nint CreateLabel(
        nint parent,
        nint instance,
        string text,
        int x,
        int y,
        int width,
        int height) =>
        CreateControl(
            0,
            "STATIC",
            text,
            WindowStyleChild | WindowStyleVisible,
            x,
            y,
            width,
            height,
            parent,
            0,
            instance);

    private static nint CreateEdit(
        nint parent,
        nint instance,
        int controlId,
        int x,
        int y,
        int width,
        int height) =>
        CreateControl(
            ExtendedStyleClientEdge,
            "EDIT",
            string.Empty,
            WindowStyleChild |
                WindowStyleVisible |
                WindowStyleTabStop |
                EditStyleAutoHorizontalScroll,
            x,
            y,
            width,
            height,
            parent,
            controlId,
            instance);

    private static nint CreateButton(
        nint parent,
        nint instance,
        int controlId,
        string text,
        int x,
        int y,
        int width,
        int height) =>
        CreateControl(
            0,
            "BUTTON",
            text,
            WindowStyleChild | WindowStyleVisible | WindowStyleTabStop,
            x,
            y,
            width,
            height,
            parent,
            controlId,
            instance);

    private static nint CreateControl(
        uint extendedStyle,
        string className,
        string text,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        int controlId,
        nint instance)
    {
        var control = CreateWindowExW(
            extendedStyle,
            className,
            text,
            style,
            x,
            y,
            width,
            height,
            parent,
            (nint)controlId,
            instance,
            0);
        if (control != 0)
        {
            ApplyDefaultFont(control);
        }

        return control;
    }

    private static void ApplyDefaultFont(nint control) => _ = GenesisWindowUiFont.Apply(control);

    private static nint ProcessWindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == WindowMessageClose)
        {
            _ = DestroyWindow(window);
            return 0;
        }

        if (message == WindowMessageNcDestroy)
        {
            _ = StatesByWindow.TryRemove(window, out _);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WindowMessageCommand &&
            StatesByWindow.TryGetValue(window, out var state))
        {
            state.ProcessCommand(wParam);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class WindowState
    {
        private readonly nint _window;
        private readonly WindowControls _controls;
        private readonly GenesisYellowPageSettingsModel _model;
        private ReadOnlyCollection<GenesisYellowPageDefinition> _entries =
            Array.AsReadOnly(Array.Empty<GenesisYellowPageDefinition>());
        private string? _editingId;

        public WindowState(
            nint window,
            WindowControls controls,
            GenesisYellowPageSettingsModel model)
        {
            _window = window;
            _controls = controls;
            _model = model;
        }

        public void Initialize()
        {
            RefreshList(preferredId: null);
        }

        public void ProcessCommand(nuint wParam)
        {
            var controlId = (int)(wParam & 0xffff);
            var notificationCode = (int)((wParam >> 16) & 0xffff);
            if (controlId == ListControlId &&
                notificationCode == ListBoxNotificationSelectionChanged)
            {
                LoadSelectedEntry();
                return;
            }

            if (notificationCode != ButtonNotificationClicked)
            {
                return;
            }

            switch (controlId)
            {
                case NewControlId:
                    BeginNew();
                    _ = SetFocus(_controls.Id);
                    break;
                case SaveControlId:
                    Save();
                    break;
                case DeleteControlId:
                    Delete();
                    break;
                case CloseControlId:
                    _ = DestroyWindow(_window);
                    break;
            }
        }

        private void RefreshList(string? preferredId)
        {
            _entries = _model.Entries;
            _ = SendMessageW(_controls.List, ListBoxResetContent, 0, 0);
            var selectedIndex = ListBoxError;
            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                _ = SendMessageTextW(
                    _controls.List,
                    ListBoxAddString,
                    0,
                    BuildEntryLabel(entry));
                if (preferredId is not null &&
                    string.Equals(entry.Id, preferredId, StringComparison.Ordinal))
                {
                    selectedIndex = index;
                }
            }

            if (selectedIndex == ListBoxError && _entries.Count > 0)
            {
                selectedIndex = 0;
            }

            if (selectedIndex == ListBoxError)
            {
                BeginNew();
                return;
            }

            _ = SendMessageW(
                _controls.List,
                ListBoxSetCurrentSelection,
                (nuint)selectedIndex,
                0);
            LoadEntry(_entries[selectedIndex]);
        }

        private void LoadSelectedEntry()
        {
            var selected = unchecked((int)SendMessageW(
                _controls.List,
                ListBoxGetCurrentSelection,
                0,
                0));
            if (selected < 0 || selected >= _entries.Count)
            {
                return;
            }

            LoadEntry(_entries[selected]);
        }

        private void LoadEntry(GenesisYellowPageDefinition definition)
        {
            _editingId = definition.Id;
            SetText(_controls.Id, definition.Id);
            SetText(_controls.Name, definition.Name);
            SetText(_controls.Index, definition.IndexUri.AbsoluteUri);
            SetText(
                _controls.Tracker,
                definition.FallbackTrackerUri?.AbsoluteUri ?? string.Empty);
            SetText(_controls.Namespace, definition.Namespace ?? string.Empty);
            SetChecked(_controls.Enabled, definition.IsEnabled);
            _ = EnableWindow(_controls.Id, false);
            _ = EnableWindow(_controls.DeleteButton, true);
            SetStatus("Editing an existing Yellow Page. Its id is stable.");
        }

        private void BeginNew()
        {
            _editingId = null;
            _ = SendMessageW(
                _controls.List,
                ListBoxSetCurrentSelection,
                unchecked((nuint)ListBoxError),
                0);
            SetText(_controls.Id, string.Empty);
            SetText(_controls.Name, string.Empty);
            SetText(_controls.Index, string.Empty);
            SetText(_controls.Tracker, string.Empty);
            SetText(_controls.Namespace, string.Empty);
            SetChecked(_controls.Enabled, true);
            _ = EnableWindow(_controls.Id, true);
            _ = EnableWindow(_controls.DeleteButton, false);
            SetStatus("Enter a new Yellow Page and choose Save.");
        }

        private void Save()
        {
            var draft = new GenesisYellowPageSettingsDraft(
                GetText(_controls.Id),
                GetText(_controls.Name),
                GetText(_controls.Index),
                GetText(_controls.Tracker),
                IsChecked(_controls.Enabled),
                GetText(_controls.Namespace));
            var result = _editingId is null
                ? _model.Add(draft)
                : _model.Edit(_editingId, draft);
            if (!result.Succeeded)
            {
                ShowError(result.ErrorMessage);
                return;
            }

            var savedId = draft.Id?.Trim();
            RefreshList(savedId);
            SetStatus("Saved. Changes apply on the next catalog refresh.");
        }

        private void Delete()
        {
            if (_editingId is null)
            {
                return;
            }

            var selected = _entries.FirstOrDefault(entry =>
                string.Equals(entry.Id, _editingId, StringComparison.Ordinal));
            var label = selected?.Name ?? _editingId;
            var answer = MessageBoxW(
                _window,
                $"Delete Yellow Page '{label}'?",
                "YP 設定",
                MessageBoxYesNo | MessageBoxIconQuestion);
            if (answer != MessageBoxResultYes)
            {
                return;
            }

            var result = _model.Remove(_editingId);
            if (!result.Succeeded)
            {
                ShowError(result.ErrorMessage);
                return;
            }

            RefreshList(preferredId: null);
            SetStatus("Deleted. Changes apply on the next catalog refresh.");
        }

        private void SetStatus(string text)
        {
            SetText(_controls.Status, text);
        }

        private void ShowError(string? message)
        {
            _ = MessageBoxW(
                _window,
                message ?? "The Yellow Page settings could not be applied.",
                "YP 設定",
                MessageBoxOk | MessageBoxIconWarning);
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
        return copied <= 0
            ? string.Empty
            : new string(buffer, 0, copied);
    }

    private static void SetText(nint control, string text)
    {
        _ = SetWindowTextW(control, text);
    }

    private static bool IsChecked(nint control) =>
        unchecked((int)SendMessageW(control, ButtonGetCheck, 0, 0)) == ButtonChecked;

    private static void SetChecked(nint control, bool isChecked)
    {
        _ = SendMessageW(
            control,
            ButtonSetCheck,
            (nuint)(isChecked ? ButtonChecked : ButtonUnchecked),
            0);
    }

    private sealed record WindowControls(
        nint List,
        nint Id,
        nint Name,
        nint Index,
        nint Tracker,
        nint Namespace,
        nint Enabled,
        nint Status,
        nint DeleteButton);
}

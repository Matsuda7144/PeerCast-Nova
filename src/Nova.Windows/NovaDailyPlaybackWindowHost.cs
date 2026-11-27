using System.Runtime.InteropServices;
using static PeerCast.Nova.Windows.Win32WindowNativeMethods;

namespace PeerCast.Nova.Windows;

public enum NovaDailyPlaybackWindowResult
{
    None,
    Started,
    PlatformUnsupported,
    Cancelled,
    Stopped,
    Faulted,
}

/// <summary>
/// Owns the small channel-select / Play / Stop / Relay Windows shell used by
/// Nova's ordinary playback and PCP relay routes.
/// </summary>
public sealed class NovaDailyPlaybackWindowHost : IAsyncDisposable
{
    private const uint ClassStyleOwnDeviceContext = 0x0020;
    private const uint WindowStyleOverlapped = 0x00CF0000;
    private const uint ChildVisible = 0x50000000;
    private const uint ChildVisibleTabStop = 0x50010000;
    private const uint ComboDropDownList = 0x0003;
    private const uint ComboVerticalScroll = 0x0040;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessageSetText = 0x000C;
    private const uint WindowMessageSetFont = 0x0030;
    private const int ComboSelectionChanged = 1;
    private const uint ComboResetContent = 0x014B;
    private const uint ComboAddString = 0x0143;
    private const uint ComboSetCurrentSelection = 0x014E;
    private const uint ComboGetCurrentSelection = 0x0147;
    private const int ButtonClicked = 0;
    private const int RefreshControlId = 1001;
    private const int PlayControlId = 1002;
    private const int StopControlId = 1003;
    private const int ChannelControlId = 1004;
    private const int StatusControlId = 1005;
    private const int RelayControlId = 1006;
    private const int StopRelayControlId = 1007;
    private const int RelayEndpointControlId = 1008;
    private const int PortMappingControlId = 1009;
    private const int PortMappingStatusControlId = 1010;
    private const int PortMappingDiagnosticsControlId = 1011;
    private const int PortMappingDiagnosticsTitleControlId = 1012;
    private const uint PortMappingUpdateMessage = 0x8001;
    private const uint ButtonGetCheck = 0x00F0;
    private const uint ButtonSetCheck = 0x00F1;
    private const int CursorArrow = 32512;
    private const int DefaultGuiFont = 17;

    private readonly object _gate = new();
    private readonly string _title;
    private readonly TaskCompletionSource<NovaDailyPlaybackWindowResult> _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<NovaDailyPlaybackWindowResult> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowProcedure _windowProcedure;
    private Thread? _thread;
    private nint _window;
    private nint _channelCombo;
    private nint _statusLabel;
    private nint _relayEndpointLabel;
    private string[] _channelNames = [];
    private bool _closeRequested;
    private bool _disposed;
    private NovaDailyPlaybackWindowResult _terminalResult;
    private (bool Enabled, string Summary, string Diagnostics) _portMappingView;
    private string? _shownDiagnostics;
    private long _portMappingGeneration = -1;

    public NovaDailyPlaybackWindowHost(string title = "PeerCast Nova")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title = title;
        _windowProcedure = ProcessWindowMessage;
    }

    public event Action? RefreshRequested;

    public event Action<int>? PlayRequested;

    public event Action? StopRequested;

    public event Action<int>? RelayRequested;

    public event Action? StopRelayRequested;

    public event Action? ChannelSelectionChanged;

    public event Action<bool>? PortMappingEnabledChanged;

    public void SetPortMapping(long generation, bool enabled, string summary, string diagnostics)
    {
        lock (_gate)
        {
            if (generation < _portMappingGeneration)
            {
                return;
            }
            _portMappingGeneration = generation;
            _portMappingView = (enabled, summary, diagnostics);
        }
        var window = Volatile.Read(ref _window);
        if (window != 0)
        {
            // Post rather than synchronously send across threads. Router waits,
            // persistence and projection never block the native message loop.
            _ = PostMessageW(window, PortMappingUpdateMessage, 0, 0);
        }
    }

    public async ValueTask<NovaDailyPlaybackWindowResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                throw new InvalidOperationException("The daily playback window can only be started once.");
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17_763))
            {
                return NovaDailyPlaybackWindowResult.PlatformUnsupported;
            }

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "PeerCast Nova daily playback window",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        try
        {
            return await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestClose();
            await _stopped.Task.ConfigureAwait(false);
            return NovaDailyPlaybackWindowResult.Cancelled;
        }
    }

    public ValueTask<NovaDailyPlaybackWindowResult> WaitForExitAsync(
        CancellationToken cancellationToken = default) =>
        new(_stopped.Task.WaitAsync(cancellationToken));

    public int SelectedChannelIndex
    {
        get
        {
            var combo = Volatile.Read(ref _channelCombo);
            if (combo == 0)
            {
                return -1;
            }

            var selected = SendMessageW(combo, ComboGetCurrentSelection, 0, 0).ToInt64();
            return selected is >= 0 and <= int.MaxValue ? (int)selected : -1;
        }
    }

    public void SetChannels(IReadOnlyList<string> channelNames)
    {
        ArgumentNullException.ThrowIfNull(channelNames);
        var names = channelNames
            .Select(static name => string.IsNullOrWhiteSpace(name) ? "(unnamed channel)" : name)
            .ToArray();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _channelNames = names;
        }

        var combo = Volatile.Read(ref _channelCombo);
        if (combo == 0 || !IsWindow(combo))
        {
            return;
        }

        _ = SendMessageW(combo, ComboResetContent, 0, 0);
        foreach (var name in names)
        {
            _ = SendMessageW(combo, ComboAddString, 0, name);
        }

        if (names.Length > 0)
        {
            _ = SendMessageW(combo, ComboSetCurrentSelection, 0, 0);
        }

        _ = EnableWindow(GetControl(PlayControlId), names.Length > 0);
        _ = EnableWindow(GetControl(RelayControlId), names.Length > 0);
    }

    public void SetStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var label = Volatile.Read(ref _statusLabel);
        if (label != 0 && IsWindow(label))
        {
            _ = SendMessageW(label, WindowMessageSetText, 0, status);
        }
    }

    public void SetRelayEndpoint(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var label = Volatile.Read(ref _relayEndpointLabel);
        if (label != 0 && IsWindow(label))
        {
            var text = string.IsNullOrWhiteSpace(endpoint)
                ? "Relay endpoint: stopped"
                : $"Relay endpoint (share with a LAN PeerCast peer):\r\n{endpoint}";
            _ = SendMessageW(label, WindowMessageSetText, 0, text);
        }
    }

    public void ShowError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        SetStatus(message);
        var window = Volatile.Read(ref _window);
        if (window != 0 && IsWindow(window))
        {
            _ = MessageBoxW(window, message, _title, 0x00000010 | 0x00000000);
        }
    }

    public void RequestClose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _closeRequested = true;
        }

        var window = Volatile.Read(ref _window);
        if (window != 0 && IsWindow(window))
        {
            _ = PostMessageW(window, WindowMessageClose, 0, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        RequestClose();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_thread is not null)
        {
            await _stopped.Task.ConfigureAwait(false);
        }
    }

    private void ThreadMain()
    {
        try
        {
            ThreadMainCore();
        }
        catch
        {
            Complete(NovaDailyPlaybackWindowResult.Faulted);
        }
        finally
        {
            lock (_gate)
            {
                _stopped.TrySetResult(_terminalResult);
            }
        }
    }

    private void ThreadMainCore()
    {
        var instance = GetModuleHandleW(null);
        if (instance == 0)
        {
            Complete(NovaDailyPlaybackWindowResult.Faulted);
            return;
        }

        var className = $"PeerCastNovaDailyPlayback-{Guid.NewGuid():N}";
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = ClassStyleOwnDeviceContext,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = instance,
            Cursor = LoadCursorW(0, (nint)CursorArrow),
            ClassName = className,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            Complete(NovaDailyPlaybackWindowResult.Faulted);
            return;
        }

        nint window = 0;
        try
        {
            var rectangle = new WindowRectangle { Right = 740, Bottom = 660 };
            if (!AdjustWindowRectEx(ref rectangle, WindowStyleOverlapped, false, 0))
            {
                Complete(NovaDailyPlaybackWindowResult.Faulted);
                return;
            }

            window = CreateWindowExW(
                0,
                className,
                _title,
                WindowStyleOverlapped,
                int.MinValue,
                int.MinValue,
                rectangle.Right - rectangle.Left,
                rectangle.Bottom - rectangle.Top,
                0,
                0,
                instance,
                0);
            if (window == 0)
            {
                Complete(NovaDailyPlaybackWindowResult.Faulted);
                return;
            }

            Volatile.Write(ref _window, window);
            CreateControls(window, instance);
            _ = ShowWindow(window, 1);
            _ = UpdateWindow(window);
            _started.TrySetResult(NovaDailyPlaybackWindowResult.Started);

            lock (_gate)
            {
                if (_closeRequested)
                {
                    _ = PostMessageW(window, WindowMessageClose, 0, 0);
                }
            }

            while (true)
            {
                var messageResult = GetMessageW(out var message, 0, 0, 0);
                if (messageResult <= 0)
                {
                    Complete(messageResult == 0
                        ? NovaDailyPlaybackWindowResult.Stopped
                        : NovaDailyPlaybackWindowResult.Faulted);
                    return;
                }

                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }
        catch
        {
            Complete(NovaDailyPlaybackWindowResult.Faulted);
        }
        finally
        {
            Volatile.Write(ref _window, 0);
            Volatile.Write(ref _channelCombo, 0);
            Volatile.Write(ref _statusLabel, 0);
            Volatile.Write(ref _relayEndpointLabel, 0);
            if (window != 0 && IsWindow(window))
            {
                _ = DestroyWindow(window);
            }

            _ = UnregisterClassW(className, instance);
        }
    }

    private void CreateControls(nint window, nint instance)
    {
        var font = GetStockObject(DefaultGuiFont);
        _statusLabel = CreateControl(
            window,
            instance,
            "STATIC",
            "チャンネルを選択して再生してください。",
            ChildVisible,
            12,
            14,
            700,
            24,
            StatusControlId);
        _channelCombo = CreateControl(
            window,
            instance,
            "COMBOBOX",
            string.Empty,
            ChildVisibleTabStop | ComboDropDownList | ComboVerticalScroll,
            12,
            48,
            560,
            320,
            ChannelControlId);
        _ = CreateControl(window, instance, "BUTTON", "更新", ChildVisibleTabStop, 586, 48, 62, 28, RefreshControlId);
        _ = CreateControl(window, instance, "BUTTON", "再生", ChildVisibleTabStop, 12, 98, 82, 30, PlayControlId);
        _ = CreateControl(window, instance, "BUTTON", "停止", ChildVisibleTabStop, 104, 98, 82, 30, StopControlId);
        _ = CreateControl(window, instance, "BUTTON", "Relay開始", ChildVisibleTabStop, 12, 140, 100, 30, RelayControlId);
        _ = CreateControl(window, instance, "BUTTON", "Relay停止", ChildVisibleTabStop, 122, 140, 100, 30, StopRelayControlId);
        _relayEndpointLabel = CreateControl(
            window,
            instance,
            "STATIC",
            "Relay endpoint: stopped",
            ChildVisible,
            12,
            180,
            700,
            68,
            RelayEndpointControlId);
        _ = EnableWindow(GetControl(PlayControlId), false);
        _ = CreateControl(window, instance, "BUTTON", "ポート自動開放 (ON / OFF)",
            ChildVisibleTabStop | 0x0003, 12, 256, 400, 28, PortMappingControlId);
        _ = CreateControl(window, instance, "STATIC", "ポート自動開放: OFF / 外部接続: 未確認",
            ChildVisible, 12, 290, 710, 150, PortMappingStatusControlId);
        _ = CreateControl(window, instance, "STATIC", "ポート診断（観測時刻 / 診断コード / 要約）",
            ChildVisible, 12, 440, 710, 20, PortMappingDiagnosticsTitleControlId);
        _ = CreateControl(window, instance, "EDIT", "",
            ChildVisibleTabStop | 0x00800000 | 0x00200000 | 0x0004 | 0x0040 | 0x0800,
            12, 466, 710, 178, PortMappingDiagnosticsControlId);

        foreach (var controlId in new[]
                 {
                     StatusControlId,
                     ChannelControlId,
                     RefreshControlId,
                     PlayControlId,
                     StopControlId,
                     RelayControlId,
                     StopRelayControlId,
                     RelayEndpointControlId,
                     PortMappingControlId,
                     PortMappingStatusControlId,
                     PortMappingDiagnosticsControlId,
                     PortMappingDiagnosticsTitleControlId,
                 })
        {
            _ = SendMessageW(
                GetControl(controlId),
                WindowMessageSetFont,
                unchecked((nuint)font),
                1);
        }

        string[] names;
        lock (_gate)
        {
            names = [.. _channelNames];
        }

        SetChannels(names);
    }

    private static nint CreateControl(
        nint parent,
        nint instance,
        string className,
        string text,
        uint style,
        int x,
        int y,
        int width,
        int height,
        int controlId) =>
        CreateWindowExW(
            0,
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

    private nint GetControl(int controlId) =>
        Volatile.Read(ref _window) is var window && window != 0
            ? GetDlgItem(window, controlId)
            : 0;

    private nint ProcessWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == PortMappingUpdateMessage)
        {
            (bool Enabled, string Summary, string Diagnostics) view;
            lock (_gate)
            {
                view = _portMappingView;
            }
            _ = SendMessageW(GetControl(PortMappingControlId), ButtonSetCheck, view.Enabled ? 1u : 0u, 0);
            _ = SendMessageW(GetControl(PortMappingStatusControlId), WindowMessageSetText, 0, view.Summary);
            if (_shownDiagnostics != view.Diagnostics)
            {
                _shownDiagnostics = view.Diagnostics;
                _ = SendMessageW(GetControl(PortMappingDiagnosticsControlId), WindowMessageSetText, 0, view.Diagnostics);
            }
            return 0;
        }
        if (message == WindowMessageCommand)
        {
            var controlId = unchecked((int)(wParam & 0xffff));
            var notification = unchecked((int)((wParam >> 16) & 0xffff));
            if (notification == ButtonClicked)
            {
                switch (controlId)
                {
                    case PortMappingControlId:
                        var enabled = SendMessageW(GetControl(PortMappingControlId), ButtonGetCheck, 0, 0) != 0;
                        PortMappingEnabledChanged?.Invoke(enabled);
                        return 0;
                    case RefreshControlId:
                        RefreshRequested?.Invoke();
                        return 0;
                    case PlayControlId:
                        var index = SelectedChannelIndex;
                        if (index >= 0)
                        {
                            PlayRequested?.Invoke(index);
                        }

                        return 0;
                    case StopControlId:
                        StopRequested?.Invoke();
                        return 0;
                    case RelayControlId:
                        var relayIndex = SelectedChannelIndex;
                        if (relayIndex >= 0)
                        {
                            RelayRequested?.Invoke(relayIndex);
                        }

                        return 0;
                    case StopRelayControlId:
                        StopRelayRequested?.Invoke();
                        return 0;
                }
            }

            if (controlId == ChannelControlId &&
                notification == ComboSelectionChanged)
            {
                ChannelSelectionChanged?.Invoke();
                return 0;
            }
        }

        if (message == WindowMessageClose)
        {
            _ = DestroyWindow(window);
            return 0;
        }

        if (message == WindowMessageDestroy)
        {
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void Complete(NovaDailyPlaybackWindowResult result)
    {
        lock (_gate)
        {
            _terminalResult = result;
        }

        _started.TrySetResult(result);
    }
}

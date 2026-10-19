using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal static class GenesisSourceKeyboard
{
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyF5 = 0x74;

    public static bool IsPlayGesture(ushort virtualKey) =>
        virtualKey == VirtualKeyEnter;

    public static bool IsStopGesture(ushort virtualKey) =>
        virtualKey == VirtualKeyEscape;

    public static bool IsRefreshGesture(ushort virtualKey) =>
        virtualKey == VirtualKeyF5;
}

internal sealed class GenesisSourceEnterPlacement
{
    private readonly nint _sourceEdit;

    private GenesisSourceEnterPlacement(nint sourceEdit)
    {
        _sourceEdit = sourceEdit;
    }

    public static GenesisSourceEnterPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var sourceEdit = FindWindowExW((nint)windowToken, 0, "EDIT", null);
        return sourceEdit == 0 ? null : new(sourceEdit);
    }

    public IAsyncDisposable? Open(GenesisMainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return GenesisSourceEnterBridge.TryCreate(_sourceEdit, viewModel);
    }

    public IAsyncDisposable? Open(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession channelCatalogSession)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(channelCatalogSession);
        return GenesisSourceEnterBridge.TryCreate(
            _sourceEdit,
            viewModel,
            channelCatalogSession);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);
}

internal sealed class GenesisSourceEnterBridge : IAsyncDisposable
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageChar = 0x0102;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const nuint SubclassIdentifier = 0x7144;
    private const nuint CarriageReturn = 0x0D;
    private const nuint EscapeCharacter = 0x1B;
    private static readonly ConcurrentDictionary<nint, SourceState> StatesByEdit = new();
    private static readonly SubclassProcedure SubclassProcedureDelegate = ProcessSubclassMessage;
    private readonly nint _sourceEdit;
    private int _disposed;

    private GenesisSourceEnterBridge(nint sourceEdit)
    {
        _sourceEdit = sourceEdit;
    }

    public static GenesisSourceEnterBridge? TryCreate(
        nint sourceEdit,
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession? channelCatalogSession = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (sourceEdit == 0 || !IsWindow(sourceEdit))
        {
            return null;
        }

        var state = new SourceState(
            sourceEdit,
            viewModel,
            channelCatalogSession);
        if (!StatesByEdit.TryAdd(sourceEdit, state))
        {
            return null;
        }

        if (!SetWindowSubclass(
                sourceEdit,
                SubclassProcedureDelegate,
                SubclassIdentifier,
                0))
        {
            _ = StatesByEdit.TryRemove(sourceEdit, out _);
            return null;
        }

        return new(sourceEdit);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _ = StatesByEdit.TryRemove(_sourceEdit, out _);
            if (_sourceEdit != 0 && IsWindow(_sourceEdit))
            {
                _ = RemoveWindowSubclass(
                    _sourceEdit,
                    SubclassProcedureDelegate,
                    SubclassIdentifier);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static nint ProcessSubclassMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        if (message == WindowMessageNcDestroy)
        {
            _ = StatesByEdit.TryRemove(window, out _);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        if (StatesByEdit.TryGetValue(window, out var state))
        {
            if (message == WindowMessageKeyDown)
            {
                var virtualKey = (ushort)wParam;
                if (GenesisSourceKeyboard.IsPlayGesture(virtualKey))
                {
                    state.PlayCurrentSource();
                    return 0;
                }

                if (GenesisSourceKeyboard.IsStopGesture(virtualKey))
                {
                    state.StopPlayback();
                    return 0;
                }

                if (GenesisSourceKeyboard.IsRefreshGesture(virtualKey) &&
                    state.CanRefreshChannels)
                {
                    state.RefreshChannels();
                    return 0;
                }
            }

            if (message == WindowMessageChar &&
                (wParam == CarriageReturn || wParam == EscapeCharacter))
            {
                return 0;
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static string ReadWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var characterCapacity = checked(length + 1);
        var byteCapacity = checked(characterCapacity * sizeof(char));
        var buffer = Marshal.AllocHGlobal(byteCapacity);
        try
        {
            var copied = GetWindowTextW(window, buffer, characterCapacity);
            return copied <= 0
                ? string.Empty
                : Marshal.PtrToStringUni(buffer, copied);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed class SourceState
    {
        private readonly nint _sourceEdit;
        private readonly GenesisMainWindowInteraction _interaction;
        private readonly GenesisInteractionCommandLanes _commandLanes = new();

        public SourceState(
            nint sourceEdit,
            GenesisMainViewModel viewModel,
            GenesisChannelCatalogSession? channelCatalogSession)
        {
            _sourceEdit = sourceEdit;
            _interaction = new(viewModel, channelCatalogSession);
        }

        public bool CanRefreshChannels => _interaction.CanRefreshCurrentView;

        public void PlayCurrentSource()
        {
            if (_interaction.IsRecordingHistoryView) return;
            if (!_commandLanes.CanStartPlayback)
            {
                return;
            }

            _commandLanes.TrackPlayback(ObserveCommandAsync(
                _interaction.PlaySourceAsync(ReadWindowText(_sourceEdit))));
        }

        public void StopPlayback()
        {
            if (_interaction.IsRecordingHistoryView) return;
            if (!_commandLanes.CanStartStop)
            {
                return;
            }

            _commandLanes.TrackStop(ObserveCommandAsync(_interaction.StopAsync()));
        }

        public void RefreshChannels()
        {
            if (!_commandLanes.CanStartRefresh || !CanRefreshChannels)
            {
                return;
            }

            _commandLanes.TrackRefresh(ObserveCommandAsync(
                _interaction.RefreshCurrentViewAsync()));
        }

        private static async Task ObserveCommandAsync(ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch
            {
                // Source keyboard command failures are converted to bounded GUI state.
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData);

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

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(
        nint window,
        nint text,
        int maximumCharacters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}

using System.Runtime.InteropServices;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed class GenesisGuiNextAppShellPlacement
{
    internal const int SidebarWidth = 160;
    internal const int SidebarGap = 12;
    internal const int StatusHeight = 28;
    internal const int StatusGap = 4;
    internal const int MinimumListHeight = 80;
    internal const int MinimumDetailsHeight = 140;
    internal const int DetailsHeight = 224;
    internal const int DetailsGap = 8;
    private readonly nint _owner;
    private readonly nint _listView;
    private readonly nint _sourceLabel;
    private readonly nint _sourceEdit;
    private readonly nint _legacyPlayButton;
    private readonly nint _legacyStopButton;

    private GenesisGuiNextAppShellPlacement(
        nint owner,
        nint listView,
        nint sourceLabel,
        nint sourceEdit,
        nint legacyPlayButton,
        nint legacyStopButton)
    {
        _owner = owner;
        _listView = listView;
        _sourceLabel = sourceLabel;
        _sourceEdit = sourceEdit;
        _legacyPlayButton = legacyPlayButton;
        _legacyStopButton = legacyStopButton;
    }

    public static GenesisGuiNextAppShellPlacement? Capture(long windowToken)
    {
        if (windowToken == 0)
        {
            return null;
        }

        var owner = (nint)windowToken;
        return !GenesisMainWindowControlRegistry.TryResolve(owner, out var handles) ||
            handles.ChannelList == 0 ||
            handles.SourceLabel == 0 ||
            handles.SourceEdit == 0 ||
            handles.PlayButton == 0 ||
            handles.StopButton == 0
                ? null
                : new(
                    owner,
                    handles.ChannelList,
                    handles.SourceLabel,
                    handles.SourceEdit,
                    handles.PlayButton,
                    handles.StopButton);
    }

    public IAsyncDisposable? Open(
        GenesisGuiNextTabsViewModel tabs,
        GenesisMainViewModel viewModel,
        GenesisMainWindowInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        var parent = GetParent(_listView);
        if (parent == 0 ||
            !TryCaptureCurrentRectangle(parent, out var currentRectangle) ||
            !TryCaptureRectangle(_sourceLabel, parent, out var sourceLabelRectangle) ||
            !TryResolveLayout(
                currentRectangle,
                sourceLabelRectangle.Top,
                out var sidebarRectangle,
                out var statusRectangle,
                out var movedListRectangle,
                out var detailsRectangle) ||
            !MoveWindow(
                _listView,
                movedListRectangle.Left,
                movedListRectangle.Top,
                movedListRectangle.Width,
                movedListRectangle.Height,
                true))
        {
            return null;
        }

        var bridge = GenesisGuiNextAppShellBridge.TryCreate(
            _owner,
            parent,
            _listView,
            currentRectangle,
            sidebarRectangle,
            statusRectangle,
            detailsRectangle,
            tabs,
            viewModel,
            interaction,
            CaptureLegacyControls());
        if (bridge is null)
        {
            _ = RestoreListView(currentRectangle);
            return null;
        }

        bridge.HideLegacyControls();
        _ = SetFocus(_listView);
        return bridge;
    }

    private GenesisGuiNextAppShellBridge.LegacyControlVisibility[] CaptureLegacyControls() =>
    [
        new(_sourceLabel, IsWindowVisible(_sourceLabel)),
        new(_sourceEdit, IsWindowVisible(_sourceEdit)),
        new(_legacyPlayButton, IsWindowVisible(_legacyPlayButton)),
        new(_legacyStopButton, IsWindowVisible(_legacyStopButton)),
    ];

    internal static bool TryResolveLayout(
        WindowRectangle listRectangle,
        out WindowRectangle sidebarRectangle,
        out WindowRectangle statusRectangle,
        out WindowRectangle movedListRectangle) =>
        TryResolveLayout(
            listRectangle,
            listRectangle.Top,
            out sidebarRectangle,
            out statusRectangle,
            out movedListRectangle,
            out _);

    internal static bool TryResolveLayout(
        WindowRectangle listRectangle,
        out WindowRectangle sidebarRectangle,
        out WindowRectangle statusRectangle,
        out WindowRectangle movedListRectangle,
        out WindowRectangle detailsRectangle) =>
        TryResolveLayout(
            listRectangle,
            listRectangle.Top,
            out sidebarRectangle,
            out statusRectangle,
            out movedListRectangle,
            out detailsRectangle);

    internal static bool TryResolveLayout(
        WindowRectangle listRectangle,
        int shellTop,
        out WindowRectangle sidebarRectangle,
        out WindowRectangle statusRectangle,
        out WindowRectangle movedListRectangle) =>
        TryResolveLayout(
            listRectangle,
            shellTop,
            out sidebarRectangle,
            out statusRectangle,
            out movedListRectangle,
            out _);

    internal static int ResolveDetailsHeight(int availableContentHeight, int? preferredDetailsHeight)
    {
        if (preferredDetailsHeight is null)
        {
            return DetailsHeight;
        }

        var maxDetails = Math.Max(MinimumDetailsHeight, availableContentHeight - MinimumListHeight - DetailsGap);
        return Math.Clamp(preferredDetailsHeight.Value, MinimumDetailsHeight, maxDetails);
    }

    internal static bool TryResolveLayout(
        WindowRectangle listRectangle,
        int shellTop,
        out WindowRectangle sidebarRectangle,
        out WindowRectangle statusRectangle,
        out WindowRectangle movedListRectangle,
        out WindowRectangle detailsRectangle,
        int? preferredDetailsHeight = null)
    {
        var movedTop = shellTop + StatusHeight + StatusGap;
        var resolvedDetailsHeight = ResolveDetailsHeight(listRectangle.Bottom - movedTop, preferredDetailsHeight);
        var detailsTop = listRectangle.Bottom - resolvedDetailsHeight;
        var movedBottom = detailsTop - DetailsGap;
        var movedHeight = movedBottom - movedTop;
        var sidebarRight = listRectangle.Left - SidebarGap;
        var sidebarLeft = sidebarRight - SidebarWidth;
        if (listRectangle.Width <= 0 ||
            shellTop < 0 ||
            shellTop >= listRectangle.Bottom ||
            sidebarLeft < 0 ||
            movedHeight < MinimumListHeight)
        {
            sidebarRectangle = default;
            statusRectangle = default;
            movedListRectangle = default;
            detailsRectangle = default;
            return false;
        }

        sidebarRectangle = new(
            sidebarLeft,
            shellTop,
            sidebarRight,
            listRectangle.Bottom);
        statusRectangle = new(
            listRectangle.Left,
            shellTop,
            listRectangle.Right,
            shellTop + StatusHeight);
        movedListRectangle = new(
            listRectangle.Left,
            movedTop,
            listRectangle.Right,
            movedBottom);
        detailsRectangle = new(
            listRectangle.Left,
            detailsTop,
            listRectangle.Right,
            listRectangle.Bottom);
        return true;
    }

    private bool TryCaptureCurrentRectangle(
        nint parent,
        out WindowRectangle rectangle) =>
        TryCaptureRectangle(_listView, parent, out rectangle);

    private static bool TryCaptureRectangle(
        nint child,
        nint parent,
        out WindowRectangle rectangle)
    {
        if (!GetWindowRect(child, out var screenRectangle))
        {
            rectangle = default;
            return false;
        }

        var topLeft = new WindowPoint(screenRectangle.Left, screenRectangle.Top);
        var bottomRight = new WindowPoint(screenRectangle.Right, screenRectangle.Bottom);
        if (!ScreenToClient(parent, ref topLeft) ||
            !ScreenToClient(parent, ref bottomRight))
        {
            rectangle = default;
            return false;
        }

        rectangle = new(
            topLeft.X,
            topLeft.Y,
            bottomRight.X,
            bottomRight.Y);
        return true;
    }

    private bool RestoreListView(WindowRectangle rectangle) =>
        IsWindow(_listView) &&
        MoveWindow(
            _listView,
            rectangle.Left,
            rectangle.Top,
            rectangle.Width,
            rectangle.Height,
            true);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct WindowRectangle(
        int Left,
        int Top,
        int Right,
        int Bottom)
    {
        public int Width => Math.Max(0, Right - Left);

        public int Height => Math.Max(0, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public WindowPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref WindowPoint point);

}

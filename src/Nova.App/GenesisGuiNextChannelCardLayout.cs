namespace PeerCast.Nova.App;

// Modern only, inside the unchanged 224px pane. No thumbnail or reserved image area.
internal sealed record GenesisGuiNextChannelCardLayout(int Width)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern &&
        (StylesSidebarPlayback(tab) || tab is GenesisGuiNextTab.Playing or GenesisGuiNextTab.Logs);

    internal static bool StylesSidebarPlayback(GenesisGuiNextTab tab) =>
        tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites;

    internal int MetadataWidth => Math.Min(340, (Width - 44) / 3);
    internal int DividerLeft => Width - 22 - MetadataWidth - 12;
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title => Bounds(22, 18, DividerLeft - 36, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Description => Bounds(22, 52, DividerLeft - 36, 64);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Metadata => Bounds(DividerLeft + 12, 18, MetadataWidth, 106);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RecordingStatus => Bounds(22, 124, DividerLeft - 36, 18);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Identity => Bounds(22, 184, DividerLeft - 36, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Record => Bounds(22, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Stop => Bounds(136, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStop => Bounds(250, 146, 132, 32);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStatus => Bounds(DividerLeft + 12, 132, MetadataWidth, 80);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogTitle => Title with { Right = Width - Title.Left };
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogDescription => Description with { Right = LogTitle.Right };
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogScope => RecordingStatus with { Right = LogTitle.Right };
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogRelayStatus => Identity with { Right = LogTitle.Right };

    private static GenesisGuiNextAppShellPlacement.WindowRectangle Bounds(int x, int y, int width, int height) =>
        new(x, y, x + width, y + height);
}

// Visibility only: lifecycle and command guards remain in the existing activation/dispatch.
internal sealed record GenesisGuiNextRelayControlsPresentation(bool Start, bool Stop, bool Status)
{
    internal static GenesisGuiNextRelayControlsPresentation Resolve(bool modern, GenesisGuiNextTab tab,
        GenesisRelayActivationSnapshot snapshot, bool canStop)
    {
        var allowed = GenesisGuiNextPublishingGuideLayout.ShowRelayControls(tab);
        if (!modern) return new(allowed, allowed, allowed);
        // Keep a path back to stop an existing owner, and never conceal failure/cleanup evidence.
        var needsAttention = canStop || snapshot.State != GenesisRelayActivationState.Stopped ||
            snapshot.Error != GenesisRelayActivationError.None || !snapshot.CleanupConfirmed || snapshot.ListenerBound;
        return new(false, allowed && canStop, allowed && needsAttention);
    }
}

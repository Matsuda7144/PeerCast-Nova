namespace PeerCast.Nova.App;

// Logical pixels inside the existing 224px details pane. The list keeps its area.
internal sealed record GenesisGuiNextChannelCardLayout(int Width, bool Channels = false)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern &&
        tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites;

    internal int MetadataWidth => Math.Min(340, (Width - 44) / 3);
    internal int DividerLeft => Width - 22 - MetadataWidth - 12;
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title => Bounds(22, 18, DividerLeft - 36, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Description => Bounds(22, 50, DividerLeft - 36, Channels ? 48 : 40);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Metadata => Bounds(DividerLeft + 12, 18, MetadataWidth, Channels ? 90 : 72);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Identity => Bounds(22, Channels ? 112 : 100, Width - 44, 18);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RecordingStatus => Bounds(22, Channels ? 134 : 123, Width - 44, 18);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Record => Bounds(22, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Stop => Bounds(136, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Copy => Bounds(250, 146, 132, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStart => Bounds(398, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStop => Bounds(512, 146, 106, 32);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStatus => Bounds(22, Channels ? 196 : 184, Width - 44, Channels ? 18 : 28);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle Action(int index)
    {
        var original = new[] { Record, Stop, Copy, RelayStart, RelayStop }[index];
        return Channels ? Bounds(original.Left, 158, original.Width, 32) : original;
    }

    private static GenesisGuiNextAppShellPlacement.WindowRectangle Bounds(int x, int y, int width, int height) =>
        new(x, y, x + width, y + height);
}

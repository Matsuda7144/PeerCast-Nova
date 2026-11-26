namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextRecordingLayout(int Width, GenesisGuiNextRecordingPageMode Mode)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Recording;
    private static GenesisGuiNextAppShellPlacement.WindowRectangle Box(int x, int y, int width, int height) => new(x, y, x + width, y + height);
    private int ContentWidth => Math.Max(1, Width - 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Toolbar(int slot) =>
        Box(slot < 4 ? 16 + slot * 92 : 392 + (slot - 4) * 76, 12, slot < 4 ? 86 : 70, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title => Box(16, 52, ContentWidth, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Description => Box(16, 82, ContentWidth,
        Mode == GenesisGuiNextRecordingPageMode.Library ? 18 : 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Metadata => Box(16,
        Mode == GenesisGuiNextRecordingPageMode.Library ? 104 : 116, ContentWidth,
        Mode == GenesisGuiNextRecordingPageMode.Rules ? 22 : 18);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Identity => Box(16,
        Mode == GenesisGuiNextRecordingPageMode.Rules ? 142 : Mode == GenesisGuiNextRecordingPageMode.Library ? 126 : 138,
        ContentWidth, Mode == GenesisGuiNextRecordingPageMode.Rules ? 36 : 16);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Status => Box(16,
        Mode == GenesisGuiNextRecordingPageMode.History ? 140 : Mode == GenesisGuiNextRecordingPageMode.Library ? 146 : 158,
        ContentWidth, Mode == GenesisGuiNextRecordingPageMode.History ? 40 : Mode == GenesisGuiNextRecordingPageMode.Library ? 34 : 22);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle History => Box(16, 52, ContentWidth, 82);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStart => Box(310, 184, 84, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStop => Box(402, 184, 84, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStatus => Box(500, 184, Math.Max(1, Width - 516), 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Record(bool allowAutomatic) => Box(16, 184, allowAutomatic ? 156 : 68, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Stop => Box(92, 184, 82, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Copy => Box(182, 184, 104, 28);
}

namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextSettingsLayout(int Width, int Height)
{
    private static GenesisGuiNextAppShellPlacement.WindowRectangle Box(int x, int y, int w, int h) => new(x, y, x + w, y + h);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title => Box(16, 12, Width - 32, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle General => Box(16, 54, 108, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Legacy => Box(Width - 224, 54, 208, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Card => Box(16, 100, Width - 32, Height - 164);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle CardTitle => Box(32, 116, Width - 64, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Runtime => Box(32, 150, (Width - 80) / 2, 22);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Configured => Box(Width / 2 + 8, 150, Width / 2 - 40, 22);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Notice => Box(32, 180, Width - 64, 40);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Classic => Box(32, 224, 156, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Modern => Box(200, 224, 156, 32);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Status => Box(32, 270, Width - 64, 40);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Back => Box(16, Height - 44, 108, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Cancel => Box(Width - 240, Height - 44, 108, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Save => Box(Width - 124, Height - 44, 108, 30);
}

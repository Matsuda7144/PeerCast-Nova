namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextNetworkSettingsLayout(int Width)
{
    private static GenesisGuiNextAppShellPlacement.WindowRectangle Box(int x, int y, int w, int h) => new(x, y, x + w, y + h);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Ipv6 => Box(Width - 208, 116, 176, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle ConnectionTitle => Box(32, 116, Width - 592, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle AutoMapping => Box(Width - 544, 116, 320, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Body(GenesisGuiNextSettingsLayout layout) =>
        Box(32, 144, Width - 64, layout.Card.Bottom - 152);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle YpStatus => Box(32, 144, Width - 64, 24);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle YpList => Box(32, 174, 224, 100);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Enabled => Box(32, 282, 224, 26);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle YpLabel(int index) => Box(272, 174 + index * 28, 100, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle YpInput(int index) => Box(376, 174 + index * 28, Width - 408, 24);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle New(int height) => Box(132, height - 44, 72, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Remove(int height) => Box(212, height - 44, 80, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Toggle(int height) => Box(300, height - 44, 108, 30);
}

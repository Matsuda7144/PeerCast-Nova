namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextNetworkSettingsLayout(int Width)
{
    private static GenesisGuiNextAppShellPlacement.WindowRectangle Box(int x, int y, int w, int h) => new(x, y, x + w, y + h);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Notice => Box(32, 144, Width - 64, 36);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Runtime => Box(32, 184, Width - 64, 18);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Saved => Box(32, 204, Width - 64, 18);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Status => Box(32, 294, Width - 352, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Wan => Box(Width - 308, 294, 276, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Ipv6 => Box(Width - 208, 116, 176, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle ConnectionTitle => Box(32, 116, Width - 592, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle AutoMapping => Box(Width - 544, 116, 320, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle NatStatus(GenesisGuiNextSettingsLayout layout)
    {
        const int statusHeight = 60;
        var inCard = Box(32, 330, Width - 64, statusHeight);
        return inCard.Bottom < layout.Card.Bottom ? inCard :
            Box(layout.Back.Right + 8, layout.Card.Bottom + 2, layout.Cancel.Left - layout.Back.Right - 16, statusHeight);
    }
    internal GenesisGuiNextAppShellPlacement.WindowRectangle ConnectionLabel(int index) => Box(32 + index % 3 * ((Width - 64) / 3), 230 + index / 3 * 34, 148, 26);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle ConnectionInput(int index)
    {
        var b = ConnectionLabel(index);
        return Box(b.Left + 148, b.Top, (Width - 64) / 3 - 160, 26);
    }
    internal GenesisGuiNextAppShellPlacement.WindowRectangle YpStatus => Box(32, 144, Width - 64, 24);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle YpList => Box(32, 174, 224, 100);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Enabled => Box(32, 282, 224, 26);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle YpLabel(int index) => Box(272, 174 + index * 28, 100, 24);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle YpInput(int index) => Box(376, 174 + index * 28, Width - 408, 24);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle New(int height) => Box(132, height - 44, 72, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Remove(int height) => Box(212, height - 44, 80, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Toggle(int height) => Box(300, height - 44, 108, 30);
}

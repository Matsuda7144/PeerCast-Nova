namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextSettingsLayout(int Width, int Height)
{
    private static GenesisGuiNextAppShellPlacement.WindowRectangle Box(int x, int y, int w, int h) => new(x, y, x + w, y + h);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title => Box(16, 12, Width - 32, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle General => Box(16, 54, 108, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle ConnectionTab => Box(516, 54, 72, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle YellowPagesTab => Box(596, 54, 56, 30);
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
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle PlaybackTab => Box(132, 54, 108, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RecordingTab => Box(248, 54, 144, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle NotificationsTab => Box(400, 54, 108, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle InternalPlayerChoice => Box(32, 150, 192, 30);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle ExternalPlayerChoice => Box(232, 150, 192, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LowLatencyPlayback => Box(448, 152, Width - 480, 26);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle ExternalPlayerArgsLabel => Box(436, 154, 52, 22);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle ExternalPlayerArgsInput => Box(492, 151, Width - 524, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle PathNotice(bool playback) => Box(32, playback ? 184 : 150, Width - 64, playback ? 38 : 40);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle PathRuntime(bool playback) => Box(32, playback ? 224 : 192, Width - 64, 20);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle PathSaved(bool playback) => Box(32, playback ? 246 : 216, Width - 64, playback ? 18 : 20);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle PathInput(bool playback) => Box(32, playback ? 265 : 244, Width - 172, playback ? 28 : 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Browse(bool playback)
    {
        var input = PathInput(playback);
        return Box(Width - 128, input.Top, 96, input.Height);
    }
    internal GenesisGuiNextAppShellPlacement.WindowRectangle PathStatus(bool playback) => Box(32, playback ? 296 : 282, Width - 64, playback ? 22 : 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Reset => Box(Width - 440, Height - 44, 188, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle SetupLibMpv => Box(Width - 452, Height - 44, 200, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle SoundTest => Box(Width - 176, Height - 44, 160, 30);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle NotificationCheck(int index) => Box(32, 196 + index * 32, Width - 64, 26);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle NotificationStatus => Box(32, 292, Width - 64, 20);
}

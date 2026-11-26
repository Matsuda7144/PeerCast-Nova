namespace PeerCast.Nova.App;

internal static class GenesisNativeWindowClassPresentation
{
    private const int ColorWindow = 5;

    // Win32 WNDCLASS/WNDCLASSEX accepts a system-color brush as COLOR_* + 1.
    // Supplying COLOR_WINDOW keeps invalidated child-container pixels painted
    // during restore/repaint instead of exposing undefined background contents.
    internal static nint WindowBackgroundBrush => (nint)(ColorWindow + 1);
}

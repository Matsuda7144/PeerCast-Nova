namespace PeerCast.Nova.App;

// Logical coordinates; the existing native controls and DPI placement are shared.
internal readonly record struct GenesisGuiNextPublishingModernLayout(int Width)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Publishing;
    private static GenesisGuiNextPublishingGuideRectangle Box(int x, int y, int width, int height) => new(x, y, width, height);
    internal GenesisGuiNextPublishingGuideRectangle Title => Box(16, 12, Width - 440, 24);
    internal GenesisGuiNextPublishingGuideRectangle Start => Box(Width - 404, 8, 88, 28);
    internal GenesisGuiNextPublishingGuideRectangle Finish => Box(Width - 308, 8, 88, 28);
    internal GenesisGuiNextPublishingGuideRectangle Interrupt => Box(Width - 212, 8, 88, 28);
    internal GenesisGuiNextPublishingGuideRectangle Settings => Box(Width - 116, 8, 100, 28);
    internal GenesisGuiNextPublishingGuideRectangle State => Box(16, 40, Width - 32, 18);
    internal static GenesisGuiNextPublishingGuideRectangle ServerLabel => Box(16, 60, 104, 22);
    internal GenesisGuiNextPublishingGuideRectangle Server => Box(128, 60, Width - 232, 22);
    internal GenesisGuiNextPublishingGuideRectangle ServerCopy => Box(Width - 92, 60, 76, 22);
    internal static GenesisGuiNextPublishingGuideRectangle KeyLabel => Box(16, 86, 104, 22);
    internal GenesisGuiNextPublishingGuideRectangle Key => Box(128, 86, Width - 308, 22);
    internal GenesisGuiNextPublishingGuideRectangle Reveal => Box(Width - 168, 86, 68, 22);
    internal GenesisGuiNextPublishingGuideRectangle KeyCopy => Box(Width - 92, 86, 76, 22);
    internal GenesisGuiNextPublishingGuideRectangle Instructions => Box(16, 112, Width - 32, 64);
    internal GenesisGuiNextPublishingGuideRectangle Feedback => Box(16, 182, Width - 32, 34);
    internal const uint SingleLineFlags = 0x0020 | 0x0004 | 0x8000; // single line, vertical center, end ellipsis
    internal const uint FeedbackFlags = 0x0010 | 0x8000; // wrap, end ellipsis; native text remains intact
}

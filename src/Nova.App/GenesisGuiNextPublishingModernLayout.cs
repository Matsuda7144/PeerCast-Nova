namespace PeerCast.Nova.App;

// Modern uses the retained publishing controls across the content area. Classic keeps its 224px guide.
internal readonly record struct GenesisGuiNextPublishingModernLayout(int Width, int Height)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Publishing;
    private int ContentWidth => Math.Min(Width, 1320);
    private int ContentHeight => Math.Min(Height, 720);
    private int Offset => (Width - ContentWidth) / 2;
    private int LeftWidth => (ContentWidth - 44) * 56 / 100;
    private int RightLeft => 28 + LeftWidth;
    private int RightWidth => ContentWidth - RightLeft - 16;
    private int BodyHeight => ContentHeight - 228;
    private int SmallHeight => (BodyHeight - 12) / 2;
    private GenesisGuiNextPublishingGuideRectangle Box(int x, int y, int width, int height) => new(Offset + x, y, width, height);
    internal GenesisGuiNextPublishingGuideRectangle Title => Box(16, 12, ContentWidth - 32, 28);
    internal GenesisGuiNextPublishingGuideRectangle Subtitle => Box(16, 44, ContentWidth - 32, 20);
    internal GenesisGuiNextPublishingGuideRectangle StatusCard => Box(16, 76, ContentWidth - 32, 84);
    internal GenesisGuiNextPublishingGuideRectangle State => Box(28, 86, ContentWidth - 56, 26);
    internal GenesisGuiNextPublishingGuideRectangle Feedback => Box(28, 118, ContentWidth - 56, 32);
    internal GenesisGuiNextPublishingGuideRectangle ObsCard => Box(16, 172, LeftWidth, BodyHeight);
    internal GenesisGuiNextPublishingGuideRectangle ObsTitle => Box(28, 182, LeftWidth - 24, 22);
    internal GenesisGuiNextPublishingGuideRectangle ServerLabel => Box(28, 210, 88, 24);
    internal GenesisGuiNextPublishingGuideRectangle Server => Box(120, 210, LeftWidth - 184, 24);
    internal GenesisGuiNextPublishingGuideRectangle ServerCopy => Box(LeftWidth - 60, 210, 64, 24);
    internal GenesisGuiNextPublishingGuideRectangle KeyLabel => Box(28, 240, 88, 24);
    internal GenesisGuiNextPublishingGuideRectangle Key => Box(120, 240, LeftWidth - 248, 24);
    internal GenesisGuiNextPublishingGuideRectangle Reveal => Box(LeftWidth - 124, 240, 56, 24);
    internal GenesisGuiNextPublishingGuideRectangle KeyCopy => Box(LeftWidth - 60, 240, 64, 24);
    internal GenesisGuiNextPublishingGuideRectangle Instructions => Box(28, 274, LeftWidth - 24, BodyHeight - 114);
    internal GenesisGuiNextPublishingGuideRectangle BasicCard => Box(RightLeft, 172, RightWidth, SmallHeight);
    internal GenesisGuiNextPublishingGuideRectangle BasicTitle => Box(RightLeft + 12, 182, RightWidth - 24, 22);
    internal GenesisGuiNextPublishingGuideRectangle Basic => Box(RightLeft + 12, 210, RightWidth - 24, SmallHeight - 50);
    internal GenesisGuiNextPublishingGuideRectangle OptionsCard => Box(RightLeft, 184 + SmallHeight, RightWidth, BodyHeight - SmallHeight - 12);
    internal GenesisGuiNextPublishingGuideRectangle OptionsTitle => Box(RightLeft + 12, 194 + SmallHeight, RightWidth - 24, 22);
    internal GenesisGuiNextPublishingGuideRectangle Options => Box(RightLeft + 12, 222 + SmallHeight, RightWidth - 24, BodyHeight - SmallHeight - 62);
    internal GenesisGuiNextPublishingGuideRectangle Start => Box(ContentWidth - 440, ContentHeight - 44, 104, 32);
    internal GenesisGuiNextPublishingGuideRectangle Finish => Box(ContentWidth - 328, ContentHeight - 44, 96, 32);
    internal GenesisGuiNextPublishingGuideRectangle Interrupt => Box(ContentWidth - 224, ContentHeight - 44, 96, 32);
    internal GenesisGuiNextPublishingGuideRectangle Settings => Box(ContentWidth - 120, ContentHeight - 44, 104, 32);
    internal const uint SingleLineFlags = 0x0020 | 0x0004 | 0x8000;
    internal const uint FeedbackFlags = 0x0010 | 0x8000;
}

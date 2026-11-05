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
    private bool Compact => ContentHeight < 480;
    private int BodyTop => Compact ? 136 : 172;
    private int BodyHeight => ContentHeight - BodyTop - (Compact ? 48 : 56);
    private int SmallHeight => Math.Max(Compact ? 114 : 120, (BodyHeight - 12) / 2);
    private GenesisGuiNextPublishingGuideRectangle Box(int x, int y, int width, int height) => new(Offset + x, y, width, height);
    internal GenesisGuiNextPublishingGuideRectangle Title => Box(16, 12, ContentWidth - 32, Compact ? 24 : 28);
    internal GenesisGuiNextPublishingGuideRectangle Subtitle => Box(16, Compact ? 40 : 44, ContentWidth - 32, Compact ? 16 : 20);
    internal GenesisGuiNextPublishingGuideRectangle StatusCard => Box(16, Compact ? 60 : 76, ContentWidth - 32, Compact ? 64 : 84);
    internal GenesisGuiNextPublishingGuideRectangle State => Box(28, Compact ? 66 : 86, ContentWidth - 56, Compact ? 20 : 26);
    internal GenesisGuiNextPublishingGuideRectangle Feedback => Box(28, Compact ? 88 : 118, ContentWidth - 56, 32);
    internal GenesisGuiNextPublishingGuideRectangle ObsCard => Box(16, BodyTop, LeftWidth, BodyHeight);
    internal GenesisGuiNextPublishingGuideRectangle ObsTitle => Box(28, BodyTop + 10, LeftWidth - 24, 22);
    internal GenesisGuiNextPublishingGuideRectangle ServerLabel => Box(28, BodyTop + 38, 88, 24);
    internal GenesisGuiNextPublishingGuideRectangle Server => Box(120, BodyTop + 38, LeftWidth - 184, 24);
    internal GenesisGuiNextPublishingGuideRectangle ServerCopy => Box(LeftWidth - 60, BodyTop + 38, 64, 24);
    internal GenesisGuiNextPublishingGuideRectangle KeyLabel => Box(28, BodyTop + 68, 88, 24);
    internal GenesisGuiNextPublishingGuideRectangle Key => Box(120, BodyTop + 68, LeftWidth - 248, 24);
    internal GenesisGuiNextPublishingGuideRectangle Reveal => Box(LeftWidth - 124, BodyTop + 68, 56, 24);
    internal GenesisGuiNextPublishingGuideRectangle KeyCopy => Box(LeftWidth - 60, BodyTop + 68, 64, 24);
    internal GenesisGuiNextPublishingGuideRectangle Instructions => Box(28, BodyTop + 102, LeftWidth - 24, BodyHeight - 114);
    internal GenesisGuiNextPublishingGuideRectangle BasicCard => Box(RightLeft, BodyTop, RightWidth, SmallHeight);
    internal GenesisGuiNextPublishingGuideRectangle BasicTitle => Box(RightLeft + 12, BodyTop + 10, RightWidth - 24, 22);
    internal GenesisGuiNextPublishingGuideRectangle Basic => Box(RightLeft + 12, BodyTop + 38, RightWidth - 24, SmallHeight - 50);
    internal GenesisGuiNextPublishingGuideRectangle OptionsCard => Box(RightLeft, BodyTop + 12 + SmallHeight, RightWidth, BodyHeight - SmallHeight - 12);
    internal GenesisGuiNextPublishingGuideRectangle OptionsTitle => Box(RightLeft + 12, BodyTop + 22 + SmallHeight, RightWidth - 24, 22);
    internal GenesisGuiNextPublishingGuideRectangle Options => Box(RightLeft + 12, BodyTop + 50 + SmallHeight, RightWidth - 24, BodyHeight - SmallHeight - 62);
    internal GenesisGuiNextPublishingGuideRectangle Start => Box(ContentWidth - 440, ContentHeight - 44, 104, 32);
    internal GenesisGuiNextPublishingGuideRectangle Finish => Box(ContentWidth - 328, ContentHeight - 44, 96, 32);
    internal GenesisGuiNextPublishingGuideRectangle Interrupt => Box(ContentWidth - 224, ContentHeight - 44, 96, 32);
    internal GenesisGuiNextPublishingGuideRectangle Settings => Box(ContentWidth - 120, ContentHeight - 44, 104, 32);
    internal const uint SingleLineFlags = 0x0020 | 0x0004 | 0x8000;
    internal const uint FeedbackFlags = 0x0010 | 0x8000;
}

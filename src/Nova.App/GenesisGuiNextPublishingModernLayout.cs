namespace PeerCast.Nova.App;

// Modern retains the same controls and commands. Only the active editor receives the larger card.
internal readonly record struct GenesisGuiNextPublishingModernLayout(int Width, int Height, bool AdvancedExpanded = false, bool BasicEditing = false)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Publishing;
    private int ContentWidth => Math.Min(Width, 1440);
    private int ContentHeight => Math.Min(Height, 960);
    private int Offset => (Width - ContentWidth) / 2;
    private int LeftWidth => (ContentWidth - 44) / 2;
    private int RightLeft => 28 + LeftWidth;
    private int RightWidth => ContentWidth - RightLeft - 16;
    private bool Compact => ContentHeight < 600;
    private int BodyTop => Compact ? 102 : 154;
    private int BodyHeight => ContentHeight - BodyTop - (Compact ? 48 : 68);
    private int ObsHeight => Math.Clamp(BodyHeight / 3, 118, 188);
    private int HeaderSpace => Compact ? 36 : 48;
    private int FooterSpace => Compact ? 44 : 60;
    internal int ActionHeight => Compact ? 30 : 36;
    private GenesisGuiNextPublishingGuideRectangle Box(int x, int y, int width, int height) => new(Offset + x, y, width, height);
    internal GenesisGuiNextPublishingGuideRectangle Title => Box(16, Compact ? 6 : 12, ContentWidth - 32, Compact ? 20 : 28);
    internal GenesisGuiNextPublishingGuideRectangle Subtitle => Box(16, Compact ? 30 : 44, ContentWidth - 32, Compact ? 14 : 20);
    internal GenesisGuiNextPublishingGuideRectangle StatusCard => Box(16, Compact ? 46 : 76, ContentWidth - 32, Compact ? 48 : 64);
    internal GenesisGuiNextPublishingGuideRectangle State => Box(32, Compact ? 50 : 80, ContentWidth - 64, Compact ? 20 : 26);
    internal GenesisGuiNextPublishingGuideRectangle Feedback => Box(32, Compact ? 72 : 108, ContentWidth - 64, Compact ? 20 : 28);
    internal GenesisGuiNextPublishingGuideRectangle ObsCard => Box(BasicEditing ? RightLeft : 16, BodyTop, BasicEditing ? RightWidth : LeftWidth, ObsHeight);
    internal GenesisGuiNextPublishingGuideRectangle ObsTitle => new(ObsCard.Left + 16, ObsCard.Top + 6, ObsCard.Width - 32, 20);
    internal GenesisGuiNextPublishingGuideRectangle ServerLabel => new(ObsCard.Left + 16, ObsCard.Top + 30, 88, 26);
    internal GenesisGuiNextPublishingGuideRectangle Server => new(ObsCard.Left + 108, ObsCard.Top + 30, ObsCard.Width - 208, 26);
    internal GenesisGuiNextPublishingGuideRectangle ServerCopy => new(ObsCard.Right - 92, ObsCard.Top + 30, 76, 26);
    internal GenesisGuiNextPublishingGuideRectangle KeyLabel => new(ObsCard.Left + 16, ObsCard.Top + 60, 88, 26);
    internal GenesisGuiNextPublishingGuideRectangle Key => new(ObsCard.Left + 108, ObsCard.Top + 60, ObsCard.Width - 284, 26);
    internal GenesisGuiNextPublishingGuideRectangle Reveal => new(ObsCard.Right - 164, ObsCard.Top + 60, 64, 26);
    internal GenesisGuiNextPublishingGuideRectangle KeyCopy => new(ObsCard.Right - 92, ObsCard.Top + 60, 76, 26);
    internal GenesisGuiNextPublishingGuideRectangle Instructions => new(ObsCard.Left + 16, ObsCard.Top + 90, ObsCard.Width - 32, Math.Max(8, ObsHeight - 98));
    internal GenesisGuiNextPublishingGuideRectangle BasicCard => BasicEditing
        ? Box(16, BodyTop, LeftWidth, BodyHeight)
        : Box(16, BodyTop + ObsHeight + 12, LeftWidth, BodyHeight - ObsHeight - 12);
    internal GenesisGuiNextPublishingGuideRectangle BasicTitle => new(BasicCard.Left + 16, BasicCard.Top + (Compact ? 8 : 14), BasicCard.Width - 32, Compact ? 20 : 26);
    internal GenesisGuiNextPublishingGuideRectangle Basic => new(BasicCard.Left + 16, BasicCard.Top + HeaderSpace, BasicCard.Width - 32, Math.Max(20, BasicCard.Height - HeaderSpace - FooterSpace));
    internal GenesisGuiNextPublishingGuideRectangle BasicAction => new(Basic.Left, BasicCard.Bottom - FooterSpace + 6, 112, ActionHeight);
    internal GenesisGuiNextPublishingGuideRectangle OptionsCard => BasicEditing
        ? Box(RightLeft, BodyTop + ObsHeight + 12, RightWidth, BodyHeight - ObsHeight - 12)
        : Box(RightLeft, BodyTop, RightWidth, BodyHeight);
    internal GenesisGuiNextPublishingGuideRectangle OptionsTitle => new(OptionsCard.Left + 16, OptionsCard.Top + (Compact ? 8 : 14), OptionsCard.Width - 132, Compact ? 20 : 26);
    internal GenesisGuiNextPublishingGuideRectangle Options => new(OptionsCard.Left + 16, OptionsCard.Top + HeaderSpace, OptionsCard.Width - 32, Math.Max(20, OptionsCard.Height - HeaderSpace - 12));
    internal GenesisGuiNextPublishingGuideRectangle AdvancedToggle => new(OptionsCard.Right - 108, OptionsCard.Top + (Compact ? 6 : 10), 92, Compact ? 26 : 32);
    internal GenesisGuiNextPublishingGuideRectangle AdvancedViewport => new(Options.Left, Options.Top, Options.Width, Math.Max(20, OptionsCard.Height - HeaderSpace - FooterSpace));
    internal GenesisGuiNextPublishingGuideRectangle AdvancedAction => new(Options.Left, OptionsCard.Bottom - FooterSpace + 6, 152, ActionHeight);
    internal GenesisGuiNextPublishingGuideRectangle Start => Box(ContentWidth - 516, ContentHeight - (Compact ? 40 : 52), 144, Compact ? 32 : 40);
    internal GenesisGuiNextPublishingGuideRectangle Finish => Box(ContentWidth - 364, Start.Top, 108, Start.Height);
    internal GenesisGuiNextPublishingGuideRectangle Interrupt => Box(ContentWidth - 248, Start.Top, 108, Start.Height);
    internal GenesisGuiNextPublishingGuideRectangle Settings => Box(ContentWidth - 132, Start.Top, 116, Start.Height);
    internal const uint SingleLineFlags = 0x0020 | 0x0004 | 0x8000;
    internal const uint FeedbackFlags = 0x0010 | 0x8000;
}

namespace PeerCast.Nova.App;

// モダンUI用録画パネル配置。詳細ペイン高さ変更時（最小140px～拡大時）にツールバー・本文・下部ボタンを収める。
internal readonly record struct GenesisGuiNextRecordingLayout(
    int Width,
    GenesisGuiNextRecordingPageMode Mode,
    int Height = GenesisGuiNextAppShellPlacement.DetailsHeight)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Recording;
    private static GenesisGuiNextAppShellPlacement.WindowRectangle Box(int x, int y, int width, int height) => new(x, y, x + width, y + height);
    private int ContentWidth => Math.Max(1, Width - 32);
    private int EffectiveHeight => Math.Max(GenesisGuiNextAppShellPlacement.MinimumDetailsHeight, Height);
    private int ExtraHeight => Math.Max(0, EffectiveHeight - GenesisGuiNextAppShellPlacement.DetailsHeight);
    private int DeficitHeight => Math.Max(0, GenesisGuiNextAppShellPlacement.DetailsHeight - EffectiveHeight);

    private int ToolbarTop => DeficitHeight == 0 ? 12 : 12 - (DeficitHeight * 4 + 42) / 84;
    private int ToolbarHeight => DeficitHeight == 0 ? 28 : 28 - (DeficitHeight * 4 + 42) / 84;
    private int GapToolbarToDivider => DeficitHeight == 0 ? 6 : 6 - (DeficitHeight * 4 + 42) / 84;
    internal int TopDividerY => ToolbarTop + ToolbarHeight + GapToolbarToDivider;
    private int BodyTop => TopDividerY + GapToolbarToDivider;
    private int ActionHeight => DeficitHeight == 0 ? 28 : 28 - (DeficitHeight * 4 + 42) / 84;
    private int BottomInset => DeficitHeight == 0 ? 12 : 12 - (DeficitHeight * 4 + 42) / 84;
    private int ActionTop => EffectiveHeight - BottomInset - ActionHeight;
    internal int BottomDividerY => ActionTop - 2;
    private int BodyBottom => BottomDividerY - 2;
    private int CompactBodySpan => Math.Max(56, BodyBottom - BodyTop);

    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Toolbar(int slot) =>
        Box(slot < 4 ? 16 + slot * 92 : 392 + (slot - 4) * 76, 12, slot < 4 ? 86 : 70, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle ToolbarButton(int slot) =>
        Box(slot < 4 ? 16 + slot * 92 : 392 + (slot - 4) * 76, ToolbarTop, slot < 4 ? 86 : 70, ToolbarHeight);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title =>
        DeficitHeight == 0
            ? Box(16, 52, ContentWidth, 24)
            : Box(16, BodyTop, ContentWidth, CompactTitleHeight);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle Description =>
        DeficitHeight == 0
            ? Box(16, 82, ContentWidth, (Mode == GenesisGuiNextRecordingPageMode.Library ? 18 : 28) + ExtraHeight)
            : Box(16, Title.Bottom + 1, ContentWidth, CompactDescriptionHeight);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle Metadata =>
        DeficitHeight == 0
            ? Box(16, (Mode == GenesisGuiNextRecordingPageMode.Library ? 104 : 116) + ExtraHeight, ContentWidth,
                Mode == GenesisGuiNextRecordingPageMode.Rules ? 22 : 18)
            : Box(16, Description.Bottom + 1, ContentWidth, CompactMetadataHeight);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle Identity =>
        DeficitHeight == 0
            ? Box(16, (Mode == GenesisGuiNextRecordingPageMode.Rules ? 142 : Mode == GenesisGuiNextRecordingPageMode.Library ? 126 : 138) + ExtraHeight,
                ContentWidth, Mode == GenesisGuiNextRecordingPageMode.Rules ? 36 : 16)
            : Box(16, Metadata.Bottom + 1, ContentWidth, CompactIdentityHeight);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle Status
    {
        get
        {
            if (DeficitHeight == 0)
            {
                return Box(16,
                    (Mode == GenesisGuiNextRecordingPageMode.History ? 140 : Mode == GenesisGuiNextRecordingPageMode.Library ? 146 : 158) + ExtraHeight,
                    ContentWidth, Mode == GenesisGuiNextRecordingPageMode.History ? 40 : Mode == GenesisGuiNextRecordingPageMode.Library ? 34 : 22);
            }

            if (Mode == GenesisGuiNextRecordingPageMode.History)
            {
                var statusHeight = Math.Clamp(CompactBodySpan * 40 / 128, 18, 40);
                return Box(16, BodyBottom - statusHeight, ContentWidth, statusHeight);
            }

            var top = Mode == GenesisGuiNextRecordingPageMode.Rules ? BodyBottom - 14 : Identity.Bottom + 1;
            return Box(16, top, ContentWidth, Math.Max(12, BodyBottom - top));
        }
    }

    internal GenesisGuiNextAppShellPlacement.WindowRectangle History =>
        DeficitHeight == 0
            ? Box(16, 52, ContentWidth, 82 + ExtraHeight)
            : Box(16, BodyTop, ContentWidth, Math.Max(20, Status.Top - 2 - BodyTop));

    private int CompactTitleHeight => Mode == GenesisGuiNextRecordingPageMode.Rules
        ? Math.Max(14, CompactBodySpan * 24 / 128)
        : Math.Max(13, CompactBodySpan * 24 / 128);
    private int CompactDescriptionHeight => Mode == GenesisGuiNextRecordingPageMode.Rules
        ? Math.Max(16, CompactBodySpan * 28 / 128)
        : Math.Max(13, CompactBodySpan * 24 / 128);
    private int CompactMetadataHeight => Mode == GenesisGuiNextRecordingPageMode.Rules
        ? Math.Max(14, CompactBodySpan * 22 / 128)
        : Math.Max(12, CompactBodySpan * 18 / 128);
    private int CompactIdentityHeight => Mode == GenesisGuiNextRecordingPageMode.Rules
        ? Math.Max(16, BodyBottom - (BodyTop + CompactTitleHeight + CompactDescriptionHeight + CompactMetadataHeight + 3))
        : Math.Max(12, CompactBodySpan * 16 / 128);

    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStart => Box(310, 184, 84, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStop => Box(402, 184, 84, 28);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStatus => Box(500, ActionTop, Math.Max(1, Width - 516), ActionHeight);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Record(bool allowAutomatic) => Box(16, 184, allowAutomatic ? 156 : 68, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Stop => Box(92, 184, 82, 28);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Copy => Box(182, 184, 104, 28);

    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStartButton => Box(310, ActionTop, 84, ActionHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStopButton => Box(402, ActionTop, 84, ActionHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RecordButton(bool allowAutomatic) => Box(16, ActionTop, allowAutomatic ? 156 : 68, ActionHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle StopButton => Box(92, ActionTop, 82, ActionHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle CopyButton => Box(182, ActionTop, 104, ActionHeight);
}

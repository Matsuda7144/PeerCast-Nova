namespace PeerCast.Nova.App;

// モダンUI用詳細カード配置。リサイズ・スプリッター縮小／拡大に追従する（既定高さ 224px では従来座標を維持）。
internal sealed record GenesisGuiNextChannelCardLayout(
    int Width,
    int Height = GenesisGuiNextAppShellPlacement.DetailsHeight)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern &&
        (StylesSidebarPlayback(tab) || tab is GenesisGuiNextTab.Playing or GenesisGuiNextTab.Logs);

    internal static bool StylesSidebarPlayback(GenesisGuiNextTab tab) =>
        tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites;

    private int EffectiveHeight => Math.Max(GenesisGuiNextAppShellPlacement.MinimumDetailsHeight, Height);
    private int ExtraHeight => Math.Max(0, EffectiveHeight - GenesisGuiNextAppShellPlacement.DetailsHeight);
    private int DeficitHeight => Math.Max(0, GenesisGuiNextAppShellPlacement.DetailsHeight - EffectiveHeight);

    private int TopMargin => DeficitHeight == 0 ? 18 : 18 - (DeficitHeight * 6 + 42) / 84;
    private int TitleHeight => DeficitHeight == 0 ? 28 : 28 - (DeficitHeight * 4 + 42) / 84;
    private int GapTitleToDescription => DeficitHeight == 0 ? 6 : 6 - (DeficitHeight * 2 + 42) / 84;
    private int GapDescriptionToStatus => DeficitHeight == 0 ? 8 : 8 - (DeficitHeight * 4 + 42) / 84;
    private int GapStatusToButtons => DeficitHeight == 0 ? 4 : DeficitHeight >= 42 ? 3 : 4;
    private int ButtonHeight => DeficitHeight == 0 ? 32 : 32 - (DeficitHeight * 4 + 42) / 84;
    private int IdentityHeight => DeficitHeight == 0 ? 38 : 38 - (DeficitHeight * 20 + 42) / 84;
    private int BottomLimit => EffectiveHeight - 7;
    private int IdentityTop => DeficitHeight == 0 ? 179 + ExtraHeight : BottomLimit - IdentityHeight;
    private int ButtonTop => DeficitHeight == 0 ? 146 + ExtraHeight : IdentityTop - 1 - ButtonHeight;
    private int RecordingStatusTop => DeficitHeight == 0 ? 124 + ExtraHeight : ButtonTop - GapStatusToButtons - 18;
    private int DescriptionTop => TopMargin + TitleHeight + GapTitleToDescription;
    private int DescriptionHeight => DeficitHeight == 0
        ? 64 + ExtraHeight
        : Math.Max(18, RecordingStatusTop - GapDescriptionToStatus - DescriptionTop);
    private int MetadataHeight => DeficitHeight == 0
        ? 106 + ExtraHeight
        : Math.Max(36, RecordingStatusTop - TopMargin);

    internal int DividerLeft => Width - 22 - MetadataWidth - 12;
    internal int DividerTop => TopMargin;
    internal int DividerBottom => TopMargin + MetadataHeight;
    internal int MetadataWidth => Math.Min(340, (Width - 44) / 3);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Title => Bounds(22, TopMargin, DividerLeft - 36, TitleHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Description => Bounds(22, DescriptionTop, DividerLeft - 36, DescriptionHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Metadata => Bounds(DividerLeft + 12, TopMargin, MetadataWidth, MetadataHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RecordingStatus => Bounds(22, RecordingStatusTop, DividerLeft - 36, 18);
    // 操作ボタンとカード下端の間にシステムフォント2行分（DPI切り上げ含む）の領域を確保する。
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Identity => Bounds(22, IdentityTop, DividerLeft - 36, IdentityHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RecordButton => Bounds(22, ButtonTop, 106, ButtonHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle StopButton => Bounds(136, ButtonTop, 106, ButtonHeight);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStopButton => Bounds(250, ButtonTop, 132, ButtonHeight);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Record => Bounds(22, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Stop => Bounds(136, 146, 106, 32);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle RelayStop => Bounds(250, 146, 132, 32);
    internal GenesisGuiNextAppShellPlacement.WindowRectangle RelayStatus => DeficitHeight == 0
        ? Bounds(DividerLeft + 12, 132 + ExtraHeight, MetadataWidth, 80)
        : Bounds(DividerLeft + 12, DividerBottom + 6, MetadataWidth, Math.Max(18, BottomLimit - (DividerBottom + 6)));
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogTitle => Title with { Right = Width - Title.Left };
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogDescription => Description with { Right = LogTitle.Right };
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogScope => RecordingStatus with { Right = LogTitle.Right };
    internal GenesisGuiNextAppShellPlacement.WindowRectangle LogRelayStatus => Identity with { Right = LogTitle.Right };

    private static GenesisGuiNextAppShellPlacement.WindowRectangle Bounds(int x, int y, int width, int height) =>
        new(x, y, x + width, y + height);
}

// Visibility only: lifecycle and command guards remain in the existing activation/dispatch.
internal sealed record GenesisGuiNextRelayControlsPresentation(bool Start, bool Stop, bool Status)
{
    internal static GenesisGuiNextRelayControlsPresentation Resolve(bool modern, GenesisGuiNextTab tab,
        GenesisRelayActivationSnapshot snapshot, bool canStop)
    {
        var allowed = GenesisGuiNextPublishingGuideLayout.ShowRelayControls(tab);
        if (!modern) return new(allowed, allowed, allowed);
        // Keep a path back to stop an existing owner, and never conceal failure/cleanup evidence.
        var needsAttention = canStop || snapshot.State != GenesisRelayActivationState.Stopped ||
            snapshot.Error != GenesisRelayActivationError.None || !snapshot.CleanupConfirmed || snapshot.ListenerBound;
        return new(false, allowed && canStop, allowed && needsAttention);
    }
}

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App.Tests;

/// <summary>
/// AppShellState から分離された各タブ／機能コントローラーの構造回帰および単体動作を検証する。
/// </summary>
public sealed class GenesisShellControllerDecouplingTests
{
    [Fact]
    public void SeparatedFeatureDirectoriesContainNoAppShellBridgePartials()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "src", "Nova.App");

        var settingsFiles = Directory.GetFiles(Path.Combine(appRoot, "Settings"), "*.cs", SearchOption.AllDirectories);
        var publishingFiles = Directory.GetFiles(Path.Combine(appRoot, "Publishing"), "*.cs", SearchOption.AllDirectories);
        var relayTreeFiles = Directory.GetFiles(appRoot, "*RelayTree*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(settingsFiles);
        Assert.NotEmpty(publishingFiles);
        Assert.NotEmpty(relayTreeFiles);

        foreach (var file in settingsFiles.Concat(publishingFiles).Concat(relayTreeFiles))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("partial class GenesisGuiNextAppShellBridge", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ControllersInstantiateAndOperateSafelyWithoutWin32Handles()
    {
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var host = new FakeShellHost(tabs, viewModel) { IsModern = true };

        var relayTree = new GenesisRelayTreeTabController(host);
        var settings = new GenesisIntegratedSettingsController(host);
        var publishing = new GenesisPublishingTabController(host);

        // リレーツリーコントローラーの初期状態と未生成時の安全な早期リターン
        Assert.False(relayTree.IsPageShown);
        Assert.False(relayTree.ProcessCommand(9999, 0, 0));
        Assert.False(relayTree.HandleContextMenu(0, 0, 0));
        Assert.False(relayTree.DrawControl(0));
        Assert.False(relayTree.HandleNotify(0));
        Assert.False(relayTree.IsTreeControl(123));

        // 統合設定コントローラーの初期状態と未生成時の安全な早期リターン
        Assert.False(settings.IsOpen);
        Assert.Equal(0, settings.PaneWindow);
        Assert.False(settings.IsPane(123));
        Assert.False(settings.Open());
        Assert.False(settings.ProcessCommand(9999, 0));
        Assert.False(settings.ProcessInput(9999, 0, 0));
        Assert.False(settings.HandleControlMessage(0, 0, 0));
        settings.Close();
        settings.Detach();

        // 配信タブコントローラーの初期状態・パネル判定・安全な終了処理
        Assert.False(publishing.IsGuideAvailable);
        Assert.False(publishing.UsesPanel);
        Assert.True(publishing.CanLeaveEditor());
        Assert.False(publishing.IsInlineInput(123));
        Assert.False(publishing.IsEditorPane(123));
        Assert.False(publishing.IsAdvancedCombo(123));
        Assert.False(publishing.IsAdvancedPane(123));
        Assert.False(publishing.ProcessGuideCommand(GenesisGuiNextAppShellBridge.PublishingSettingsControlId, 0));

        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        Assert.True(publishing.UsesPanel);

        host.IsDetached = true;
        Assert.False(publishing.UsesPanel);
        publishing.ResetPanePlacement();
        publishing.Detach();
    }

    [Theory]
    [InlineData(false, false, false, true, true, true)]
    [InlineData(false, false, true, false, true, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(false, true, true, false, false, false)]
    [InlineData(true, true, true, false, false, false)]
    public void ResolveMainPaneVisibilityCombinesAllPaneStatesWithoutTransientListShow(
        bool settingsOpen,
        bool relayTreeShown,
        bool publishingExpanded,
        bool expectedListVisible,
        bool expectedDetailsVisible,
        bool expectedSplitterVisible)
    {
        var actual = GenesisGuiNextAppShellBridge.ResolveMainPaneVisibility(
            settingsOpen,
            relayTreeShown,
            publishingExpanded);

        Assert.Equal(expectedListVisible, actual.ListVisible);
        Assert.Equal(expectedDetailsVisible, actual.DetailsVisible);
        Assert.Equal(expectedSplitterVisible, actual.SplitterVisible);
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PeerCast-Nova.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PeerCast-Nova.sln が見つかりませんでした。");
    }

    private sealed class FakeShellHost(
        GenesisGuiNextTabsViewModel tabs,
        GenesisMainViewModel viewModel) : IGenesisGuiNextShellHost
    {
        public nint Owner => 0;
        public nint Sidebar => 0;
        public nint ListView => 0;
        public nint DetailsPane => 0;
        public nint DetailsTitle => 0;
        public nint DetailsDescription => 0;
        public nint DetailsMetadata => 0;
        public nint DetailsIdentity => 0;
        public int DetailsTextWidth => 480;
        public nint PublishingStartButton => 0;
        public nint PublishingInterruptButton => 0;
        public nint PublishingFinishButton => 0;
        public GenesisGuiNextTabsViewModel Tabs { get; } = tabs;
        public GenesisMainViewModel ViewModel { get; } = viewModel;
        public bool IsModern { get; set; }
        public bool IsDetached { get; set; }
        public bool IsSettingsPageOpen { get; set; }
        public uint LayoutDpi => 96;
        public int? DetailsHeight => null;
        public bool LayoutInProgress { get; set; }
        public nint HoverControl => 0;
        public nint MainBrush => 0;
        public nint TopBrush => 0;
        public nint InputBrush => 0;
        public nint VisualFontHandle => 0;
        public nint CardTitleFontHandle => 0;

        public int Scale(int value) => value;
        public int LogicalWidth(int value) => value;
        public void ApplyDefaultFont(nint control) { }
        public nint CreateOwnerDrawButton(string text, nint parent, int id, int x, nint instance) => 0;
        public bool RegisterContainerWindow(nint window) => false;
        public void UnregisterContainerWindow(nint window) { }
        public void DestroyWindowIfPresent(nint window) { }
        public bool PresentVisible(nint control, int show) => false;
        public bool PresentEnabled(nint control, bool enabled) => false;
        public bool PresentText(nint control, string text) => false;
        public bool PresentBounds(nint control, int x, int y, int width, int height, bool repaint) => false;
        public bool PresentInvalidation(nint control, nint rectangle, bool erase) => false;
        public void ResetDetailsUpdates() { }
        public void ResetFavoriteUpdates() { }
        public void SynchronizeNativeSelection() { }
        public void SynchronizeMainPaneVisibility() { }
        public void UpdatePaneSplitterVisibility() { }
        public void ProcessCommand(nuint wParam, nint lParam) { }
        public nint PrepareStaticControl(nuint deviceContext, nint pane) => 0;
        public nint PrepareDarkControl(nuint deviceContext, nint pane) => 0;
        public void EnqueueUiAction(Action action) { }
        public void RestoreViewFocusFromSettings() { }
        public void FillRectangle(nint deviceContext, Win32Rect rectangle, int color) { }
        public void DrawModernSurface(nint dc, Win32Rect box, int background, int border, int radius) { }
        public void DrawCardLabel(nint dc, Win32Rect bounds, string text, int color, nint font, nint control, uint flags) { }
        public void DrawNavigationIcon(nint dc, int index, Win32Rect bounds, bool disabled) { }
        public bool DrawRecordingModeButton(Win32DrawItem drawItem, string label, bool selected, bool dangerous = false) => false;
        public bool DrawYellowPageItem(Win32DrawItem draw) => false;
        public string ReadControlText(nint control) => string.Empty;
        public void PaintTopInputFrame(nint window) { }
    }
}

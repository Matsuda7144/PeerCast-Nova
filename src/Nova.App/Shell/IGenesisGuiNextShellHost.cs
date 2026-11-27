using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// 分離されたタブ／機能コントローラーがネイティブ UI ホスト（AppShellState）とやり取りするための最小契約。
/// </summary>
internal interface IGenesisGuiNextShellHost
{
    nint Owner { get; }
    nint Sidebar { get; }
    nint ListView { get; }
    nint DetailsPane { get; }
    nint DetailsTitle { get; }
    nint DetailsDescription { get; }
    nint DetailsMetadata { get; }
    nint DetailsIdentity { get; }
    int DetailsTextWidth { get; }
    nint PublishingStartButton { get; }
    nint PublishingInterruptButton { get; }
    nint PublishingFinishButton { get; }
    GenesisGuiNextTabsViewModel Tabs { get; }
    GenesisMainViewModel ViewModel { get; }
    bool IsModern { get; }
    bool IsDetached { get; }
    bool IsSettingsPageOpen { get; }
    uint LayoutDpi { get; }
    int? DetailsHeight { get; }
    bool LayoutInProgress { get; set; }
    nint HoverControl { get; }
    nint MainBrush { get; }
    nint TopBrush { get; }
    nint InputBrush { get; }
    nint VisualFontHandle { get; }
    nint CardTitleFontHandle { get; }

    int Scale(int value);
    int LogicalWidth(int value);
    void ApplyDefaultFont(nint control);
    nint CreateOwnerDrawButton(string text, nint parent, int id, int x, nint instance);
    bool RegisterContainerWindow(nint window);
    void UnregisterContainerWindow(nint window);
    void DestroyWindowIfPresent(nint window);
    bool PresentVisible(nint control, int show);
    bool PresentEnabled(nint control, bool enabled);
    bool PresentText(nint control, string text);
    bool PresentBounds(nint control, int x, int y, int width, int height, bool repaint);
    bool PresentInvalidation(nint control, nint rectangle, bool erase);
    void ResetDetailsUpdates();
    void ResetFavoriteUpdates();
    void SynchronizeNativeSelection();
    void UpdatePaneSplitterVisibility();
    void ProcessCommand(nuint wParam, nint lParam);
    nint PrepareStaticControl(nuint deviceContext, nint pane);
    nint PrepareDarkControl(nuint deviceContext, nint pane);
    void EnqueueUiAction(Action action);
    void RestoreViewFocusFromSettings();
    void FillRectangle(nint deviceContext, Win32Rect rectangle, int color);
    void DrawModernSurface(nint dc, Win32Rect box, int background, int border, int radius);
    void DrawCardLabel(nint dc, Win32Rect bounds, string text, int color, nint font, nint control, uint flags);
    void DrawNavigationIcon(nint dc, int index, Win32Rect bounds, bool disabled);
    bool DrawRecordingModeButton(Win32DrawItem drawItem, string label, bool selected, bool dangerous = false);
    bool DrawYellowPageItem(Win32DrawItem draw);
    string ReadControlText(nint control);
    void PaintTopInputFrame(nint window);
}

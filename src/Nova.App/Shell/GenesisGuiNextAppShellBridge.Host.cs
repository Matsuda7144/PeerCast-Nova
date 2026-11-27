using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState : IGenesisGuiNextShellHost
    {
        nint IGenesisGuiNextShellHost.Owner => _owner;
        nint IGenesisGuiNextShellHost.Sidebar => _sidebar;
        nint IGenesisGuiNextShellHost.ListView => _listView;
        nint IGenesisGuiNextShellHost.DetailsPane => _detailsPane;
        nint IGenesisGuiNextShellHost.DetailsTitle => _detailsTitle;
        nint IGenesisGuiNextShellHost.DetailsDescription => _detailsDescription;
        nint IGenesisGuiNextShellHost.DetailsMetadata => _detailsMetadata;
        nint IGenesisGuiNextShellHost.DetailsIdentity => _detailsIdentity;
        int IGenesisGuiNextShellHost.DetailsTextWidth => _detailsTextWidth;
        nint IGenesisGuiNextShellHost.PublishingStartButton => _publishingStartButton;
        nint IGenesisGuiNextShellHost.PublishingInterruptButton => _publishingInterruptButton;
        nint IGenesisGuiNextShellHost.PublishingFinishButton => _publishingFinishButton;
        GenesisGuiNextTabsViewModel IGenesisGuiNextShellHost.Tabs => _tabs;
        GenesisMainViewModel IGenesisGuiNextShellHost.ViewModel => _viewModel;
        bool IGenesisGuiNextShellHost.IsModern => IsModern;
        bool IGenesisGuiNextShellHost.IsDetached => Volatile.Read(ref _detached) != 0;
        bool IGenesisGuiNextShellHost.IsSettingsPageOpen => _settings.IsOpen;
        uint IGenesisGuiNextShellHost.LayoutDpi => _layoutDpi;
        int? IGenesisGuiNextShellHost.DetailsHeight => _detailsHeight;

        bool IGenesisGuiNextShellHost.LayoutInProgress
        {
            get => LayoutInProgress;
            set => LayoutInProgress = value;
        }

        nint IGenesisGuiNextShellHost.HoverControl => _hoverControl;
        nint IGenesisGuiNextShellHost.MainBrush => _mainBrush;
        nint IGenesisGuiNextShellHost.TopBrush => _topBrush;
        nint IGenesisGuiNextShellHost.InputBrush => _inputBrush;
        nint IGenesisGuiNextShellHost.VisualFontHandle => _visualFont?.Handle ?? 0;
        nint IGenesisGuiNextShellHost.CardTitleFontHandle => _cardTitleFont?.Handle ?? 0;

        int IGenesisGuiNextShellHost.Scale(int value) => Scale(value);
        int IGenesisGuiNextShellHost.LogicalWidth(int value) => LogicalWidth(value);
        void IGenesisGuiNextShellHost.ApplyDefaultFont(nint control) => ApplyDefaultFont(control);

        nint IGenesisGuiNextShellHost.CreateOwnerDrawButton(string text, nint parent, int id, int x, nint instance) =>
            CreateRecordingModeButton(text, parent, id, x, instance);

        bool IGenesisGuiNextShellHost.RegisterContainerWindow(nint window) => StatesByContainer.TryAdd(window, this);
        void IGenesisGuiNextShellHost.UnregisterContainerWindow(nint window) => _ = StatesByContainer.TryRemove(window, out _);
        void IGenesisGuiNextShellHost.DestroyWindowIfPresent(nint window) => DestroyWindowIfPresent(window);
        bool IGenesisGuiNextShellHost.PresentVisible(nint control, int show) => PresentVisible(control, show);
        bool IGenesisGuiNextShellHost.PresentEnabled(nint control, bool enabled) => PresentEnabled(control, enabled);
        bool IGenesisGuiNextShellHost.PresentText(nint control, string text) => PresentText(control, text);

        bool IGenesisGuiNextShellHost.PresentBounds(nint control, int x, int y, int width, int height, bool repaint) =>
            PresentBounds(control, x, y, width, height, repaint);

        bool IGenesisGuiNextShellHost.PresentInvalidation(nint control, nint rectangle, bool erase) =>
            PresentInvalidation(control, rectangle, erase);

        void IGenesisGuiNextShellHost.ResetDetailsUpdates() => _detailsUpdates.Reset();
        void IGenesisGuiNextShellHost.ResetFavoriteUpdates() => _favoriteUpdates.Reset();
        void IGenesisGuiNextShellHost.SynchronizeNativeSelection() => SynchronizeNativeSelection();
        void IGenesisGuiNextShellHost.UpdatePaneSplitterVisibility() => UpdatePaneSplitterVisibility();
        void IGenesisGuiNextShellHost.ProcessCommand(nuint wParam, nint lParam) => ProcessCommand(wParam, lParam);
        nint IGenesisGuiNextShellHost.PrepareStaticControl(nuint deviceContext, nint pane) => PrepareStaticControl(deviceContext, pane);
        nint IGenesisGuiNextShellHost.PrepareDarkControl(nuint deviceContext, nint pane) => PrepareDarkControl(deviceContext, pane);
        void IGenesisGuiNextShellHost.EnqueueUiAction(Action action) => PostPublishingUi(action);

        void IGenesisGuiNextShellHost.RestoreViewFocusFromSettings()
        {
            _utilityNavigationSelection.SelectView(_tabs, _tabs.SelectedTab);
            _ = InvalidateRect(_sidebar, 0, true);
            _ = SetFocus(_listView);
        }
        void IGenesisGuiNextShellHost.FillRectangle(nint deviceContext, Win32Rect rectangle, int color) =>
            FillRectangle(deviceContext, rectangle, color);

        void IGenesisGuiNextShellHost.DrawModernSurface(nint dc, Win32Rect box, int background, int border, int radius) =>
            DrawModernSurface(dc, box, background, border, radius);

        void IGenesisGuiNextShellHost.DrawCardLabel(nint dc, Win32Rect bounds, string text, int color, nint font, nint control, uint flags) =>
            DrawCardLabel(dc, bounds, text, color, font, control, flags);

        void IGenesisGuiNextShellHost.DrawNavigationIcon(nint dc, int index, Win32Rect bounds, bool disabled) =>
            DrawNavigationIcon(dc, index, bounds, disabled);

        bool IGenesisGuiNextShellHost.DrawRecordingModeButton(Win32DrawItem drawItem, string label, bool selected, bool dangerous) =>
            DrawRecordingModeButton(drawItem, label, selected, dangerous);

        bool IGenesisGuiNextShellHost.DrawYellowPageItem(Win32DrawItem draw) => DrawYellowPageItem(draw);
        string IGenesisGuiNextShellHost.ReadControlText(nint control) => ReadCardControlText(control);
        void IGenesisGuiNextShellHost.PaintTopInputFrame(nint window) => PaintTopInputFrame(window);
    }
}

using PeerCast.Nova.Windows;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// 配信タブ（ガイド・インライン基本/詳細設定・パネルスタイル）を管理するコントローラー。
/// </summary>
internal sealed partial class GenesisPublishingTabController(IGenesisGuiNextShellHost host)
{
    private readonly IGenesisGuiNextShellHost _host = host ?? throw new ArgumentNullException(nameof(host));
    private nint _publishingSubtitle, _publishingObsTitle, _publishingBasicTitle, _publishingOptionsTitle;
    private nint _publishingBasic, _publishingOptions, _publishingDiagnostic;
    private (bool Expanded, int Width, int Height, uint Dpi, GenesisGuiNextAppShellPlacement.WindowRectangle Bounds)? _publishingPanePlacement;
    private nint _publishingServerLabel, _publishingServerValue, _publishingKeyLabel, _publishingKeyValue;
    private nint _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy, _publishingConnectionState, _publishingInstructions;
    private nint _publishingSettingsButton;
    private GenesisGuiNextPublishingSettingsWindowHost? _publishingSettingsWindow;

    private nint[] PublishingPageTitles => _host.IsModern ? [_publishingSubtitle, _publishingObsTitle, _publishingBasicTitle, _publishingOptionsTitle] : [];
    private nint[] PublishingPageValues => _host.IsModern ? [_publishingBasic, _publishingOptions, _publishingDiagnostic] : [];
    private nint[] PublishingPageControls => [.. PublishingPageTitles, .. PublishingPageValues, .. PublishingEditorControls];

    internal nint ConnectionStateWindow => _publishingConnectionState;

    internal bool IsGuideAvailable => new[] { _publishingServerLabel, _publishingServerValue,
        _publishingKeyLabel, _publishingKeyValue, _publishingServerCopy, _publishingKeyReveal,
        _publishingKeyCopy, _publishingConnectionState, _publishingInstructions, _publishingSettingsButton }.Concat(PublishingPageControls).All(handle => handle != 0);

    internal bool IsDarkReadOnlyControl(nint control) => UsesPanel &&
        (control == _publishingInstructions || control == _publishingBasic ||
         control == _publishingOptions || control == _publishingDiagnostic);

    internal void CreateGuide(nint instance)
    {
        nint Text(string text) => CreateWindowExW(0, "STATIC", text, WindowStyleChild,
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0);
        // STATIC values have no native Edit copy/context menu that could
        // bypass identity validation. Buttons copy the full, unelided value.
        nint Value() => CreateWindowExW(0, "STATIC", string.Empty,
            WindowStyleChild | 0x000C | 0x4000, // no-wrap, end ellipsis
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0);
        _publishingServerLabel = Text("サーバー");
        _publishingServerValue = Value();
        _publishingServerCopy = _host.CreateOwnerDrawButton("コピー", _host.DetailsPane, PublishingServerCopyControlId, 0, instance);
        _publishingKeyLabel = Text("ストリームキー");
        _publishingKeyValue = Value();
        _publishingConnectionState = CreateWindowExW(0, "STATIC", string.Empty,
            WindowStyleChild | StaticStyleOwnerDraw, 0, 0, 1, 1, _host.DetailsPane,
            (nint)PublishingConnectionStateControlId, instance, 0);
        _publishingKeyReveal = _host.CreateOwnerDrawButton("表示", _host.DetailsPane, PublishingKeyRevealControlId, 0, instance);
        _publishingKeyCopy = _host.CreateOwnerDrawButton("コピー", _host.DetailsPane, PublishingKeyCopyControlId, 0, instance);
        _publishingSettingsButton = _host.CreateOwnerDrawButton("配信設定…", _host.DetailsPane, PublishingSettingsControlId, 0, instance);
        // Keep wrapped instructions readable at narrow widths without growing
        // into lifecycle feedback. This read-only field never contains a key.
        _publishingInstructions = CreateWindowExW(0, "EDIT", NovaObsPublishingConnectionPresentation.Instructions,
            WindowStyleChild | (_host.IsModern ? WindowStyleTabStop : 0) | 0x00200000 | 0x0800 | 0x0004 | 0x0040,
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0);
        CreatePage(instance);
        foreach (var control in PublishingGuideControls())
        {
            _host.ApplyDefaultFont(control);
            _ = ShowWindow(control, ShowWindowHide);
        }
    }

    internal void AttachEditor()
    {
        if (_host.IsModern && _host.Tabs.PublishingSettings is { } settings) _publishingEditor = new(settings);
    }

    internal void Detach()
    {
        _publishingEditor?.Dispose();
        if (_publishingEditPane != 0 && IsWindow(_publishingEditPane)) _ = RemoveWindowSubclass(_publishingEditPane, PublishingEditorProcedure, PublishingEditorSubclassId);
        if (_publishingAdvancedPane != 0 && IsWindow(_publishingAdvancedPane)) _ = RemoveWindowSubclass(_publishingAdvancedPane, PublishingEditorProcedure, PublishingEditorSubclassId);
        _publishingSettingsWindow?.Dispose();
        _publishingSettingsWindow = null;
        _ = ControllersByOwner.TryRemove(KeyValuePair.Create(_host.Owner, this));
    }

    internal void ResetPanePlacement() => _publishingPanePlacement = null;

    private nint[] PublishingGuideControls() => [_publishingServerLabel, _publishingServerValue,
        _publishingKeyLabel, _publishingKeyValue, _publishingServerCopy, _publishingKeyReveal,
        _publishingKeyCopy, _publishingConnectionState, _publishingInstructions, _publishingSettingsButton];

    private void CreatePage(nint instance)
    {
        if (!_host.IsModern) return;
        nint Text(string text) => CreateWindowExW(0, "STATIC", text, WindowStyleChild,
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0);
        nint Value() => CreateWindowExW(0, "EDIT", string.Empty,
            WindowStyleChild | WindowStyleTabStop | 0x00200000 | 0x0800 | 0x0004 | 0x0040,
            0, 0, 1, 1, _host.DetailsPane, 0, instance, 0); // native read-only multiline, keyboard-selectable and scrollable
        _publishingSubtitle = Text("OBS入力・接続情報・配信操作を確認します。公開範囲は配信設定に従います。");
        _publishingObsTitle = Text("OBS接続情報");
        _publishingBasicTitle = Text("基本設定（保存済み）");
        _publishingOptionsTitle = Text("詳細設定・適用状況");
        _publishingBasic = Value(); _publishingOptions = Value(); _publishingDiagnostic = Value();
        CreateInlineSettings(instance);
        foreach (var h in PublishingPageControls) _host.ApplyDefaultFont(h);
    }

    internal void SynchronizePageBounds()
    {
        if (!_host.IsModern || !GetClientRect(_host.Owner, out var client) ||
            !GenesisMainWindowLayout.TryResolve(client.Right, client.Bottom, _host.LayoutDpi, out _, out _, out var list, out var details, _host.DetailsHeight)) return;
        var expanded = UsesPanel;
        var bounds = GenesisMainWindowLayout.ResolveDetailsPaneBounds(expanded, list, details);
        var placement = (expanded, client.Right, client.Bottom, _host.LayoutDpi, bounds);
        if (_publishingPanePlacement != placement)
        {
            _publishingPanePlacement = placement;
            var wasInProgress = _host.LayoutInProgress;
            _host.LayoutInProgress = true;
            try
            {
                _ = MoveWindow(_host.DetailsPane, _host.Scale(bounds.Left), _host.Scale(bounds.Top), _host.Scale(bounds.Width), _host.Scale(bounds.Height), true);
                _host.ResetDetailsUpdates();
            }
            finally { _host.LayoutInProgress = wasInProgress; }
        }
        _host.SynchronizeMainPaneVisibility();
    }

    private void SynchronizePage(bool visible)
    {
        foreach (var h in PublishingPageTitles.Concat(PublishingPageValues.Where(h => h != _publishingBasic && h != _publishingOptions)))
            _ = _host.PresentVisible(h, visible && _host.IsModern ? ShowWindowShowNoActivate : ShowWindowHide);
        if (_host.IsModern) SynchronizeInline(visible);
        if (!visible || !_host.IsModern) return;
        var settings = _host.Tabs.PublishingSettings;
        var summary = GenesisGuiNextPublishingPageSummary.Create(settings?.Saved, settings?.Applied, settings?.LoadFailed == true,
            _host.Tabs.PublishingRows.Count > 0 ? _host.Tabs.PublishingRows[0] : null, _host.Tabs.PublishingCommands.DiagnosticText, _host.Tabs.PublishingConnectionFeedback);
        _ = _host.PresentText(_publishingBasic, summary.Basic);
        _ = _host.PresentText(_publishingOptions, summary.Options);
        _ = _host.PresentText(_publishingDiagnostic, (_publishingEditor is { } editor ? editor.Result.Message +
            InlineErrorLabel(editor.Result.Field) + "\r\n" : "") + summary.Diagnostic);
        _ = _host.PresentVisible(_host.DetailsIdentity, ShowWindowHide);
    }

    internal bool ProcessGuideCommand(int commandId, nint control)
    {
        if (_host.Tabs.SelectedTab != GenesisGuiNextTab.Publishing) return false;
        if (ProcessAdvancedCommand(commandId, control)) return true;
        if (ProcessInlineCommand(commandId, control)) return true;
        if (commandId == PublishingSettingsControlId && control == _publishingSettingsButton)
        {
            if (!CanLeaveEditor()) return true;
            if (_host.Tabs.PublishingSettings is { } settings)
            {
                _publishingSettingsWindow ??= new(_host.Owner, settings, () => _host.Tabs.PublishingCommands.CanInterrupt || _host.Tabs.PublishingCommands.CanFinish);
                SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(_host.Owner, _publishingSettingsWindow.Show(), GenesisGuiNextSettingsWindowKind.Publishing);
            }
            return true;
        }
        if (commandId == PublishingKeyRevealControlId && control == _publishingKeyReveal)
        { _ = _host.Tabs.TogglePublishingKeyAsync(); return true; }
        var key = commandId == PublishingKeyCopyControlId && control == _publishingKeyCopy;
        if (!key && !(commandId == PublishingServerCopyControlId && control == _publishingServerCopy)) return false;
        _ = _host.Tabs.CopyPublishingConnectionAsync(key, (value, current) =>
            !_host.IsDetached &&
            GenesisChannelContextMenuNative.CopySourceToClipboard(_host.DetailsPane, value, current));
        return true;
    }

    internal void SynchronizeGuide(bool visible)
    {
        SynchronizePage(visible);
        foreach (var control in PublishingGuideControls())
            _ = _host.PresentVisible(control, visible ? ShowWindowShowNoActivate : ShowWindowHide);
        if (!visible)
        {
            _ = _host.PresentText(_publishingKeyValue, NovaObsPublishingConnectionPresentation.Mask);
            _ = _host.PresentText(_publishingServerValue, string.Empty);
            return;
        }
        var width = GetClientRect(_host.DetailsPane, out var rectangle) ? _host.LogicalWidth(rectangle.Right - rectangle.Left) : _host.DetailsTextWidth;
        if (UsesPanel) PlacePanel(width);
        else
        {
            var layout = GenesisGuiNextPublishingGuideLayout.ForWidth(width);
            void Place(nint control, GenesisGuiNextPublishingGuideRectangle bounds) =>
                _ = _host.PresentBounds(control, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
            Place(_host.DetailsTitle, layout.Title);
            Place(_host.PublishingStartButton, layout.Start);
            Place(_host.PublishingFinishButton, layout.Finish);
            Place(_host.PublishingInterruptButton, layout.Interrupt);
            Place(_publishingConnectionState, layout.State);
            Place(_publishingSettingsButton, layout.Settings);
            Place(_publishingServerLabel, GenesisGuiNextPublishingGuideLayout.ServerLabel);
            Place(_publishingServerValue, layout.Server);
            Place(_publishingServerCopy, layout.ServerCopy);
            Place(_publishingKeyLabel, GenesisGuiNextPublishingGuideLayout.KeyLabel);
            Place(_publishingKeyValue, layout.Key);
            Place(_publishingKeyReveal, layout.Reveal);
            Place(_publishingKeyCopy, layout.KeyCopy);
            Place(_publishingInstructions, layout.Instructions);
            Place(_host.DetailsIdentity, layout.Feedback);
        }
        _ = _host.PresentEnabled(_publishingSettingsButton, _host.Tabs.PublishingSettings is not null);
        _ = _host.PresentVisible(_host.DetailsDescription, ShowWindowHide);
        _ = _host.PresentVisible(_host.DetailsMetadata, ShowWindowHide);
        _ = _host.PresentText(_publishingInstructions, NovaObsPublishingConnectionPresentation.Instructions + "\r\n" +
            (_host.Tabs.PublishingRows.Count == 1 ? _host.Tabs.PublishingRows[0].LanRelayText : "LAN PCP Relay: 状態未確認"));
        var presentation = _host.Tabs.PublishingConnection;
        _ = _host.PresentText(_publishingServerValue, presentation.ServerText);
        _ = _host.PresentText(_publishingKeyValue, _host.Tabs.PublishingKeyDisplay);
        _ = _host.PresentText(_publishingKeyReveal, _host.Tabs.PublishingKeyRevealed ? "隠す" : "表示");
        _ = _host.PresentEnabled(_publishingServerCopy, presentation.CanCopyServer);
        _ = _host.PresentEnabled(_publishingKeyReveal, presentation.CanUseKey);
        _ = _host.PresentEnabled(_publishingKeyCopy, presentation.CanUseKey);
        foreach (var control in new[] { _publishingConnectionState, _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy })
            _ = _host.PresentInvalidation(control, 0, true);
    }

    internal bool DrawGuide(Win32DrawItem drawItem)
    {
        if (IsOwnerDrawButton(drawItem.ControlType) && drawItem.ControlId is
            PublishingServerCopyControlId or PublishingKeyCopyControlId or PublishingKeyRevealControlId or PublishingSettingsControlId)
            return _host.DrawRecordingModeButton(drawItem,
                drawItem.ControlId == PublishingSettingsControlId ? "配信設定…" :
                drawItem.ControlId == PublishingKeyRevealControlId ? _host.Tabs.PublishingKeyRevealed ? "隠す" : "表示" : "コピー", false);
        if (!IsOwnerDrawStatic(drawItem.ControlType) || drawItem.ControlId != PublishingConnectionStateControlId) return false;
        _host.FillRectangle(drawItem.DeviceContext, drawItem.ItemRectangle, BackgroundColor);
        _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
        _ = SetTextColor(drawItem.DeviceContext, _host.Tabs.PublishingConnection.Tone switch
        {
            NovaObsPublishingTone.Active => 0x008BC935,
            NovaObsPublishingTone.Warning => WarningTextColor,
            NovaObsPublishingTone.Special => 0x00FF8DAD,
            _ => AccentColor,
        });
        var rectangle = drawItem.ItemRectangle;
        _ = DrawTextW(drawItem.DeviceContext, _host.Tabs.PublishingConnection.StateText, -1, ref rectangle,
            DrawTextNoPrefix | DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
        return true;
    }
}

internal readonly record struct GenesisGuiNextPublishingGuideRectangle(int Left, int Top, int Width, int Height)
{
    internal int Right => Left + Width;
    internal int Bottom => Top + Height;
}

/// <summary>Logical coordinates in the main-window shell.</summary>
internal readonly record struct GenesisGuiNextPublishingGuideLayout(int ContentWidth, int ServerWidth, int KeyWidth, int CopyLeft, int RevealLeft)
{
    internal GenesisGuiNextPublishingGuideRectangle Title => new(12, 8, Math.Max(1, ContentWidth - 264), 24);
    internal GenesisGuiNextPublishingGuideRectangle Start => new(ContentWidth - 240, 8, 78, 26);
    internal GenesisGuiNextPublishingGuideRectangle Finish => new(ContentWidth - 156, 8, 78, 26);
    internal GenesisGuiNextPublishingGuideRectangle Interrupt => new(ContentWidth - 72, 8, 78, 26);
    internal GenesisGuiNextPublishingGuideRectangle State => new(12, 36, Math.Max(1, ContentWidth - 120), 22);
    internal GenesisGuiNextPublishingGuideRectangle Settings => new(ContentWidth - 100, 36, 112, 22);
    internal static GenesisGuiNextPublishingGuideRectangle ServerLabel => new(12, 62, 76, 24);
    internal GenesisGuiNextPublishingGuideRectangle Server => new(94, 60, ServerWidth, 24);
    internal GenesisGuiNextPublishingGuideRectangle ServerCopy => new(CopyLeft, 60, 72, 26);
    internal static GenesisGuiNextPublishingGuideRectangle KeyLabel => new(12, 94, 82, 24);
    internal GenesisGuiNextPublishingGuideRectangle Key => new(94, 92, KeyWidth, 24);
    internal GenesisGuiNextPublishingGuideRectangle Reveal => new(RevealLeft, 92, 64, 26);
    internal GenesisGuiNextPublishingGuideRectangle KeyCopy => new(CopyLeft, 92, 72, 26);
    internal GenesisGuiNextPublishingGuideRectangle Instructions => new(12, 124, ContentWidth, 60);
    internal GenesisGuiNextPublishingGuideRectangle Feedback => new(12, 188, ContentWidth, 34);

    internal static bool ShowRelayControls(GenesisGuiNextTab tab) => tab != GenesisGuiNextTab.Publishing;

    internal static GenesisGuiNextPublishingGuideLayout ForWidth(int width) =>
        new(Math.Max(1, width - 24), Math.Max(1, width - 186), Math.Max(1, width - 258),
            Math.Max(94, width - 84), Math.Max(94, width - 156));
}

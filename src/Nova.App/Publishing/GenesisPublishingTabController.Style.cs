using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisPublishingTabController
{
    internal bool UsesPanel => !_host.IsDetached &&
        GenesisGuiNextPublishingModernLayout.Applies(_host.IsModern, _host.Tabs.SelectedTab);

    private nint[] PublishingButtons => [_host.PublishingStartButton, _host.PublishingFinishButton, _host.PublishingInterruptButton,
        _publishingSettingsButton, _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy, .. PublishingEditorButtons];

    private nint[] PublishingGuideTexts => [_publishingServerLabel, _publishingServerValue, _publishingKeyLabel, _publishingKeyValue];

    private nint[] PublishingTexts => [_host.DetailsTitle, _host.DetailsIdentity, .. PublishingGuideTexts, .. PublishingPageTitles, .. _publishingInputLabels, _publishingEditNote, .. _publishingAdvancedLabels, _publishingAdvancedHint];

    internal nint[] VisualControls => ((nint[])[.. PublishingButtons, .. PublishingGuideTexts, _publishingInstructions, .. PublishingPageControls]).Distinct().ToArray();

    private void PlacePanel(int width)
    {
        var layout = new GenesisGuiNextPublishingModernLayout(width, GetClientRect(_host.DetailsPane, out var pane) ? _host.LogicalWidth(pane.Bottom) : 384, _publishingAdvancedExpanded, _publishingEditor?.Editing == true && !_publishingEditAdvanced);
        void Place(nint control, GenesisGuiNextPublishingGuideRectangle bounds) =>
            _ = _host.PresentBounds(control, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
        Place(_host.DetailsTitle, layout.Title);
        Place(_host.PublishingStartButton, layout.Start);
        Place(_host.PublishingFinishButton, layout.Finish);
        Place(_host.PublishingInterruptButton, layout.Interrupt);
        Place(_publishingSettingsButton, layout.Settings);
        Place(_publishingConnectionState, layout.State);
        Place(_publishingServerLabel, layout.ServerLabel);
        Place(_publishingServerValue, layout.Server);
        Place(_publishingServerCopy, layout.ServerCopy);
        Place(_publishingKeyLabel, layout.KeyLabel);
        Place(_publishingKeyValue, layout.Key);
        Place(_publishingKeyReveal, layout.Reveal);
        Place(_publishingKeyCopy, layout.KeyCopy);
        Place(_publishingInstructions, layout.Instructions);
        Place(_publishingSubtitle, layout.Subtitle);
        Place(_publishingDiagnostic, layout.Feedback);
        Place(_publishingObsTitle, layout.ObsTitle);
        Place(_publishingBasicTitle, layout.BasicTitle);
        Place(_publishingOptionsTitle, layout.OptionsTitle); Place(_publishingOptions, layout.Options);
        PlaceInline(layout);
    }

    internal void RefreshFocus(nint control)
    {
        if (UsesPanel && (control == _publishingInstructions || PublishingPageValues.Contains(control)))
            _ = RedrawWindow(_host.DetailsPane, 0, 0, 0x0001 | 0x0004);
    }

    internal bool PaintPanel(nint pane, nint dc)
    {
        if (!UsesPanel || pane != _host.DetailsPane || !GetClientRect(pane, out var client)) return false;
        _ = FillRect(dc, ref client, _host.MainBrush);
        var layout = new GenesisGuiNextPublishingModernLayout(_host.LogicalWidth(client.Right), _host.LogicalWidth(client.Bottom), _publishingAdvancedExpanded, _publishingEditor?.Editing == true && !_publishingEditAdvanced);
        foreach (var card in new[] { layout.StatusCard, layout.ObsCard, layout.BasicCard, layout.OptionsCard })
        {
            var box = new Win32Rect { Left = _host.Scale(card.Left), Top = _host.Scale(card.Top), Right = _host.Scale(card.Right), Bottom = _host.Scale(card.Bottom) };
            _host.DrawModernSurface(dc, box, GenesisGuiNextDesign.Panel, GenesisGuiNextDesign.Border, _host.Scale(12));
        }
        foreach (var area in new[] { (layout.Instructions, _publishingInstructions), (layout.Basic, _publishingBasic),
            (layout.AdvancedViewport, _publishingOptions), (layout.Feedback, _publishingDiagnostic) })
        {
            if (!IsWindowVisible(area.Item2)) continue;
            using var focus = new GenesisVisualPen(dc, _host.Scale(1), GetFocus() == area.Item2 ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, _host.Scale(area.Item1.Left) - 1, _host.Scale(area.Item1.Top) - 1, _host.Scale(area.Item1.Right) + 1, _host.Scale(area.Item1.Bottom) + 1);
        }
        return true;
    }

    internal bool PaintText(nint control, uint message)
    {
        if (!UsesPanel || !PublishingTexts.Contains(control)) return false;
        if (message == 0x0014) return true;
        if (message != WindowMessagePaint) return false;
        var dc = BeginPaint(control, out var paint);
        try
        {
            if (dc == 0 || !GetClientRect(control, out var box)) return true;
            var value = control == _publishingServerValue || control == _publishingKeyValue;
            _ = FillRect(dc, ref box, value ? _host.InputBrush : control == _host.DetailsTitle || control == _publishingSubtitle ? _host.MainBrush : _host.TopBrush);
            if (value)
            {
                using var pen = new GenesisVisualPen(dc, _host.Scale(1), GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
                box.Left += _host.Scale(6); box.Right -= _host.Scale(6);
            }
            if (control == _host.DetailsTitle)
            {
                _host.DrawNavigationIcon(dc, 6, new Win32Rect { Left = 0, Top = 0, Right = _host.Scale(24), Bottom = _host.Scale(24) }, false);
                box.Left += _host.Scale(34);
            }
            var title = control == _host.DetailsTitle || PublishingPageTitles.Contains(control) && control != _publishingSubtitle;
            var feedback = control == _host.DetailsIdentity;
            // Read the existing STATIC text only. Key disclosure/masking is still owned by the VM.
            _host.DrawCardLabel(dc, box, _host.ReadControlText(control), title || value ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText,
                title ? _host.CardTitleFontHandle : _host.VisualFontHandle, control,
                feedback || control == _publishingEditNote || control == _publishingAdvancedHint ? GenesisGuiNextPublishingModernLayout.FeedbackFlags : GenesisGuiNextPublishingModernLayout.SingleLineFlags);
            return true;
        }
        finally { _ = EndPaint(control, ref paint); }
    }

    internal bool DrawPanelItem(Win32DrawItem draw)
    {
        if (!UsesPanel) return false;
        if (IsOwnerDrawButton(draw.ControlType) && PublishingButtons.Contains(draw.ControlWindow))
        {
            var colors = GenesisGuiNextDesign.PublishingButtonColors(draw.ControlId == PublishingStartControlId,
                draw.ControlId == PublishingInterruptControlId, (draw.ItemState & DrawItemDisabled) != 0,
                _host.HoverControl == draw.ControlWindow, (draw.ItemState & 1) != 0, (draw.ItemState & DrawItemFocused) != 0);
            var lifecycle = draw.ControlWindow == _host.PublishingStartButton || draw.ControlWindow == _host.PublishingFinishButton ||
                draw.ControlWindow == _host.PublishingInterruptButton || draw.ControlWindow == _publishingSettingsButton;
            _host.FillRectangle(draw.DeviceContext, draw.ItemRectangle, lifecycle ? GenesisGuiNextDesign.Background : GenesisGuiNextDesign.Panel);
            _host.DrawModernSurface(draw.DeviceContext, draw.ItemRectangle, colors.Background, colors.Border, _host.Scale(8));
            var box = draw.ItemRectangle;
            box.Left += _host.Scale(5); box.Right -= _host.Scale(5);
            _host.DrawCardLabel(draw.DeviceContext, box, _host.ReadControlText(draw.ControlWindow), colors.Text,
                _host.VisualFontHandle, draw.ControlWindow, GenesisGuiNextPublishingModernLayout.SingleLineFlags | 1);
            if ((draw.ItemState & DrawItemFocused) != 0)
            {
                box.Top += _host.Scale(3); box.Bottom -= _host.Scale(3);
                _ = DrawFocusRect(draw.DeviceContext, ref box);
            }
            return true;
        }
        if (!IsOwnerDrawStatic(draw.ControlType) || draw.ControlId != PublishingConnectionStateControlId) return false;
        _ = FillRect(draw.DeviceContext, ref draw.ItemRectangle, _host.TopBrush);
        var presentation = _host.Tabs.PublishingConnection;
        var stateBox = draw.ItemRectangle;
        var middle = (stateBox.Top + stateBox.Bottom) / 2;
        using (var marker = new GenesisVisualPen(draw.DeviceContext, _host.Scale(2), GenesisGuiNextDesign.PublishingStatusColor(presentation.Tone)))
            _ = Ellipse(draw.DeviceContext, stateBox.Left + _host.Scale(2), middle - _host.Scale(7), stateBox.Left + _host.Scale(16), middle + _host.Scale(7));
        stateBox.Left += _host.Scale(28);
        _host.DrawCardLabel(draw.DeviceContext, stateBox, presentation.StateText,
            GenesisGuiNextDesign.PublishingStatusColor(presentation.Tone), _host.CardTitleFontHandle,
            draw.ControlWindow, GenesisGuiNextPublishingModernLayout.SingleLineFlags);
        return true;
    }
}

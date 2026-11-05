namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private bool UsesPublishingPanel => Volatile.Read(ref _detached) == 0 &&
            GenesisGuiNextPublishingModernLayout.Applies(IsModern, _tabs.SelectedTab);
        private nint[] PublishingButtons => [_publishingStartButton, _publishingFinishButton, _publishingInterruptButton,
            _publishingSettingsButton, _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy];
        private nint[] PublishingGuideTexts => [_publishingServerLabel, _publishingServerValue, _publishingKeyLabel, _publishingKeyValue];
        private nint[] PublishingTexts => [_detailsTitle, _detailsIdentity, .. PublishingGuideTexts];
        private nint[] PublishingVisualControls => [.. PublishingButtons, .. PublishingGuideTexts, _publishingInstructions];

        private void PlacePublishingPanel(int width)
        {
            var layout = new GenesisGuiNextPublishingModernLayout(width);
            void Place(nint control, GenesisGuiNextPublishingGuideRectangle bounds) =>
                _ = PresentBounds(control, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
            Place(_detailsTitle, layout.Title);
            Place(_publishingStartButton, layout.Start);
            Place(_publishingFinishButton, layout.Finish);
            Place(_publishingInterruptButton, layout.Interrupt);
            Place(_publishingSettingsButton, layout.Settings);
            Place(_publishingConnectionState, layout.State);
            Place(_publishingServerLabel, GenesisGuiNextPublishingModernLayout.ServerLabel);
            Place(_publishingServerValue, layout.Server);
            Place(_publishingServerCopy, layout.ServerCopy);
            Place(_publishingKeyLabel, GenesisGuiNextPublishingModernLayout.KeyLabel);
            Place(_publishingKeyValue, layout.Key);
            Place(_publishingKeyReveal, layout.Reveal);
            Place(_publishingKeyCopy, layout.KeyCopy);
            Place(_publishingInstructions, layout.Instructions);
            Place(_detailsIdentity, layout.Feedback);
        }

        internal void RefreshPublishingFocus(nint control)
        {
            if (UsesPublishingPanel && control == _publishingInstructions)
                _ = RedrawWindow(_detailsPane, 0, 0, 0x0001 | 0x0004);
        }

        private bool PaintPublishingPanel(nint pane, nint dc)
        {
            if (!UsesPublishingPanel || pane != _detailsPane || !GetClientRect(pane, out var client)) return false;
            _ = FillRect(dc, ref client, _mainBrush);
            var panel = client;
            panel.Left += Scale(6); panel.Top += Scale(6); panel.Right -= Scale(6); panel.Bottom -= Scale(6);
            _ = FillRect(dc, ref panel, _topBrush);
            using var pen = new VisualPen(dc, Scale(1), GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, panel.Left, panel.Top, panel.Right, panel.Bottom);
            Line(dc, Scale(16), Scale(178), client.Right - Scale(16), Scale(178));
            var area = new GenesisGuiNextPublishingModernLayout(LogicalWidth(client.Right)).Instructions;
            using var focus = new VisualPen(dc, Scale(1), GetFocus() == _publishingInstructions ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, Scale(area.Left) - 1, Scale(area.Top) - 1, Scale(area.Right) + 1, Scale(area.Bottom) + 1);
            return true;
        }

        internal bool PaintPublishingText(nint control, uint message)
        {
            if (!UsesPublishingPanel || !PublishingTexts.Contains(control)) return false;
            if (message == 0x0014) return true;
            if (message != WindowMessagePaint) return false;
            var dc = BeginCardPaint(control, out var paint);
            try
            {
                if (dc == 0 || !GetClientRect(control, out var box)) return true;
                var value = control == _publishingServerValue || control == _publishingKeyValue;
                _ = FillRect(dc, ref box, value ? _inputBrush : _topBrush);
                if (value)
                {
                    using var pen = new VisualPen(dc, Scale(1), GenesisGuiNextDesign.Border);
                    _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
                    box.Left += Scale(6); box.Right -= Scale(6);
                }
                var title = control == _detailsTitle;
                var feedback = control == _detailsIdentity;
                // Read the existing STATIC text only. Key disclosure/masking is still owned by the VM.
                DrawCardLabel(dc, box, ReadCardControlText(control), title || value ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText,
                    title ? _cardTitleFont?.Handle ?? 0 : _visualFont?.Handle ?? 0, control,
                    feedback ? GenesisGuiNextPublishingModernLayout.FeedbackFlags : GenesisGuiNextPublishingModernLayout.SingleLineFlags);
                return true;
            }
            finally { _ = EndCardPaint(control, ref paint); }
        }

        private bool DrawPublishingPanelItem(DrawItem draw)
        {
            if (!UsesPublishingPanel) return false;
            if (IsOwnerDrawButton(draw.ControlType) && PublishingButtons.Contains(draw.ControlWindow))
            {
                var colors = GenesisGuiNextDesign.PublishingButtonColors(draw.ControlId == PublishingStartControlId,
                    draw.ControlId == PublishingInterruptControlId, (draw.ItemState & DrawItemDisabled) != 0,
                    _hoverControl == draw.ControlWindow, (draw.ItemState & 1) != 0, (draw.ItemState & DrawItemFocused) != 0);
                FillRectangle(draw.DeviceContext, draw.ItemRectangle, colors.Background);
                using var pen = new VisualPen(draw.DeviceContext, Scale(1), colors.Border);
                var box = draw.ItemRectangle;
                _ = GdiRectangle(draw.DeviceContext, box.Left, box.Top, box.Right, box.Bottom);
                box.Left += Scale(5); box.Right -= Scale(5);
                DrawCardLabel(draw.DeviceContext, box, ReadCardControlText(draw.ControlWindow), colors.Text,
                    _visualFont?.Handle ?? 0, draw.ControlWindow, GenesisGuiNextPublishingModernLayout.SingleLineFlags | 1);
                if ((draw.ItemState & DrawItemFocused) != 0)
                {
                    box.Top += Scale(3); box.Bottom -= Scale(3);
                    _ = DrawFocusRect(draw.DeviceContext, ref box);
                }
                return true;
            }
            if (!IsOwnerDrawStatic(draw.ControlType) || draw.ControlId != PublishingConnectionStateControlId) return false;
            _ = FillRect(draw.DeviceContext, ref draw.ItemRectangle, _topBrush);
            var presentation = _tabs.PublishingConnection;
            DrawCardLabel(draw.DeviceContext, draw.ItemRectangle, presentation.StateText,
                GenesisGuiNextDesign.PublishingStatusColor(presentation.Tone), _visualFont?.Handle ?? 0,
                draw.ControlWindow, GenesisGuiNextPublishingModernLayout.SingleLineFlags);
            return true;
        }
    }
}

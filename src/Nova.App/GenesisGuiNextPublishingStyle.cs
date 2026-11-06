using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private bool UsesPublishingPanel => Volatile.Read(ref _detached) == 0 &&
            GenesisGuiNextPublishingModernLayout.Applies(IsModern, _tabs.SelectedTab);
        private nint[] PublishingButtons => [_publishingStartButton, _publishingFinishButton, _publishingInterruptButton,
            _publishingSettingsButton, _publishingServerCopy, _publishingKeyReveal, _publishingKeyCopy, .. PublishingEditorButtons];
        private nint[] PublishingGuideTexts => [_publishingServerLabel, _publishingServerValue, _publishingKeyLabel, _publishingKeyValue];
        private nint[] PublishingTexts => [_detailsTitle, _detailsIdentity, .. PublishingGuideTexts, .. PublishingPageTitles, .. _publishingInputLabels, _publishingEditNote, .. _publishingAdvancedLabels, _publishingAdvancedHint];
        private nint[] PublishingVisualControls => ((nint[])[.. PublishingButtons, .. PublishingGuideTexts, _publishingInstructions, .. PublishingPageControls]).Distinct().ToArray();

        private void PlacePublishingPanel(int width)
        {
            var layout = new GenesisGuiNextPublishingModernLayout(width, GetClientRect(_detailsPane, out var pane) ? LogicalWidth(pane.Bottom) : 384, _publishingAdvancedExpanded, _publishingEditor?.Editing == true && !_publishingEditAdvanced);
            void Place(nint control, GenesisGuiNextPublishingGuideRectangle bounds) =>
                _ = PresentBounds(control, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
            Place(_detailsTitle, layout.Title);
            Place(_publishingStartButton, layout.Start);
            Place(_publishingFinishButton, layout.Finish);
            Place(_publishingInterruptButton, layout.Interrupt);
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
            PlacePublishingInline(layout);
        }

        internal void RefreshPublishingFocus(nint control)
        {
            if (UsesPublishingPanel && (control == _publishingInstructions || PublishingPageValues.Contains(control)))
                _ = RedrawWindow(_detailsPane, 0, 0, 0x0001 | 0x0004);
        }

        private bool PaintPublishingPanel(nint pane, nint dc)
        {
            if (!UsesPublishingPanel || pane != _detailsPane || !GetClientRect(pane, out var client)) return false;
            _ = FillRect(dc, ref client, _mainBrush);
            var layout = new GenesisGuiNextPublishingModernLayout(LogicalWidth(client.Right), LogicalWidth(client.Bottom), _publishingAdvancedExpanded, _publishingEditor?.Editing == true && !_publishingEditAdvanced);
            foreach (var card in new[] { layout.StatusCard, layout.ObsCard, layout.BasicCard, layout.OptionsCard })
            {
                var box = new Rectangle { Left = Scale(card.Left), Top = Scale(card.Top), Right = Scale(card.Right), Bottom = Scale(card.Bottom) };
                DrawPublishingSurface(dc, box, GenesisGuiNextDesign.Panel, GenesisGuiNextDesign.Border, Scale(12));
            }
            foreach (var area in new[] { (layout.Instructions, _publishingInstructions), (layout.Basic, _publishingBasic),
                (layout.AdvancedViewport, _publishingOptions), (layout.Feedback, _publishingDiagnostic) })
            {
                if (!PublishingIsWindowVisible(area.Item2)) continue;
                using var focus = new VisualPen(dc, Scale(1), GetFocus() == area.Item2 ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, Scale(area.Item1.Left) - 1, Scale(area.Item1.Top) - 1, Scale(area.Item1.Right) + 1, Scale(area.Item1.Bottom) + 1);
            }
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
                _ = FillRect(dc, ref box, value ? _inputBrush : control == _detailsTitle || control == _publishingSubtitle ? _mainBrush : _topBrush);
                if (value)
                {
                    using var pen = new VisualPen(dc, Scale(1), GenesisGuiNextDesign.Border);
                    _ = GdiRectangle(dc, box.Left, box.Top, box.Right, box.Bottom);
                    box.Left += Scale(6); box.Right -= Scale(6);
                }
                if (control == _detailsTitle)
                {
                    DrawNavigationIcon(dc, 6, new Rectangle { Left = 0, Top = 0, Right = Scale(24), Bottom = Scale(24) }, false);
                    box.Left += Scale(34);
                }
                var title = control == _detailsTitle || PublishingPageTitles.Contains(control) && control != _publishingSubtitle;
                var feedback = control == _detailsIdentity;
                // Read the existing STATIC text only. Key disclosure/masking is still owned by the VM.
                DrawCardLabel(dc, box, ReadCardControlText(control), title || value ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText,
                    title ? _cardTitleFont?.Handle ?? 0 : _visualFont?.Handle ?? 0, control,
                    feedback || control == _publishingEditNote || control == _publishingAdvancedHint ? GenesisGuiNextPublishingModernLayout.FeedbackFlags : GenesisGuiNextPublishingModernLayout.SingleLineFlags);
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
                var lifecycle = draw.ControlWindow == _publishingStartButton || draw.ControlWindow == _publishingFinishButton ||
                    draw.ControlWindow == _publishingInterruptButton || draw.ControlWindow == _publishingSettingsButton;
                FillRectangle(draw.DeviceContext, draw.ItemRectangle, lifecycle ? GenesisGuiNextDesign.Background : GenesisGuiNextDesign.Panel);
                DrawPublishingSurface(draw.DeviceContext, draw.ItemRectangle, colors.Background, colors.Border, Scale(8));
                var box = draw.ItemRectangle;
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
            var stateBox = draw.ItemRectangle;
            var middle = (stateBox.Top + stateBox.Bottom) / 2;
            using (var marker = new VisualPen(draw.DeviceContext, Scale(2), GenesisGuiNextDesign.PublishingStatusColor(presentation.Tone)))
                _ = Ellipse(draw.DeviceContext, stateBox.Left + Scale(2), middle - Scale(7), stateBox.Left + Scale(16), middle + Scale(7));
            stateBox.Left += Scale(28);
            DrawCardLabel(draw.DeviceContext, stateBox, presentation.StateText,
                GenesisGuiNextDesign.PublishingStatusColor(presentation.Tone), _cardTitleFont?.Handle ?? 0,
                draw.ControlWindow, GenesisGuiNextPublishingModernLayout.SingleLineFlags);
            return true;
        }
        private void DrawPublishingSurface(nint dc, Rectangle box, int background, int border, int radius)
        {
            using var pen = new VisualPen(dc, Scale(1), border);
            var brush = CreateSolidBrush((uint)background);
            if (brush == 0) return;
            var previous = SelectObject(dc, brush);
            try { _ = PublishingRoundRect(dc, box.Left, box.Top, box.Right, box.Bottom, radius, radius); }
            finally { _ = SelectObject(dc, previous); _ = DeleteObject(brush); }
        }
    }

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PublishingIsWindowVisible(nint window);

    [DllImport("gdi32.dll", EntryPoint = "RoundRect")]
    private static extern bool PublishingRoundRect(nint dc, int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
}

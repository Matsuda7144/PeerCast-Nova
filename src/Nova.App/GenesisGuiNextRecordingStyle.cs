namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private bool UsesRecordingPanel => Volatile.Read(ref _detached) == 0 &&
            GenesisGuiNextRecordingLayout.Applies(IsModern, _tabs.SelectedTab);
        private nint[] RecordingToolbarControls => [_activeRecordingsModeButton, _recordingLibraryModeButton,
            _recordingRulesModeButton, _recordingHistoryModeButton, _recordingLibraryRefreshButton, _recordingHistoryRefreshButton,
            _createRuleButton, _editRuleButton, _toggleRuleButton, _deleteRuleButton, _moveRuleUpButton, _moveRuleDownButton];
        private nint[] RecordingVisualControls => [.. RecordingToolbarControls, _recordingHistoryDetails];

        internal void RefreshRecordingFocus(nint control)
        {
            if (UsesRecordingPanel && control == _recordingHistoryDetails)
                _ = RedrawWindow(_detailsPane, 0, 0, 0x0001 | 0x0004);
        }

        private void PlaceRecordingPanel()
        {
            if (!GetClientRect(_detailsPane, out var client)) return;
            var layout = new GenesisGuiNextRecordingLayout(LogicalWidth(client.Right), _tabs.RecordingPageMode);
            void Place(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle box) =>
                _ = PresentBounds(control, box.Left, box.Top, box.Width, box.Height, true);
            ReadOnlySpan<int> slots = [0, 1, 2, 3, 4, 4, 4, 5, 6, 7, 8, 9];
            var controls = RecordingToolbarControls;
            for (var i = 0; i < controls.Length; i++) Place(controls[i], GenesisGuiNextRecordingLayout.Toolbar(slots[i]));
            Place(_detailsTitle, layout.Title);
            Place(_detailsDescription, layout.Description);
            Place(_detailsMetadata, layout.Metadata);
            Place(_detailsIdentity, layout.Identity);
            Place(_detailsRecordingStatus, layout.Status);
            Place(_recordingHistoryDetails, layout.History);
            Place(_detailsRecordButton, GenesisGuiNextRecordingLayout.Record(_allowAutomaticShown));
            Place(_detailsStopRecordingButton, GenesisGuiNextRecordingLayout.Stop);
            Place(_detailsCopyButton, GenesisGuiNextRecordingLayout.Copy);
            Place(_relayStart, GenesisGuiNextRecordingLayout.RelayStart);
            Place(_relayStop, GenesisGuiNextRecordingLayout.RelayStop);
            Place(_relayStatus, layout.RelayStatus);
        }

        private bool PaintRecordingPanel(nint pane, nint dc)
        {
            if (!UsesRecordingPanel || pane != _detailsPane || !GetClientRect(pane, out var client)) return false;
            _ = FillRect(dc, ref client, _mainBrush);
            var panel = client;
            panel.Left += Scale(6); panel.Top += Scale(6); panel.Right -= Scale(6); panel.Bottom -= Scale(6);
            _ = FillRect(dc, ref panel, _topBrush);
            using var pen = new VisualPen(dc, Scale(1), GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, panel.Left, panel.Top, panel.Right, panel.Bottom);
            Line(dc, Scale(16), Scale(46), client.Right - Scale(16), Scale(46));
            Line(dc, Scale(16), Scale(182), client.Right - Scale(16), Scale(182));
            if (_tabs.IsRecordingHistoryPage)
            {
                var area = new GenesisGuiNextRecordingLayout(LogicalWidth(client.Right), _tabs.RecordingPageMode).History;
                using var focus = new VisualPen(dc, Scale(1), GetFocus() == _recordingHistoryDetails ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, Scale(area.Left) - 1, Scale(area.Top) - 1, Scale(area.Right) + 1, Scale(area.Bottom) + 1);
            }
            return true;
        }

        internal bool PaintRecordingText(nint control, uint message)
        {
            if (!UsesRecordingPanel || !CardTexts.Contains(control)) return false;
            if (message == 0x0014) return true;
            if (message != WindowMessagePaint) return false;
            var dc = BeginCardPaint(control, out var paint);
            try
            {
                if (dc == 0 || !GetClientRect(control, out var box)) return true;
                _ = FillRect(dc, ref box, _topBrush);
                var title = control == _detailsTitle;
                var wrap = control == _detailsDescription || _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.Rules && !title;
                DrawCardLabel(dc, box, ReadCardControlText(control), title ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText,
                    title ? _cardTitleFont?.Handle ?? 0 : _visualFont?.Handle ?? 0, control,
                    wrap ? 0x0010u | 0x8000 : DrawTextSingleLine | 0x8000);
                return true;
            }
            finally { _ = EndCardPaint(control, ref paint); }
        }

        private bool DrawRecordingPanelItem(DrawItem draw)
        {
            if (!UsesRecordingPanel) return false;
            if (IsOwnerDrawButton(draw.ControlType) &&
                (RecordingToolbarControls.Contains(draw.ControlWindow) ||
                    draw.ControlId is DetailsRecordControlId or DetailsStopRecordingControlId or DetailsCopyControlId or RelayStartControlId or RelayStopControlId))
            {
                var selected = draw.ControlId switch
                {
                    ActiveRecordingsModeControlId => _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.ActiveRecordings,
                    RecordingLibraryModeControlId => _tabs.IsRecordingLibraryPage,
                    RecordingRulesModeControlId => IsRulesPageActive(),
                    RecordingHistoryModeControlId => _tabs.IsRecordingHistoryPage,
                    _ => false,
                };
                var disabled = (draw.ItemState & DrawItemDisabled) != 0;
                var colors = GenesisGuiNextDesign.RecordingButtonColors(selected, disabled,
                    _hoverControl == draw.ControlWindow, (draw.ItemState & 1) != 0, (draw.ItemState & DrawItemFocused) != 0);
                FillRectangle(draw.DeviceContext, draw.ItemRectangle, colors.Background);
                using var pen = new VisualPen(draw.DeviceContext, Scale(1), colors.Border);
                var box = draw.ItemRectangle;
                _ = GdiRectangle(draw.DeviceContext, box.Left, box.Top, box.Right, box.Bottom);
                if (selected)
                {
                    var underline = box; underline.Top = underline.Bottom - Scale(2);
                    FillRectangle(draw.DeviceContext, underline, GenesisGuiNextDesign.Accent);
                }
                box.Left += Scale(5); box.Right -= Scale(5);
                DrawCardLabel(draw.DeviceContext, box, ReadCardControlText(draw.ControlWindow), colors.Text,
                    _visualFont?.Handle ?? 0, draw.ControlWindow, DrawTextSingleLine | DrawTextVerticalCenter | 1 | 0x8000);
                if ((draw.ItemState & DrawItemFocused) != 0)
                {
                    box.Top += Scale(3); box.Bottom -= Scale(3);
                    _ = DrawFocusRect(draw.DeviceContext, ref box);
                }
                return true;
            }
            if (IsOwnerDrawStatic(draw.ControlType) && draw.ControlId == RelayStatusControlId)
            {
                _ = FillRect(draw.DeviceContext, ref draw.ItemRectangle, _topBrush);
                var relay = _viewModel.RelaySnapshot;
                DrawCardLabel(draw.DeviceContext, draw.ItemRectangle,
                    _viewModel.RelayActivation is null ? "ローカルRelay: 利用できません" : relay.Text,
                    relay.Error == GenesisRelayActivationError.None ? GenesisGuiNextDesign.SecondaryText : GenesisGuiNextDesign.Warning,
                    _visualFont?.Handle ?? 0, draw.ControlWindow, DrawTextSingleLine | DrawTextVerticalCenter | 0x8000);
                return true;
            }
            if (!IsRecordingStatusDraw(draw.ControlType, draw.ControlId)) return false;
            _ = FillRect(draw.DeviceContext, ref draw.ItemRectangle, _topBrush);
            var status = GetCurrentRecordingPresentation();
            var color = _tabs.IsRecordingHistoryPage ? _historyStatusPresentation.IsWarning ? GenesisGuiNextDesign.Warning : GenesisGuiNextDesign.SecondaryText :
                _tabs.IsRecordingLibraryPage ? GenesisGuiNextDesign.SecondaryText :
                GenesisGuiNextDesign.RecordingStatusColor(status.IsFailure, status.IsRecording, status.IsTransitioning);
            DrawCardLabel(draw.DeviceContext, draw.ItemRectangle, ReadCardControlText(draw.ControlWindow), color,
                _visualFont?.Handle ?? 0, draw.ControlWindow, _tabs.RecordingPageMode == GenesisGuiNextRecordingPageMode.ActiveRecordings
                    ? DrawTextSingleLine | DrawTextVerticalCenter | 0x8000 : 0x0010u | 0x8000);
            return true;
        }
    }
}

using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private GenesisUiFontLease? _cardTitleFont;
        private bool UsesChannelCard => Volatile.Read(ref _detached) == 0 &&
            GenesisGuiNextChannelCardLayout.Applies(IsModern, _tabs.SelectedTab);
        private nint[] CardTexts => [_detailsTitle, _detailsDescription, _detailsMetadata, _detailsIdentity];
        private nint[] CardButtons => [_detailsRecordButton, _detailsStopRecordingButton, _detailsCopyButton,
            _playButton, _stopButton, _relayStart, _relayStop];
        private nint[] VisualSubclassControls => [.. VisualButtons, .. CardTexts, .. CardButtons, .. TopVisualControls, .. RecordingVisualControls, .. PublishingVisualControls, .. SettingsVisualControls, GetParent(_listView), _owner];

        private void PlaceChannelCard()
        {
            if (!GetClientRect(_detailsPane, out var client)) return;
            var layout = new GenesisGuiNextChannelCardLayout(LogicalWidth(client.Right));
            void Place(nint control, GenesisGuiNextAppShellPlacement.WindowRectangle bounds) =>
                _ = PresentBounds(control, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
            Place(_detailsTitle, layout.Title);
            Place(_detailsDescription, layout.Description);
            Place(_detailsMetadata, layout.Metadata);
            Place(_detailsIdentity, layout.Identity);
            Place(_detailsRecordingStatus, layout.RecordingStatus);
            Place(_detailsRecordButton, GenesisGuiNextChannelCardLayout.Record);
            Place(_detailsStopRecordingButton, GenesisGuiNextChannelCardLayout.Stop);
            Place(_detailsCopyButton, GenesisGuiNextChannelCardLayout.Copy);
            Place(_relayStart, GenesisGuiNextChannelCardLayout.RelayStart);
            Place(_relayStop, GenesisGuiNextChannelCardLayout.RelayStop);
            Place(_relayStatus, layout.RelayStatus);
        }

        private bool PaintChannelCard(nint pane, nint dc)
        {
            if (!UsesChannelCard || pane != _detailsPane || !GetClientRect(pane, out var client)) return false;
            _ = FillRect(dc, ref client, _mainBrush);
            var card = client;
            card.Left += Scale(6); card.Right -= Scale(6);
            card.Top += Scale(6); card.Bottom -= Scale(6);
            _ = FillRect(dc, ref card, _topBrush);
            using var border = new VisualPen(dc, Scale(1), GenesisGuiNextDesign.Border);
            _ = GdiRectangle(dc, card.Left, card.Top, card.Right, card.Bottom);
            var layout = new GenesisGuiNextChannelCardLayout(LogicalWidth(client.Right));
            Line(dc, Scale(layout.DividerLeft), Scale(18), Scale(layout.DividerLeft), Scale(90));
            return true;
        }

        // Existing STATIC controls keep their text/accessibility and ordinary native
        // rendering on other tabs. Only this card's WM_PAINT is rendered here.
        internal bool PaintChannelCardText(nint control, uint message)
        {
            if (!UsesChannelCard || !CardTexts.Contains(control)) return false;
            if (message == 0x0014) return true;
            if (message != WindowMessagePaint) return false;
            var dc = BeginCardPaint(control, out var paint);
            try
            {
                if (dc == 0 || !GetClientRect(control, out var bounds)) return true;
                _ = FillRect(dc, ref bounds, _topBrush);
                var title = control == _detailsTitle;
                var wrap = control == _detailsDescription || control == _detailsMetadata;
                DrawCardLabel(dc, bounds, ReadCardControlText(control),
                    title ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText,
                    title ? _cardTitleFont?.Handle ?? 0 : control == _detailsIdentity ? 0 : _visualFont?.Handle ?? 0,
                    control, wrap ? 0x0010u | 0x8000 : DrawTextSingleLine | 0x8000);
                return true;
            }
            finally { _ = EndCardPaint(control, ref paint); }
        }

        private bool DrawChannelCardItem(DrawItem draw)
        {
            if (!UsesChannelCard) return false;
            if (IsOwnerDrawButton(draw.ControlType) && CardButtons.Contains(draw.ControlWindow))
            {
                var disabled = (draw.ItemState & DrawItemDisabled) != 0;
                var pressed = (draw.ItemState & 1) != 0;
                var recording = draw.ControlId is DetailsRecordControlId or DetailsStopRecordingControlId;
                var play = draw.ControlId == PlayControlId;
                var colors = GenesisGuiNextDesign.CardButtonColors(play, recording, disabled,
                    _hoverControl == draw.ControlWindow, pressed);
                FillRectangle(draw.DeviceContext, draw.ItemRectangle, colors.Background);
                using var pen = new VisualPen(draw.DeviceContext, Scale(1), colors.Border);
                var box = draw.ItemRectangle;
                _ = GdiRectangle(draw.DeviceContext, box.Left, box.Top, box.Right, box.Bottom);
                var x = box.Left + Scale(12); var y = (box.Top + box.Bottom) / 2;
                if (draw.ControlId == DetailsRecordControlId)
                    _ = Ellipse(draw.DeviceContext, x - Scale(4), y - Scale(4), x + Scale(4), y + Scale(4));
                else if (draw.ControlId is DetailsStopRecordingControlId or StopControlId or RelayStopControlId)
                    _ = GdiRectangle(draw.DeviceContext, x - Scale(4), y - Scale(4), x + Scale(4), y + Scale(4));
                else if (draw.ControlId is PlayControlId or RelayStartControlId)
                {
                    Line(draw.DeviceContext, x - Scale(3), y - Scale(5), x + Scale(5), y);
                    Line(draw.DeviceContext, x + Scale(5), y, x - Scale(3), y + Scale(5));
                    Line(draw.DeviceContext, x - Scale(3), y + Scale(5), x - Scale(3), y - Scale(5));
                }
                else
                {
                    _ = GdiRectangle(draw.DeviceContext, x - Scale(4), y - Scale(5), x + Scale(3), y + Scale(3));
                    Line(draw.DeviceContext, x - Scale(1), y + Scale(6), x + Scale(6), y + Scale(6));
                }
                box.Left += Scale(25); box.Right -= Scale(5);
                DrawCardLabel(draw.DeviceContext, box, ReadCardControlText(draw.ControlWindow), colors.Text,
                    _visualFont?.Handle ?? 0, draw.ControlWindow, DrawTextSingleLine | DrawTextVerticalCenter | 0x8000 | 1);
                if ((draw.ItemState & DrawItemFocused) != 0)
                {
                    var focus = draw.ItemRectangle;
                    focus.Left += Scale(3); focus.Right -= Scale(3); focus.Top += Scale(3); focus.Bottom -= Scale(3);
                    _ = DrawFocusRect(draw.DeviceContext, ref focus);
                }
                return true;
            }
            if (IsRecordingStatusDraw(draw.ControlType, draw.ControlId) ||
                IsOwnerDrawStatic(draw.ControlType) && draw.ControlId == RelayStatusControlId)
            {
                _ = FillRect(draw.DeviceContext, ref draw.ItemRectangle, _topBrush);
                var recording = draw.ControlId == DetailsRecordingStatusControlId;
                var status = GetCurrentRecordingPresentation();
                var relay = _viewModel.RelaySnapshot;
                var color = recording ? status.IsFailure ? GenesisGuiNextDesign.Warning :
                    status.IsRecording || status.IsTransitioning ? GenesisGuiNextDesign.Recording : GenesisGuiNextDesign.SecondaryText :
                    relay.Error == GenesisRelayActivationError.None ? GenesisGuiNextDesign.SecondaryText : GenesisGuiNextDesign.Warning;
                var text = recording ? status.DisplayText :
                    _viewModel.RelayActivation is null ? "ローカルRelay: 利用できません" : relay.Text;
                DrawCardLabel(draw.DeviceContext, draw.ItemRectangle, text, color, recording ? _visualFont?.Handle ?? 0 : 0,
                    draw.ControlWindow, recording ? DrawTextSingleLine | 0x8000 : 0x0010u | 0x8000);
                return true;
            }
            return false;
        }

        private static string ReadCardControlText(nint control)
        {
            var buffer = new char[8193];
            var length = GetWindowTextW(control, buffer, buffer.Length);
            return new string(buffer, 0, Math.Max(0, length));
        }

        private static void DrawCardLabel(nint dc, Rectangle bounds, string text, int color, nint font, nint control, uint flags)
        {
            using var inheritedFont = GenesisWindowUiFont.SelectForDrawing(dc, control);
            var previous = font == 0 ? 0 : SelectObject(dc, font);
            try
            {
                _ = SetBkMode(dc, TransparentBackgroundMode);
                _ = SetTextColor(dc, color);
                _ = DrawTextW(dc, text, -1, ref bounds, flags | DrawTextNoPrefix);
            }
            finally { if (previous != 0) _ = SelectObject(dc, previous); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CardPaint
    {
        public nint Dc;
        public int Erase;
        public Rectangle Bounds;
        public int Restore, Incremental;
        public uint Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7, Reserved8;
    }
    [DllImport("user32.dll", EntryPoint = "BeginPaint")]
    private static extern nint BeginCardPaint(nint window, out CardPaint paint);
    [DllImport("user32.dll", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndCardPaint(nint window, ref CardPaint paint);
}

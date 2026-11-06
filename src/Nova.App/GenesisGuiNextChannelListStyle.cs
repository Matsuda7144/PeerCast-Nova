using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

// Presentation only: retained native rows and their captured identities remain authoritative.
internal static class GenesisGuiNextChannelListStyle
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Channels;
    internal const int RowHeight = 32;
    internal static int[] ColumnWidths(int width)
    {
        // Leave room for the native report-view edge so an exact fit does not add a tiny scrollbar.
        width = Math.Max(0, width - 4);
        var name = Math.Clamp((width - 420) * 40 / 100, 200, 400);
        return [name, Math.Max(260, width - name - 420), 112, 80, 84, 56, 88];
    }
}

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private bool UsesChannelListStyle => GenesisGuiNextChannelListStyle.Applies(IsModern, _tabs.SelectedTab);
        private (bool Channels, int Width, uint Dpi, int Columns)? _channelListStyle;
        private int _rowImageHeight;
        private GenesisUiFontLease? _channelNameFont;

        private void SynchronizeChannelListStyle()
        {
            if (!IsModern || !GetClientRect(_listView, out var client)) return;
            var channels = UsesChannelListStyle;
            if (_footerShowsModes != !channels) PlaceFooter();
            var header = SendMessageW(_listView, 0x101F, 0, 0);
            var count = (int)SendMessageW(header, 0x1200, 0, 0);
            var style = (channels, LogicalWidth(client.Right), _layoutDpi, count);
            if (_channelListStyle == style) return;
            if (channels && count != 7) return; // A tab render may still be replacing its columns.
            if (channels)
            {
                var buffer = Marshal.AllocHGlobal(512);
                try
                {
                    var item = new VisualHeaderItem { Mask = 2, Text = buffer, TextCharacters = 256 };
                    if (SendHeaderItem(header, 0x120B, 0, ref item) == 0 || Marshal.PtrToStringUni(buffer) != GenesisChannelListPresentation.Columns[0].Header) return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            // Dedicated tabs own different columns. Never replace their definitions.
            if (count == 7 &&
                (channels || _channelListStyle?.Channels == true && _tabs.SelectedTab is GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites or GenesisGuiNextTab.Playing))
            {
                var widths = channels ? GenesisGuiNextChannelListStyle.ColumnWidths(style.Item2) : GenesisChannelListPresentation.Columns.Select(c => c.PreferredWidth).ToArray();
                for (var i = 0; i < widths.Length; i++) _ = SendMessageW(_listView, 0x101E, (nuint)i, Scale(widths[i]));
            }
            SetModernRowHeight(channels ? GenesisGuiNextChannelListStyle.RowHeight : GenesisGuiNextDesign.RowImageHeight, _layoutDpi);
            _channelListStyle = style;
        }

        internal void PaintChannelListFrame()
        {
            if (!UsesChannelListStyle || !GetWindowRect(_listView, out var bounds)) return;
            var dc = GetWindowDC(_listView);
            if (dc == 0) return;
            try
            {
                // Repaint only the existing CLIENTEDGE; keep client bounds, native scrollbars and hit testing.
                for (var inset = 0; inset < Scale(2); inset++)
                {
                    using var pen = new VisualPen(dc, 1, inset == 0 ? GenesisGuiNextDesign.Border : GenesisGuiNextDesign.Panel);
                    _ = GdiRectangle(dc, inset, inset, bounds.Right - bounds.Left - inset, bounds.Bottom - bounds.Top - inset);
                }
            }
            finally { _ = ReleaseDC(_listView, dc); }
        }

        private void SetModernRowHeight(int height, uint dpi)
        {
            var pixels = GenesisMainWindowLayout.Scale(height, dpi);
            if (_rowImageHeight == pixels) return;
            // The existing 1px native spacer controls report-row height. No image content or thumbnail area.
            var images = ImageList_Create(1, pixels, 0x0020, 1, 1);
            if (images == 0) return;
            var previous = SendMessageW(_listView, 0x1003, 1, images);
            if (_rowImages == 0) _originalImages = previous;
            else _ = ImageList_Destroy(_rowImages);
            _rowImages = images; _rowImageHeight = pixels;
        }

        private bool DrawChannelListCell(nint pointer, out nint result)
        {
            result = 0;
            if (!UsesChannelListStyle) return false;
            var draw = Marshal.PtrToStructure<ListViewCustomDraw>(pointer);
            if (draw.Draw.DrawStage != 0x00030001 || draw.Draw.ItemSpec > int.MaxValue || draw.SubItem is < 0 or > 6) return false;
            var row = GenesisChannelListRenderedChannels.FindRow(_listView, (int)draw.Draw.ItemSpec);
            if (row is null) return false;
            var box = new Rectangle { Top = draw.SubItem, Left = 0 };
            if (SendHeaderRectangle(_listView, 0x1038, draw.Draw.ItemSpec, ref box) == 0) return false;
            // Subitem zero's BOUNDS covers the whole row; restrict it to the first column.
            if (draw.SubItem == 0) box.Right = box.Left + (int)SendMessageW(_listView, 0x101D, 0, 0);
            var selected = (int)draw.Draw.ItemSpec == _paintSelectedRow;
            FillRectangle(draw.Draw.DeviceContext, box, GenesisGuiNextDesign.RowBackground(selected, (int)draw.Draw.ItemSpec == _hoverRow));
            box.Left += Scale(10); box.Right -= Scale(10);
            var values = GenesisChannelListPresentation.CreateRowValues(row.DisplayChannel);
            var numeric = GenesisChannelListPresentation.Columns[draw.SubItem].Alignment == GenesisChannelColumnAlignment.Right;
            DrawCardLabel(draw.Draw.DeviceContext, box, values[draw.SubItem],
                selected || draw.SubItem == 0 ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText,
                draw.SubItem == 0 ? _channelNameFont?.Handle ?? 0 : _visualFont?.Handle ?? 0, _listView,
                DrawTextSingleLine | DrawTextVerticalCenter | 0x8000u | (numeric ? 2u : 0u));
            result = 4; // CDRF_SKIPDEFAULT affects pixels only, retaining selection, sorting and activation.
            return true;
        }
    }
}

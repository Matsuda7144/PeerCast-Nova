using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

// Presentation only: retained native rows and their captured identities remain authoritative.
internal static class GenesisGuiNextChannelListStyle
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern &&
        tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites or
            GenesisGuiNextTab.Playing or GenesisGuiNextTab.Recording or GenesisGuiNextTab.Logs;
    internal static bool FrameApplies(bool modern, GenesisGuiNextTab tab) => Applies(modern, tab);
    internal const int RowHeight = 32;
    internal static int[] ColumnWidths(int width, bool fitNarrow = false)
    {
        // Leave room for the native report-view edge so an exact fit does not add a tiny scrollbar.
        width = Math.Max(0, width - 4);
        var name = Math.Clamp((width - 420) * 40 / 100, 200, 400);
        return [name, Math.Max(fitNarrow ? 180 : 260, width - name - 420), 112, 80, 84, 56, 88];
    }
}

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private bool UsesChannelListStyle => GenesisGuiNextChannelListStyle.Applies(IsModern, _tabs.SelectedTab);
        private (int Width, uint Dpi, bool FitNarrow)? _channelListStyle;
        private IReadOnlyList<GenesisNativeListColumnPresentation>? _paintColumns;
        private IReadOnlyList<string[]> _paintValues = [];
        private int _rowImageHeight;
        private GenesisUiFontLease? _channelNameFont;

        private IReadOnlyList<GenesisNativeListColumnPresentation> SelectedListColumns =>
            GenesisGuiNextRecordingListPresentation.Columns(_tabs.SelectedTab, _tabs.RecordingPageMode);
        private bool UsesStyledChannelColumns => UsesChannelListStyle &&
            GenesisGuiNextRecordingListPresentation.UsesChannelColumns(SelectedListColumns);

        internal void UpdateListPaintData(IReadOnlyList<GenesisNativeListColumnPresentation>? columns, IReadOnlyList<string[]> values)
        {
            if (!IsModern) return;
            if (!ReferenceEquals(_paintColumns, columns)) _channelListStyle = null;
            _paintColumns = columns;
            _paintValues = values;
            SynchronizeChannelListStyle();
        }

        private void SynchronizeChannelListStyle()
        {
            if (!IsModern) return;
            SetModernRowHeight(UsesChannelListStyle ? GenesisGuiNextChannelListStyle.RowHeight : GenesisGuiNextDesign.RowImageHeight, _layoutDpi);
            if (!UsesStyledChannelColumns || !ReferenceEquals(_paintColumns, SelectedListColumns) ||
                !GetClientRect(_listView, out var client)) return;
            var width = LogicalWidth(client.Right);
            // The added tabs can give the description a few pixels to fit beside a native vertical scrollbar.
            var style = (Width: width, Dpi: _layoutDpi, FitNarrow: width < 884 && _tabs.SelectedTab != GenesisGuiNextTab.Channels);
            if (_channelListStyle == style) return;
            _channelListStyle = style;
            var widths = GenesisGuiNextChannelListStyle.ColumnWidths(style.Width, style.FitNarrow);
            for (var i = 0; i < widths.Length; i++) _ = SendMessageW(_listView, 0x101E, (nuint)i, Scale(widths[i]));
        }

        internal void PaintModernListFrame()
        {
            if (!GenesisGuiNextChannelListStyle.FrameApplies(IsModern, _tabs.SelectedTab) || !GetWindowRect(_listView, out var bounds)) return;
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
            if (!UsesChannelListStyle || _paintColumns is not { } columns ||
                !ReferenceEquals(columns, SelectedListColumns)) return false;
            var draw = Marshal.PtrToStructure<Win32ListViewCustomDraw>(pointer);
            if (draw.Draw.DrawStage != 0x00030001 || draw.Draw.ItemSpec >= (nuint)_paintValues.Count ||
                (uint)draw.SubItem >= (uint)columns.Count) return false;
            var values = _paintValues[(int)draw.Draw.ItemSpec];
            if (values.Length != columns.Count) return false;
            var box = new Win32Rect { Top = draw.SubItem, Left = 0 };
            if (SendMessageW(_listView, 0x1038, draw.Draw.ItemSpec, ref box) == 0) return false;
            // Subitem zero's BOUNDS covers the whole row; restrict it to the first column.
            if (draw.SubItem == 0) box.Right = box.Left + (int)SendMessageW(_listView, 0x101D, 0, 0);
            var selected = (int)draw.Draw.ItemSpec == _paintSelectedRow;
            FillRectangle(draw.Draw.DeviceContext, box, GenesisGuiNextDesign.RowBackground(selected, (int)draw.Draw.ItemSpec == _hoverRow));
            box.Left += Scale(10); box.Right -= Scale(10);
            var channelName = draw.SubItem == 0 && GenesisGuiNextRecordingListPresentation.UsesChannelColumns(columns);
            var numeric = columns[draw.SubItem].Alignment == GenesisChannelColumnAlignment.Right;
            var color = selected || channelName ? GenesisGuiNextDesign.Text : GenesisGuiNextDesign.SecondaryText;
            if (!selected && _tabs.SelectedTab == GenesisGuiNextTab.Logs && draw.SubItem == 1 &&
                GenesisChannelListRenderedChannels.FindRow(_listView, (int)draw.Draw.ItemSpec)?.Log is { Kind: not GenesisGuiNextLogKind.Information } log)
                color = GenesisGuiNextLogProjection.TextColor(log.Kind);
            DrawCardLabel(draw.Draw.DeviceContext, box, values[draw.SubItem],
                color, channelName ? _channelNameFont?.Handle ?? 0 : _visualFont?.Handle ?? 0, _listView,
                DrawTextSingleLine | DrawTextVerticalCenter | 0x8000u | (numeric ? 2u : 0u));
            result = 4; // CDRF_SKIPDEFAULT affects pixels only, retaining selection, sorting and activation.
            return true;
        }
    }
}

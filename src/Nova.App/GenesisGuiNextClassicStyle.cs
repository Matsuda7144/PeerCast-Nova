using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextAppShellBridge
{
    // PR #340 rendering. Only drawing is separated; input and data remain shared.
    private sealed partial class AppShellState
    {
        private bool DrawClassicNavigation(DrawItem drawItem, int itemIndex)
        {
            var selected = _utilityNavigationSelection.IsSelected(itemIndex, _tabs.SelectedTab);
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                selected ? SelectedColor : UnselectedColor);
            if (selected)
            {
                var accent = drawItem.ItemRectangle;
                accent.Right = Math.Min(accent.Right, accent.Left + AccentWidth);
                FillRectangle(drawItem.DeviceContext, accent, AccentColor);
            }

            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 16;
            labelRectangle.Right -= 4;
            _ = DrawTextW(
                drawItem.DeviceContext,
                GenesisGuiNextNavigationPresentation.Items[itemIndex],
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        private static bool DrawClassicRefreshButton(DrawItem drawItem)
        {
            var pressed = (drawItem.ItemState & 0x0001) != 0;
            var disabled = (drawItem.ItemState & DrawItemDisabled) != 0;
            FillRectangle(
                drawItem.DeviceContext,
                drawItem.ItemRectangle,
                pressed ? SelectedColor : UnselectedColor);
            _ = SetBkMode(drawItem.DeviceContext, TransparentBackgroundMode);
            _ = SetTextColor(
                drawItem.DeviceContext,
                disabled ? DisabledTextColor : TextColor);
            var labelRectangle = drawItem.ItemRectangle;
            labelRectangle.Left += 4;
            labelRectangle.Right -= 4;
            _ = DrawTextW(
                drawItem.DeviceContext,
                "更新",
                -1,
                ref labelRectangle,
                DrawTextSingleLine |
                    DrawTextVerticalCenter |
                    DrawTextNoPrefix);
            if ((drawItem.ItemState & DrawItemFocused) != 0)
            {
                _ = DrawFocusRect(drawItem.DeviceContext, ref drawItem.ItemRectangle);
            }

            return true;
        }

        private bool HandleClassicListViewCustomDraw(
            nint notificationPointer,
            out nint result)
        {
            result = 0;
            if (notificationPointer == 0 ||
                Volatile.Read(ref _detached) != 0)
            {
                return false;
            }

            var header = Marshal.PtrToStructure<NotifyHeader>(notificationPointer);
            if (header.WindowFrom != _listView || header.Code != NotifyCustomDraw)
            {
                return false;
            }

            var draw = Marshal.PtrToStructure<CustomDraw>(notificationPointer);
            if (draw.DrawStage == CustomDrawStagePrePaint)
            {
                result = (nint)CustomDrawNotifyItemDraw;
                return true;
            }

            if (draw.DrawStage == CustomDrawStageItemPrePaint)
            {
                if (_tabs.SelectedTab == GenesisGuiNextTab.Logs && draw.ItemSpec <= int.MaxValue &&
                    GenesisChannelListRenderedChannels.FindRow(_listView, (int)draw.ItemSpec)?.Log is { } log)
                {
                    var colorOffset = Marshal.OffsetOf<ListViewCustomDraw>(nameof(ListViewCustomDraw.TextColor)).ToInt32();
                    Marshal.WriteInt32(notificationPointer, colorOffset, GenesisGuiNextLogProjection.TextColor(log.Kind));
                    result = (nint)0x00000002; // CDRF_NEWFONT, apply row COLORREF.
                }
                if ((draw.ItemState & CustomDrawStateSelected) != 0)
                {
                    result = (nint)((long)result | CustomDrawNotifyPostPaint);
                }

                return true;
            }

            if (draw.DrawStage == CustomDrawStageItemPostPaint)
            {
                if ((draw.ItemState & CustomDrawStateSelected) != 0)
                {
                    var accent = draw.ItemRectangle;
                    accent.Right = Math.Min(accent.Right, accent.Left + AccentWidth);
                    FillRectangle(draw.DeviceContext, accent, AccentColor);
                }

                return true;
            }

            return false;
        }

    }
}

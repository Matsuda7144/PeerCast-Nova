using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static PeerCast.Nova.App.GenesisGuiNextAppShellBridge;
using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisIntegratedSettingsController
{
    private const nuint ConnectionBodySubclassId = 0x4E43424F;
    private const string ConnectionNotice = GenesisConnectionSettingsText.Notice;
    private static readonly ConcurrentDictionary<nint, GenesisIntegratedSettingsController> ControllersByOwner = new();
    private static readonly Win32SubclassProcedure ConnectionBodyProcedure = ProcessConnectionBody;

    private nint _connectionBody;
    private nint _connectionNotice;
    private nint _connectionRuntime;
    private nint _connectionSaved;
    private nint _connectionStatus;
    private int _connectionScroll;
    private int _connectionContentHeight;
    private bool _placingConnectionBody;

    private nint[] ConnectionBodyTexts => [_connectionNotice, _connectionRuntime, _connectionSaved, _connectionStatus];
    private nint[] ConnectionBodyControls => [.. ConnectionBodyTexts, .. _connectionLabels, .. _connectionInputs, _connectionWan, _natSettingsStatus];

    internal static nint CreateConnectionBodyWindow(nint parent, nint instance, nint owner)
    {
        var window = CreateWindowExW(
            0x00010000,
            "STATIC",
            "",
            WindowStyleChild | 0x02000000 | 0x00200000,
            0,
            0,
            1,
            1,
            parent,
            0,
            instance,
            0);
        _ = SetWindowSubclass(window, ConnectionBodyProcedure, ConnectionBodySubclassId, (nuint)owner);
        return window;
    }

    private static nint ProcessConnectionBody(nint window, uint message, nuint value, nint data, nuint id, nuint owner)
    {
        // STATIC は既定で HTTRANSPARENT を返すため、スクロール領域としてヒットテストとスクロールを追跡させる。
        if (message is 0x0084 or 0x00A1 or 0x00A3)
        {
            return DefWindowProcW(window, message, value, data);
        }

        if (message == WindowMessageNcDestroy)
        {
            _ = RemoveWindowSubclass(window, ConnectionBodyProcedure, id);
        }

        if (!ControllersByOwner.TryGetValue((nint)owner, out var controller))
        {
            return DefSubclassProc(window, message, value, data);
        }

        if (message == WindowMessageCommand)
        {
            controller._host.ProcessCommand(value, data);
            return 0;
        }

        if (message is 0x0115 or 0x020A)
        {
            controller.ScrollConnectionBody(message, value);
            return 0;
        }

        if (message is 0x0133 or 0x0134)
        {
            return controller._host.PrepareDarkControl(value, GetParent(window));
        }

        if (message is 0x0135 or 0x0138)
        {
            return controller._host.PrepareStaticControl(value, GetParent(window));
        }

        if (message == 0x0014)
        {
            return 1;
        }

        if (message == WindowMessagePaint)
        {
            controller.PaintConnectionBody();
            return 0;
        }

        return DefSubclassProc(window, message, value, data);
    }

    private void CreateConnectionBody(nint instance)
    {
        _connectionBody = CreateConnectionBodyWindow(_settingsPane, instance, _host.Owner);
        nint Text() => CreateWindowExW(0, "STATIC", "", WindowStyleChild | WindowStyleVisible, 0, 0, 1, 1, _connectionBody, 0, instance, 0);
        _connectionNotice = Text();
        _connectionRuntime = Text();
        _connectionSaved = Text();
        _connectionStatus = Text();
    }

    private void PlaceConnectionBody()
    {
        if (!ConnectionCategory || _placingConnectionBody || !GetClientRect(_connectionBody, out var area) ||
            !GetWindowRect(_connectionBody, out var bodyBounds))
        {
            return;
        }

        _placingConnectionBody = true;
        try
        {
            // スクロールバー非表示時も折り返し幅がぶれないよう右余白を確保する。
            var width = Math.Max(1, _host.LogicalWidth(bodyBounds.Right - bodyBounds.Left) - 24);
            var page = _host.LogicalWidth(area.Bottom);
            var dc = GetDC(_connectionBody);
            if (dc == 0)
            {
                return;
            }

            var placements = new List<(nint Control, GenesisGuiNextAppShellPlacement.WindowRectangle Box)>();
            try
            {
                using var font = GenesisWindowUiFont.SelectForDrawing(dc, _connectionNotice);
                int TextHeight(nint control, int w)
                {
                    var box = new Win32Rect { Right = _host.Scale(w) };
                    var height = DrawTextW(dc, _host.ReadControlText(control), -1, ref box, 0x0010 | 0x0400 | 0x8000);
                    return Math.Max(20, _host.LogicalWidth(height) + 4);
                }

                void Add(nint control, int x, int y, int w, int h) => placements.Add((control, new(x, y, x + w, y + h)));
                var top = 4;
                var height = TextHeight(_connectionNotice, width);
                Add(_connectionNotice, 2, top, width, height);
                top += height + 8;
                height = TextHeight(_connectionRuntime, width);
                Add(_connectionRuntime, 2, top, width, height);
                top += height + 4;
                height = TextHeight(_connectionSaved, width);
                Add(_connectionSaved, 2, top, width, height);
                top += height + 8;
                var inputWidth = Math.Min(300, Math.Max(120, width - 190));
                for (var i = 0; i < 6; i++)
                {
                    Add(_connectionLabels[i], 2, top + 3, 180, 22);
                    Add(_connectionInputs[i], 188, top, inputWidth, 26);
                    top += 34;
                }

                Add(_connectionWan, 188, top, Math.Max(140, width - 190), 26);
                top += 34;
                var natHeight = Math.Max(38, TextHeight(_natSettingsStatus, width));
                Add(_natSettingsStatus, 2, top, width, natHeight);
                top += natHeight + 8;
                height = Math.Max(34, TextHeight(_connectionStatus, width));
                Add(_connectionStatus, 2, top, width, height);
                _connectionContentHeight = top + height + 4;
            }
            finally
            {
                _ = ReleaseDC(_connectionBody, dc);
            }

            _connectionScroll = GenesisConnectionBodyLayout.Clamp(_connectionScroll, page, _connectionContentHeight);
            var scroll = new Win32ScrollInfo
            {
                Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(),
                Mask = 7,
                Maximum = _connectionContentHeight - 1,
                Page = (uint)Math.Max(1, page),
                Position = _connectionScroll,
            };
            _ = SetScrollInfo(_connectionBody, 1, ref scroll, true);
            foreach (var (control, box) in placements)
            {
                _ = MoveWindow(control, _host.Scale(box.Left), _host.Scale(box.Top - _connectionScroll), _host.Scale(box.Width), _host.Scale(box.Height), true);
            }

            _ = RedrawWindow(_connectionBody, 0, 0, 0x0001 | 0x0004 | 0x0080);
        }
        finally
        {
            _placingConnectionBody = false;
        }
    }

    internal void ScrollConnectionBody(uint message, nuint value)
    {
        if (!ConnectionCategory || !GetClientRect(_connectionBody, out var area))
        {
            return;
        }

        var page = _host.LogicalWidth(area.Bottom);
        var info = new Win32ScrollInfo { Size = (uint)Marshal.SizeOf<Win32ScrollInfo>(), Mask = 0x10 };
        _ = GetScrollInfo(_connectionBody, 1, ref info);
        _connectionScroll = message == 0x020A
            ? _connectionScroll - (short)(value >> 16) * 48 / 120
            : (int)(value & 0xffff) switch
            {
                0 => _connectionScroll - 24,
                1 => _connectionScroll + 24,
                2 => _connectionScroll - page,
                3 => _connectionScroll + page,
                4 or 5 => info.TrackPosition,
                6 => 0,
                7 => _connectionContentHeight,
                _ => _connectionScroll,
            };
        PlaceConnectionBody();
    }

    internal bool HandleControlMessage(nint window, uint message, nuint value)
    {
        if (!ConnectionCategory || !ConnectionBodyControls.Contains(window))
        {
            return false;
        }

        if (message == 0x020A)
        {
            ScrollConnectionBody(message, value);
            return true;
        }

        if (message == 0x0007 && GetWindowRect(window, out var box) && GetClientRect(_connectionBody, out var area))
        {
            var point = new Win32Point { X = box.Left, Y = box.Top };
            _ = ScreenToClient(_connectionBody, ref point);
            var top = _host.LogicalWidth(point.Y) + _connectionScroll;
            _connectionScroll = GenesisConnectionBodyLayout.Reveal(
                _connectionScroll,
                _host.LogicalWidth(area.Bottom),
                top,
                top + _host.LogicalWidth(box.Bottom - box.Top),
                _connectionContentHeight);
            PlaceConnectionBody();
        }

        return false;
    }

    internal void PaintConnectionBody()
    {
        var dc = BeginPaint(_connectionBody, out var paint);
        try
        {
            if (dc == 0 || !GetClientRect(_connectionBody, out var area))
            {
                return;
            }

            _ = FillRect(dc, ref area, _host.TopBrush);
            foreach (var input in _connectionInputs)
            {
                if (!GetWindowRect(input, out var box))
                {
                    continue;
                }

                var point = new Win32Point { X = box.Left, Y = box.Top };
                _ = ScreenToClient(_connectionBody, ref point);
                using var pen = new GenesisVisualPen(dc, _host.Scale(1), GetFocus() == input ? GenesisGuiNextDesign.Accent : GenesisGuiNextDesign.Border);
                _ = GdiRectangle(dc, point.X - 1, point.Y - 1, point.X + box.Right - box.Left + 1, point.Y + box.Bottom - box.Top + 1);
            }
        }
        finally
        {
            _ = EndPaint(_connectionBody, ref paint);
        }
    }
}

internal static class GenesisConnectionBodyLayout
{
    internal static int Clamp(int scroll, int page, int content) => Math.Clamp(scroll, 0, Math.Max(0, content - page));

    internal static int Reveal(int scroll, int page, int top, int bottom, int content) =>
        top < scroll ? Clamp(top, page, content) : bottom > scroll + page ? Clamp(bottom - page, page, content) : Clamp(scroll, page, content);
}

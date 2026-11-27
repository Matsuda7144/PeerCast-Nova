using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

/// <summary>
/// GDI 描画時に 1px/スケール対応のペンと NULL_BRUSH を一時選択し、破棄時に元のオブジェクトへ復元する RAII ラッパー。
/// </summary>
internal sealed class GenesisVisualPen : IDisposable
{
    private readonly nint _dc;
    private readonly nint _pen;
    private readonly nint _previousPen;
    private readonly nint _previousBrush;

    internal GenesisVisualPen(nint dc, int width, int color)
    {
        _dc = dc;
        _pen = CreatePen(0, Math.Max(1, width), color);
        if (_pen != 0)
        {
            _previousPen = SelectObject(dc, _pen);
        }

        _previousBrush = SelectObject(dc, GetStockObject(5)); // NULL_BRUSH
    }

    public void Dispose()
    {
        if (_previousBrush != 0)
        {
            _ = SelectObject(_dc, _previousBrush);
        }

        if (_previousPen != 0)
        {
            _ = SelectObject(_dc, _previousPen);
        }

        if (_pen != 0)
        {
            _ = DeleteObject(_pen);
        }
    }
}

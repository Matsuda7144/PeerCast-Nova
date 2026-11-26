using System.Text.Json;

namespace PeerCast.Nova.App;

internal sealed record GenesisMainWindowPlacement(
    int Left,
    int Top,
    int Width,
    int Height,
    bool Maximized)
{
    public GenesisMainWindowPlacement ClampToWorkArea(
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        int minWidth,
        int minHeight)
    {
        var safeMinWidth = Math.Max(1, minWidth);
        var safeMinHeight = Math.Max(1, minHeight);
        var availableWidth = Math.Max(safeMinWidth, workRight - workLeft);
        var availableHeight = Math.Max(safeMinHeight, workBottom - workTop);
        var clampedWidth = Math.Clamp(Width, safeMinWidth, availableWidth);
        var clampedHeight = Math.Clamp(Height, safeMinHeight, availableHeight);
        var maxLeft = Math.Max(workLeft, workRight - clampedWidth);
        var maxTop = Math.Max(workTop, workBottom - clampedHeight);
        var clampedLeft = Math.Clamp(Left, workLeft, maxLeft);
        var clampedTop = Math.Clamp(Top, workTop, maxTop);
        return new(clampedLeft, clampedTop, clampedWidth, clampedHeight, Maximized);
    }
}

internal sealed class GenesisMainWindowPlacementStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public GenesisMainWindowPlacementStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? ResolveDefaultPath() : path;
    }

    public static string ResolveDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PeerCast Nova",
            "main-window-placement.json");

    public GenesisMainWindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = File.ReadAllText(_path);
            var placement = JsonSerializer.Deserialize<GenesisMainWindowPlacement>(json, SerializerOptions);
            return placement is { Width: > 0, Height: > 0 } ? placement : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(GenesisMainWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.Width <= 0 || placement.Height <= 0)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(placement, SerializerOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // ウィンドウ配置の保存失敗でアプリの終了やリサイズを妨げない。
        }
    }
}

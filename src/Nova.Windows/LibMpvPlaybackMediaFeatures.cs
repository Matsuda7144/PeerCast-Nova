using System.Globalization;

namespace PeerCast.Nova.Windows;

internal sealed record LibMpvPlaybackStats(
    int VideoWidth,
    int VideoHeight,
    double? VideoFps,
    double? TotalBitrateBps,
    double? CacheDurationSeconds)
{
    public string FormatCompact()
    {
        var parts = new List<string>(4);
        if (VideoWidth > 0 && VideoHeight > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{VideoWidth}x{VideoHeight}"));
        }

        if (TotalBitrateBps is > 0)
        {
            var kbps = (int)Math.Round(TotalBitrateBps.Value / 1000.0);
            if (kbps > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{kbps} kbps"));
            }
        }

        if (VideoFps is > 0.5)
        {
            var fps = (int)Math.Round(VideoFps.Value);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"({fps})"));
        }

        return string.Join(' ', parts);
    }
}

internal sealed record LibMpvVideoAdjustment(
    int Brightness = 0,
    int Contrast = 0,
    int Saturation = 0,
    int Gamma = 0)
{
    public const int MinValue = -100;
    public const int MaxValue = 100;
    public const int Step = 10;

    public static LibMpvVideoAdjustment Default { get; } = new();

    public bool IsDefault => Brightness == 0 && Contrast == 0 && Saturation == 0 && Gamma == 0;

    public LibMpvVideoAdjustment Clamped() => new(
        Math.Clamp(Brightness, MinValue, MaxValue),
        Math.Clamp(Contrast, MinValue, MaxValue),
        Math.Clamp(Saturation, MinValue, MaxValue),
        Math.Clamp(Gamma, MinValue, MaxValue));
}

internal static class LibMpvScreenshotPaths
{
    public static string ResolveDefaultDirectory()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var root = string.IsNullOrWhiteSpace(pictures)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : pictures;
        return Path.Combine(root, "PeerCast Nova", "Screenshots");
    }

    public static string CreateFilePath(string directory, DateTimeOffset timestamp)
    {
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"Nova_{timestamp:yyyyMMdd_HHmmss_fff}.png");
        return Path.Combine(directory, fileName);
    }
}

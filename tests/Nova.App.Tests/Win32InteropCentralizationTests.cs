using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PeerCast.Nova.App.Tests;

public sealed partial class Win32InteropCentralizationTests
{
    private static readonly HashSet<string> AllowedInteropRelativePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Nova.App/Interop/Win32NativeMethods.cs",
        "src/Nova.App/Interop/Win32NativeMethods.ShellAndGdi.cs",
        "src/Nova.Windows/Win32WindowNativeMethods.cs",
        "src/Nova.Windows/Win32AccessibilityAnnotation.cs",
    };

    [Fact]
    public void CentralizedNativeStructsHaveExpectedAbiSizes()
    {
        // Win32 API 境界で用いる主要構造体のサイズが期待通りであることを検証する。
        Assert.Equal(8, Marshal.SizeOf<Win32NativeMethods.Win32Point>());
        Assert.Equal(16, Marshal.SizeOf<Win32NativeMethods.Win32Rect>());
        Assert.Equal(40, Marshal.SizeOf<Win32NativeMethods.Win32MonitorInfo>());
        Assert.Equal(nint.Size == 8 ? 72 : 64, Marshal.SizeOf<Win32NativeMethods.Win32PaintStruct>());
        Assert.Equal(nint.Size == 8 ? 24 : 16, Marshal.SizeOf<Win32NativeMethods.Win32TrackMouseEvent>());
        Assert.Equal(44, Marshal.SizeOf<Win32NativeMethods.Win32WindowPlacement>());
        Assert.Equal(8, Marshal.SizeOf<Win32NativeMethods.Win32InitCommonControlsEx>());
        Assert.Equal(nint.Size == 8 ? 16 : 12, Marshal.SizeOf<Win32NativeMethods.Win32HighContrast>());
        Assert.Equal(28, Marshal.SizeOf<Win32NativeMethods.Win32ScrollInfo>());
        Assert.Equal(nint.Size == 8 ? 24 : 12, Marshal.SizeOf<Win32NativeMethods.Win32NotifyHeader>());
    }

    [Fact]
    public void PInvokeAttributesAreRestrictedToDedicatedInteropFiles()
    {
        // P/Invoke 宣言が所定の Interop 集約ファイル以外へ再拡散していないことを検証する。
        var repositoryRoot = ResolveRepositoryRoot();
        var sourceDirectory = Path.Combine(repositoryRoot, "src");
        var offendingFiles = new List<string>();

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var normalizedFullPath = filePath.Replace('\\', '/');
            if (normalizedFullPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                normalizedFullPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(filePath);
            if (!PInvokeAttributeRegex().IsMatch(content))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/');
            if (!AllowedInteropRelativePaths.Contains(relativePath))
            {
                offendingFiles.Add(relativePath);
            }
        }

        Assert.True(
            offendingFiles.Count == 0,
            $"集約先の Interop ファイル以外に P/Invoke 宣言が残っています: {string.Join(", ", offendingFiles)}");
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PeerCast-Nova.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("PeerCast-Nova.sln を含むリポジトリルートが見つかりませんでした。");
    }

    [GeneratedRegex(@"\[(DllImport|LibraryImport)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PInvokeAttributeRegex();
}

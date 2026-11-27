using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace PeerCast.Nova.App.Tests;

public sealed class HighDpiAndSplitterTests
{
    [Fact]
    public void AppManifestDeclaresPerMonitorV2AndProjectReferencesIt()
    {
        var manifestPath = FindRepositoryFile("src", "Nova.App", "app.manifest");
        var manifest = XDocument.Load(manifestPath);
        var manifestText = File.ReadAllText(manifestPath);

        Assert.Contains("PerMonitorV2", manifestText, StringComparison.Ordinal);
        Assert.Contains("true/pm", manifestText, StringComparison.Ordinal);
        Assert.Contains("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a", manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(manifest.Root);

        XNamespace smi2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        XNamespace smi2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";

        var dpiAware = Assert.Single(manifest.Descendants(smi2005 + "dpiAware"));
        Assert.Equal("true/pm", dpiAware.Value);

        var dpiAwareness = Assert.Single(manifest.Descendants(smi2016 + "dpiAwareness"));
        Assert.Contains("PerMonitorV2", dpiAwareness.Value, StringComparison.Ordinal);

        var longPathAware = Assert.Single(manifest.Descendants(smi2016 + "longPathAware"));
        Assert.Equal("true", longPathAware.Value);

        var project = XDocument.Load(FindRepositoryFile("src", "Nova.App", "Nova.App.csproj"));
        var appManifestProperty = Assert.Single(project.Descendants("ApplicationManifest"));
        Assert.Equal("app.manifest", appManifestProperty.Value);
    }

    [Fact]
    public void AppManifestGeneratesValidWindowsActivationContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var manifestPath = FindRepositoryFile("src", "Nova.App", "app.manifest");
        var actCtx = new ActCtxW
        {
            Size = (uint)Marshal.SizeOf<ActCtxW>(),
            Source = manifestPath,
        };

        var handle = CreateActCtxW(ref actCtx);
        var lastError = Marshal.GetLastWin32Error();
        try
        {
            Assert.True(handle != 0 && handle != -1, $"CreateActCtxW failed with Win32 error {lastError}.");
        }
        finally
        {
            if (handle != 0 && handle != -1)
            {
                ReleaseActCtx(handle);
            }
        }
    }

    [Fact]
    public void AppShellPlacementSupportsCustomDetailsHeightWithSafeClamping()
    {
        var area = new GenesisGuiNextAppShellPlacement.WindowRectangle(184, 46, 1100, 620);

        Assert.True(GenesisGuiNextAppShellPlacement.TryResolveLayout(
            area, 14, out _, out _, out var defaultList, out var defaultDetails));
        Assert.Equal(GenesisGuiNextAppShellPlacement.DetailsHeight, defaultDetails.Height);
        Assert.Equal(defaultDetails.Top - GenesisGuiNextAppShellPlacement.DetailsGap, defaultList.Bottom);

        Assert.True(GenesisGuiNextAppShellPlacement.TryResolveLayout(
            area, 14, out _, out _, out var customList, out var customDetails, preferredDetailsHeight: 300));
        Assert.Equal(300, customDetails.Height);
        Assert.Equal(customDetails.Top - GenesisGuiNextAppShellPlacement.DetailsGap, customList.Bottom);

        Assert.True(GenesisGuiNextAppShellPlacement.TryResolveLayout(
            area, 14, out _, out _, out var minList, out var minDetails, preferredDetailsHeight: 20));
        Assert.Equal(GenesisGuiNextAppShellPlacement.MinimumDetailsHeight, minDetails.Height);
        Assert.True(minList.Height >= GenesisGuiNextAppShellPlacement.MinimumListHeight);

        Assert.True(GenesisGuiNextAppShellPlacement.TryResolveLayout(
            area, 14, out _, out _, out var maxList, out var maxDetails, preferredDetailsHeight: 900));
        Assert.True(maxList.Height >= GenesisGuiNextAppShellPlacement.MinimumListHeight);
        Assert.Equal(maxDetails.Top - GenesisGuiNextAppShellPlacement.DetailsGap, maxList.Bottom);
    }

    [Fact]
    public void MainWindowPlacementStorePersistsAndClampsDetailsHeight()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"NovaPlacement-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "main-window-placement.json");
            var store = new GenesisMainWindowPlacementStore(path);
            var original = new GenesisMainWindowPlacement(120, 80, 1280, 760, Maximized: false, DetailsHeight: 310);

            store.Save(original);
            var loaded = store.Load();

            Assert.Equal(original, loaded);
            var clamped = loaded!.ClampToWorkArea(0, 0, 1920, 1080, 720, 480);
            Assert.Equal(310, clamped.DetailsHeight);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void ResolveDetailsPaneBoundsTracksCustomDetailsHeightAndExpandsOnlyForPublishing(uint dpi)
    {
        var width = GenesisMainWindowLayout.Scale(1280, dpi);
        var height = GenesisMainWindowLayout.Scale(900, dpi);

        foreach (var preferredHeight in new[] { 140, 160, 224, 300, 420 })
        {
            Assert.True(GenesisMainWindowLayout.TryResolve(
                width, height, dpi, out _, out _, out var list, out var details, preferredHeight));
            Assert.Equal(preferredHeight, details.Height);

            var standardPane = GenesisMainWindowLayout.ResolveDetailsPaneBounds(false, list, details);
            Assert.Equal(details, standardPane);
            Assert.Equal(list.Bottom + GenesisGuiNextAppShellPlacement.DetailsGap, standardPane.Top);
            Assert.True(list.Bottom < standardPane.Top);

            var publishingPane = GenesisMainWindowLayout.ResolveDetailsPaneBounds(true, list, details);
            Assert.Equal(new GenesisGuiNextAppShellPlacement.WindowRectangle(list.Left, list.Top, list.Right, details.Bottom), publishingPane);
        }
    }

    private static string FindRepositoryFile(params string[] segments) =>
        RepositorySourceTestHelper.FindRepositoryFile(segments);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ActCtxW
    {
        public uint Size;
        public uint Flags;
        public string? Source;
        public ushort ProcessorArchitecture;
        public ushort LangId;
        public string? AssemblyDirectory;
        public string? ResourceName;
        public string? ApplicationName;
        public nint ModuleHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateActCtxW(ref ActCtxW actCtx);

    [DllImport("kernel32.dll")]
    private static extern void ReleaseActCtx(nint actCtx);
}

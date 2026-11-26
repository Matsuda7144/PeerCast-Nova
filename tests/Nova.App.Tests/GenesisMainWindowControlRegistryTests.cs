using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowControlRegistryTests
{
    private const uint WindowStylePopup = 0x80000000u;
    private const uint WindowStyleChild = 0x40000000u;

    [Fact]
    public void RegisterAndUnregister_ResolveAndClearSyntheticHandles()
    {
        const nint owner = 0x714401;
        var expected = new GenesisMainWindowControlHandles(
            owner,
            SourceLabel: 101,
            SourceEdit: 102,
            PlayButton: 103,
            StopButton: 104,
            ChannelList: 105,
            StatusText: 106);

        try
        {
            GenesisMainWindowControlRegistry.Register(expected);

            Assert.True(GenesisMainWindowControlRegistry.TryResolve(owner, out var actual));
            Assert.Equal(expected, actual);
            Assert.Equal(105, GenesisMainWindowControlRegistry.ResolveChannelList(owner));
            Assert.Equal(102, GenesisMainWindowControlRegistry.ResolveSourceEdit(owner));
            Assert.Equal(106, GenesisMainWindowControlRegistry.ResolveStatusText(owner));
        }
        finally
        {
            GenesisMainWindowControlRegistry.Unregister(owner);
        }

        Assert.False(GenesisMainWindowControlRegistry.TryResolve(owner, out _));
        Assert.Equal(0, GenesisMainWindowControlRegistry.ResolveChannelList(owner));
    }

    [Fact]
    public void TryResolve_ResolvesControlsByControlIdWithoutEnglishLabels()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        nint owner = 0;
        var children = new List<nint>();
        try
        {
            owner = CreateWindowExW(0, "STATIC", "テストウィンドウ", WindowStylePopup, 0, 0, 320, 240, 0, 0, 0, 0);
            Assert.NotEqual(0, owner);

            nint CreateChild(string className, string text, int id)
            {
                var child = CreateWindowExW(0, className, text, WindowStyleChild, 0, 0, 80, 24, owner, (nint)id, 0, 0);
                Assert.NotEqual(0, child);
                children.Add(child);
                return child;
            }

            var sourceLabel = CreateChild("STATIC", "ソース", GenesisMainWindowControlIds.SourceLabel);
            var sourceEdit = CreateChild("EDIT", string.Empty, GenesisMainWindowControlIds.SourceEdit);
            var playButton = CreateChild("BUTTON", "再生", GenesisMainWindowControlIds.PlayButton);
            var stopButton = CreateChild("BUTTON", "停止", GenesisMainWindowControlIds.StopButton);
            var channelList = CreateChild("SysListView32", string.Empty, GenesisMainWindowControlIds.ChannelList);
            var statusText = CreateChild("STATIC", "待機中", GenesisMainWindowControlIds.StatusText);

            Assert.True(GenesisMainWindowControlRegistry.TryResolve(owner, out var handles));
            Assert.Equal(sourceLabel, handles.SourceLabel);
            Assert.Equal(sourceEdit, handles.SourceEdit);
            Assert.Equal(playButton, handles.PlayButton);
            Assert.Equal(stopButton, handles.StopButton);
            Assert.Equal(channelList, handles.ChannelList);
            Assert.Equal(statusText, handles.StatusText);
            Assert.Equal(channelList, GenesisMainWindowControlRegistry.ResolveChannelList(owner));
            Assert.Equal(sourceEdit, GenesisMainWindowControlRegistry.ResolveSourceEdit(owner));
            Assert.Equal(statusText, GenesisMainWindowControlRegistry.ResolveStatusText(owner));
        }
        finally
        {
            foreach (var child in children)
            {
                _ = DestroyWindow(child);
            }

            if (owner != 0)
            {
                _ = DestroyWindow(owner);
            }
        }
    }

    [Fact]
    public void NovaAppSource_RestrictsFindWindowExWToRegistryAndDeclaration()
    {
        var appDirectory = Path.Combine(RepositorySourceTestHelper.FindRepositoryRoot(), "src", "Nova.App");
        var offendingFiles = Directory
            .EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (string.Equals(name, "Win32NativeMethods.cs", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "GenesisMainWindowControlRegistry.cs", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return File.ReadAllText(path).Contains("FindWindowExW(", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}

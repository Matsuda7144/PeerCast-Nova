namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextMainWindowKeyboardNavigationTests
{
    [Fact]
    public void RemovedTabKeyDownFilterRejectsUnrelatedMessages()
    {
        Assert.True(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: -1,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 0,
            message: 0x0100,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0101,
            virtualKey: 0x09));
        Assert.False(GenesisGuiNextMainWindowKeyboardNavigation.IsRemovedTabKeyDown(
            hookCode: 0,
            removalAction: 1,
            message: 0x0100,
            virtualKey: 0x0D));
    }

    [Theory]
    [InlineData(4, -1, false, 0)]
    [InlineData(4, -1, true, 3)]
    [InlineData(4, 0, false, 1)]
    [InlineData(4, 3, false, 0)]
    [InlineData(4, 3, true, 2)]
    [InlineData(4, 0, true, 3)]
    [InlineData(0, -1, false, -1)]
    public void ResolveNextIndexWrapsInBothDirections(
        int count,
        int currentIndex,
        bool reverse,
        int expected)
    {
        Assert.Equal(
            expected,
            GenesisGuiNextMainWindowKeyboardNavigation.ResolveNextIndex(
                count,
                currentIndex,
                reverse));
    }

    [Fact]
    public void GuiNextPresentationOpensMainWindowKeyboardNavigation()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextFavoriteFeedback.cs"));

        Assert.Contains(
            "GenesisGuiNextMainWindowKeyboardNavigation.Open(owner)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisGuiNextAuxiliaryPresentationLifetime",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeHookConsumesOnlySuccessfullyHandledTabMessages()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextMainWindowKeyboardNavigation.cs"));

        Assert.Contains("SetWindowsHookExW(", source, StringComparison.Ordinal);
        Assert.Contains("IsMessageForOwner(message.Window)", source, StringComparison.Ordinal);
        Assert.Contains("TryMoveFocus())", source, StringComparison.Ordinal);
        Assert.Contains(
            "message.Message = WindowMessageNull;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("CallNextHookEx(", source, StringComparison.Ordinal);
        Assert.Contains("UnhookWindowsHookEx(hook)", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }
}

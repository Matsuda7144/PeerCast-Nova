namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelNativeHeaderSortContractTests
{
    [Fact]
    public void HeaderItemClickNotificationsAreRecognizedForAnsiAndUnicode()
    {
        Assert.True(GenesisChannelNativeHeaderSort.IsColumnClick(unchecked((uint)-302)));
        Assert.True(GenesisChannelNativeHeaderSort.IsColumnClick(unchecked((uint)-322)));
        Assert.False(GenesisChannelNativeHeaderSort.IsColumnClick(unchecked((uint)-301)));
        Assert.False(GenesisChannelNativeHeaderSort.IsColumnClick(0));
    }

    [Fact]
    public void DirectListViewSubclassConsumesHeaderClickAndRoutesChannelsToSort()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisChannelDirectContextMenuBridge.cs"));
        var messageHandler = ExtractMethod(
            source,
            "private static nint ProcessListViewMessage(");
        var headerHandler = ExtractMethod(
            source,
            "public bool ProcessHeaderNotify(");

        Assert.Contains(
            "message == WindowMessageNotify &&",
            messageHandler,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.ProcessHeaderNotify(lParam)",
            messageHandler,
            StringComparison.Ordinal);
        Assert.Contains(
            "return 0;",
            messageHandler,
            StringComparison.Ordinal);

        Assert.Contains(
            "GenesisChannelNativeHeaderSort.IsColumnClick(header.Code)",
            headerHandler,
            StringComparison.Ordinal);
        Assert.Contains(
            "_guiNextTabs is null ||",
            headerHandler,
            StringComparison.Ordinal);
        Assert.Contains(
            "_guiNextTabs.SelectedTab == GenesisGuiNextTab.Channels",
            headerHandler,
            StringComparison.Ordinal);
        Assert.Contains(
            "_interaction.SortChannelColumn(notification.Item)",
            headerHandler,
            StringComparison.Ordinal);
        Assert.Contains(
            "return true;",
            headerHandler,
            StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method was not found: {signature}");

        var openingBrace = source.IndexOf('{', start);
        Assert.True(openingBrace >= 0, $"Method body was not found: {signature}");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException(
            $"Method body was incomplete: {signature}");
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

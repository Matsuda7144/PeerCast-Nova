namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNewNativeRefreshContractTests
{
    [Fact]
    public void NewChannelMutationPublishesVisibleNewProjectionChange()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection())
        {
            SelectedTab = GenesisGuiNextTab.New,
        };
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing",
            ChannelId: "existing");
        var added = new GenesisChannelItem(
            "Added",
            "pcp://example/added",
            ChannelId: "added");
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot(
                [existing, added],
                [added]));

        Assert.Contains(
            nameof(GenesisGuiNextTabsViewModel.VisibleNewChannels),
            changed);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Same(added, row.DisplayChannel);
        Assert.Same(added, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void VisibleNewProjectionChangesReachNativeRowRebuildBoundary()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisMainWindowInteractionBridgeFactory.cs"));
        var propertyHandler = ExtractMethod(
            source,
            "private void OnGuiNextTabsPropertyChanged(");
        var messageHandler = ExtractMethod(
            source,
            "private static nint ProcessWindowMessage(");
        var refreshControls = ExtractMethod(
            source,
            "public void RefreshControls(");

        Assert.Contains(
            "nameof(GenesisGuiNextTabsViewModel.VisibleNewChannels)",
            propertyHandler,
            StringComparison.Ordinal);
        Assert.Contains("PostMessageW", propertyHandler, StringComparison.Ordinal);
        Assert.Contains("WindowMessageRefresh", propertyHandler, StringComparison.Ordinal);

        Assert.Contains(
            "message == WindowMessageRefresh",
            messageHandler,
            StringComparison.Ordinal);
        Assert.Contains("state.RefreshControls();", messageHandler, StringComparison.Ordinal);

        Assert.Contains(
            "GenesisGuiNextNativeTabProjection.Create(",
            refreshControls,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenesisMainWindowNativeSurface.ReplaceRows(",
            refreshControls,
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

        throw new InvalidOperationException($"Method body was incomplete: {signature}");
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        RepositorySourceTestHelper.FindRepositoryFile(relativeParts);
}

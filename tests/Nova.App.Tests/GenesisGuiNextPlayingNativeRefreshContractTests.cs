namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPlayingNativeRefreshContractTests
{
    [Fact]
    public void PlaybackMutationPublishesTruthfulPlayingProjectionChange()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection())
        {
            SelectedTab = GenesisGuiNextTab.Playing,
        };
        var snapshot = new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://example/playing",
            null);
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        tabs.UpdatePlayback(snapshot);

        Assert.Contains(nameof(GenesisGuiNextTabsViewModel.Playing), changed);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(snapshot.Source, row.DisplayChannel.Name);
        Assert.Contains(snapshot.Source!, row.DisplayChannel.DetailsText);
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
    }

    [Fact]
    public void PlayingProjectionChangesReachNativeRowRebuildBoundary()
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
            "nameof(GenesisGuiNextTabsViewModel.Playing)",
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

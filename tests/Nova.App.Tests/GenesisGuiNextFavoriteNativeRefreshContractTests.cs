namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextFavoriteNativeRefreshContractTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void FavoriteMutationsPublishVisibleFavoriteProjectionChanges()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var channel = new GenesisChannelItem(
            "Favorite",
            $"pcp://example/channel/{ChannelId}",
            ChannelId: ChannelId);
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        Assert.Equal(
            GenesisFavoriteChannelMutationResult.Added,
            tabs.AddFavorite(channel));
        Assert.Contains(
            nameof(GenesisGuiNextTabsViewModel.VisibleFavoriteChannels),
            changed);

        changed.Clear();

        Assert.True(tabs.RemoveFavorite(channel));
        Assert.Contains(
            nameof(GenesisGuiNextTabsViewModel.VisibleFavoriteChannels),
            changed);
    }

    [Fact]
    public void NativeInteractionRefreshesVisibleFavoriteProjectionChanges()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisMainWindowInteractionBridgeFactory.cs"));
        var handler = ExtractMethod(
            source,
            "private void OnGuiNextTabsPropertyChanged(");

        Assert.Contains(
            "nameof(GenesisGuiNextTabsViewModel.VisibleFavoriteChannels)",
            handler,
            StringComparison.Ordinal);
        Assert.Contains("PostMessageW", handler, StringComparison.Ordinal);
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

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
}

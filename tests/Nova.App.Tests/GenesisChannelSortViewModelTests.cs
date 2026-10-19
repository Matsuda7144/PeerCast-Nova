namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelSortViewModelTests
{
    [Fact]
    public void RepeatedColumnSortTogglesAscendingThenDescending()
    {
        var viewModel = CreateViewModel(
            Channel("Bravo", listeners: 2),
            Channel("Alpha", listeners: 3),
            Channel("Charlie", listeners: 1));

        viewModel.SortChannels(GenesisChannelColumn.ChannelName);

        Assert.Equal(
            new(GenesisChannelColumn.ChannelName, GenesisChannelSortDirection.Ascending),
            viewModel.ChannelSort);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], viewModel.Channels.Select(x => x.Name));

        viewModel.SortChannels(GenesisChannelColumn.ChannelName);

        Assert.Equal(
            new(GenesisChannelColumn.ChannelName, GenesisChannelSortDirection.Descending),
            viewModel.ChannelSort);
        Assert.Equal(["Charlie", "Bravo", "Alpha"], viewModel.Channels.Select(x => x.Name));
    }

    [Fact]
    public void NewColumnStartsAscending()
    {
        var viewModel = CreateViewModel(
            Channel("A", listeners: 12),
            Channel("B", listeners: 4));
        viewModel.SortChannels(GenesisChannelColumn.ChannelName);
        viewModel.SortChannels(GenesisChannelColumn.ChannelName);

        viewModel.SortChannels(GenesisChannelColumn.ListenerRelay);

        Assert.Equal(
            new(GenesisChannelColumn.ListenerRelay, GenesisChannelSortDirection.Ascending),
            viewModel.ChannelSort);
        Assert.Equal(["B", "A"], viewModel.Channels.Select(x => x.Name));
    }

    [Fact]
    public void CatalogRefreshKeepsCurrentSortAndSelection()
    {
        var selected = Channel("Selected", listeners: 20);
        var viewModel = CreateViewModel(
            selected,
            Channel("Other", listeners: 5));
        viewModel.SelectedChannel = selected;
        viewModel.SortChannels(GenesisChannelColumn.ListenerRelay);
        var refreshedSelected = selected with { Listeners = 1 };

        viewModel.ReplaceChannels([
            Channel("Other", listeners: 10),
            refreshedSelected,
        ]);

        Assert.Equal(["Selected", "Other"], viewModel.Channels.Select(x => x.Name));
        Assert.Same(refreshedSelected, viewModel.SelectedChannel);
        Assert.Equal(refreshedSelected.Source, viewModel.SourceText);
    }

    private static GenesisMainViewModel CreateViewModel(
        params GenesisChannelItem[] channels)
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels(channels);
        return viewModel;
    }

    private static GenesisChannelItem Channel(string name, int listeners) =>
        new(
            name,
            $"pcp://example/{name}",
            $"[game - {name}]",
            listeners,
            1,
            1800,
            TimeSpan.FromMinutes(10),
            "FLV");
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelSortInteractionTests
{
    [Fact]
    public void ValidColumnIndexSortsMappedPresentationColumn()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([
            Channel("A", listeners: 12),
            Channel("B", listeners: 4),
        ]);
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var listenerRelayIndex = GenesisChannelListPresentation.Columns
            .Select((column, index) => (column, index))
            .Single(entry => entry.column.Column == GenesisChannelColumn.ListenerRelay)
            .index;

        var sorted = interaction.SortChannelColumn(listenerRelayIndex);

        Assert.True(sorted);
        Assert.Equal(
            new(GenesisChannelColumn.ListenerRelay, GenesisChannelSortDirection.Ascending),
            viewModel.ChannelSort);
        Assert.Equal(["B", "A"], viewModel.Channels.Select(channel => channel.Name));
    }

    [Fact]
    public void YellowPageColumnIndexSortsByDisplayedSource()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([
            Channel("SP", listeners: 1) with { YellowPage = "SP" },
            Channel("Other", listeners: 1) with { YellowPage = "Other" },
        ]);
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var yellowPageIndex = GenesisChannelListPresentation.Columns
            .Select((column, index) => (column, index))
            .Single(entry => entry.column.Column == GenesisChannelColumn.YellowPage)
            .index;

        var sorted = interaction.SortChannelColumn(yellowPageIndex);

        Assert.True(sorted);
        Assert.Equal(
            new(GenesisChannelColumn.YellowPage, GenesisChannelSortDirection.Ascending),
            viewModel.ChannelSort);
        Assert.Equal(["Other", "SP"], viewModel.Channels.Select(channel => channel.Name));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(99)]
    public void InvalidColumnIndexDoesNotChangeSort(int columnIndex)
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var sorted = interaction.SortChannelColumn(columnIndex);

        Assert.False(sorted);
        Assert.Null(viewModel.ChannelSort);
    }

    private static GenesisChannelItem Channel(string name, int listeners) =>
        new(
            name,
            $"pcp://example/{name}",
            null,
            listeners,
            1,
            1800,
            null,
            "FLV");
}

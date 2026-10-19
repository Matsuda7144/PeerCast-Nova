namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelYellowPagePresentationTests
{
    [Fact]
    public void ChannelListEndsWithSeparateTypeAndYellowPageColumns()
    {
        var columns = GenesisChannelListPresentation.Columns;

        Assert.True(columns.Count >= 2);
        Assert.Equal(GenesisChannelColumn.Type, columns[^2].Column);
        Assert.Equal("タイプ", columns[^2].Header);
        Assert.Equal(GenesisChannelColumn.YellowPage, columns[^1].Column);
        Assert.Equal("YP", columns[^1].Header);
    }

    [Fact]
    public void RowValuesExposeRawTypeAndSpYellowPageSeparately()
    {
        var channel = new GenesisChannelItem(
            "SP RAW",
            "pcp://example/channel",
            Type: "RAW")
        {
            YellowPage = "SP",
        };

        var values = GenesisChannelListPresentation.CreateRowValues(channel);

        Assert.Equal(GenesisChannelListPresentation.Columns.Count, values.Length);
        Assert.Equal("RAW", values[^2]);
        Assert.Equal("SP", values[^1]);
    }
}

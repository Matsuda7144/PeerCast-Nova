namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelRefreshSelectionTests
{
    [Fact]
    public void MetadataRefreshPreservesSelectionBySource()
    {
        var original = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - old]",
            10,
            2,
            1800,
            TimeSpan.FromMinutes(10),
            "FLV");
        var refreshed = original with
        {
            Description = "[game - refreshed]",
            Listeners = 15,
            Relays = 4,
            Uptime = TimeSpan.FromMinutes(12),
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;

        viewModel.ReplaceChannels([refreshed]);

        Assert.Same(refreshed, viewModel.SelectedChannel);
        Assert.Equal(refreshed.Source, viewModel.SourceText);
    }

    [Fact]
    public void TrackerRefreshPreservesSelectionByChannelId()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var original = new GenesisChannelItem(
            "Example",
            "pcp://tracker-a.example/channel/id",
            ChannelId: channelId);
        var refreshed = original with
        {
            Source = "pcp://tracker-b.example/channel/id",
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;

        viewModel.ReplaceChannels([refreshed]);

        Assert.Same(refreshed, viewModel.SelectedChannel);
        Assert.Equal(refreshed.Source, viewModel.SourceText);
    }

    [Fact]
    public void TrackerRefreshPreservesManualSourceWhileRestoringByChannelId()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var original = new GenesisChannelItem(
            "Example",
            "pcp://tracker-a.example/channel/id",
            ChannelId: channelId);
        var refreshed = original with
        {
            Source = "pcp://tracker-b.example/channel/id",
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;
        viewModel.SourceText = "pcp://manual/override";

        viewModel.ReplaceChannels([refreshed]);

        Assert.Same(refreshed, viewModel.SelectedChannel);
        Assert.Equal("pcp://manual/override", viewModel.SourceText);
    }

    [Fact]
    public void DifferentChannelIdDoesNotAliasByMatchingSource()
    {
        var original = new GenesisChannelItem(
            "Original",
            "pcp://tracker.example/channel/id",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var replacement = new GenesisChannelItem(
            "Replacement",
            original.Source,
            ChannelId: "ABCDEF0123456789ABCDEF0123456789");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;

        viewModel.ReplaceChannels([replacement]);

        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal(original.Source, viewModel.SourceText);
    }

    [Fact]
    public void MetadataRefreshPreservesManualSourceWhileRestoringSelection()
    {
        var original = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - old]");
        var refreshed = original with
        {
            Description = "[game - refreshed]",
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;
        viewModel.SourceText = "pcp://manual/override";

        viewModel.ReplaceChannels([refreshed]);

        Assert.Same(refreshed, viewModel.SelectedChannel);
        Assert.Equal("pcp://manual/override", viewModel.SourceText);
    }

    [Fact]
    public void StructurallyIdenticalRefreshUsesCurrentCatalogInstance()
    {
        var original = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]");
        var refreshed = original with { };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;

        viewModel.ReplaceChannels([refreshed]);

        Assert.NotSame(original, refreshed);
        Assert.Same(refreshed, viewModel.SelectedChannel);
    }

    [Fact]
    public void RemovedChannelClearsSelectionWithoutErasingManualSource()
    {
        var original = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([original]);
        viewModel.SelectedChannel = original;

        viewModel.ReplaceChannels([]);

        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal(original.Source, viewModel.SourceText);
    }
}

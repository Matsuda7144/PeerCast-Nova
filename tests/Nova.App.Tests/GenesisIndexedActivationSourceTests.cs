namespace PeerCast.Nova.App.Tests;

public sealed class GenesisIndexedActivationSourceTests
{
    [Fact]
    public async Task ActivationUsesChannelSourceAfterManualSourceEdit()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]");
        viewModel.ReplaceChannels([channel]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        Assert.True(interaction.SelectChannel(0));
        viewModel.SourceText = "pcp://example/manual";

        var activated = await interaction.ActivateChannelAsync(0);

        Assert.True(activated);
        Assert.Same(channel, viewModel.SelectedChannel);
        Assert.Equal(channel.Source, viewModel.SourceText);
        Assert.Equal(channel.Source, viewModel.CurrentSource);
    }

    [Fact]
    public async Task ActivationKeepsGestureSourceAcrossSelectionRefresh()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var clicked = new GenesisChannelItem(
            "Clicked",
            "pcp://example/clicked",
            "[game - clicked]");
        var replacement = new GenesisChannelItem(
            "Replacement",
            "pcp://example/replacement",
            "[game - replacement]");
        viewModel.ReplaceChannels([clicked]);
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var replaced = false;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (!replaced &&
                eventArgs.PropertyName == nameof(GenesisMainViewModel.SelectedChannel) &&
                viewModel.SelectedChannel is not null)
            {
                replaced = true;
                viewModel.ReplaceChannels([replacement]);
            }
        };

        var activated = await interaction.ActivateChannelAsync(0);

        Assert.True(activated);
        Assert.True(replaced);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal(clicked.Source, viewModel.SourceText);
        Assert.Equal(clicked.Source, viewModel.CurrentSource);
    }

    [Fact]
    public async Task RenderedActivationFollowsSourceAcrossCatalogReorder()
    {
        var rendered = new GenesisChannelItem(
            "Clicked",
            "pcp://example/clicked",
            "[game - old]");
        var replacement = new GenesisChannelItem(
            "Replacement",
            "pcp://example/replacement",
            "[game - replacement]");
        var refreshed = rendered with
        {
            Description = "[game - refreshed]",
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([replacement, refreshed]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateChannelAsync(rendered);

        Assert.True(activated);
        Assert.Same(refreshed, viewModel.SelectedChannel);
        Assert.Equal(rendered.Source, viewModel.SourceText);
        Assert.Equal(rendered.Source, viewModel.CurrentSource);
    }

    [Fact]
    public async Task RemovedRenderedActivationDoesNotFallThroughToNewIndex()
    {
        var rendered = new GenesisChannelItem(
            "Removed",
            "pcp://example/removed",
            "[game - removed]");
        var replacement = new GenesisChannelItem(
            "Replacement",
            "pcp://example/replacement",
            "[game - replacement]");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([replacement]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateChannelAsync(rendered);

        Assert.False(activated);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal(string.Empty, viewModel.SourceText);
        Assert.Null(viewModel.CurrentSource);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
    }

    [Fact]
    public void RenderedSelectionFollowsSourceAcrossCatalogReorder()
    {
        var rendered = new GenesisChannelItem(
            "Clicked",
            "pcp://example/clicked",
            "[game - old]");
        var replacement = new GenesisChannelItem(
            "Replacement",
            "pcp://example/replacement",
            "[game - replacement]");
        var refreshed = rendered with
        {
            Description = "[game - refreshed]",
        };
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([replacement, refreshed]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var selected = interaction.SelectChannel(rendered);

        Assert.True(selected);
        Assert.Same(refreshed, viewModel.SelectedChannel);
        Assert.Equal(rendered.Source, viewModel.SourceText);
    }

    [Fact]
    public void RemovedRenderedSelectionDoesNotFallThroughToNewIndex()
    {
        var rendered = new GenesisChannelItem(
            "Removed",
            "pcp://example/removed",
            "[game - removed]");
        var replacement = new GenesisChannelItem(
            "Replacement",
            "pcp://example/replacement",
            "[game - replacement]");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([replacement]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var selected = interaction.SelectChannel(rendered);

        Assert.False(selected);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal(string.Empty, viewModel.SourceText);
    }
}

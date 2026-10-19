namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowInteractionAvailabilityTests
{
    [Fact]
    public async Task UnavailableSelectedActivationPreservesManualSource()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/selected",
            "[game - example]");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([channel]);
        var interaction = new GenesisMainWindowInteraction(viewModel);
        Assert.True(interaction.SelectChannel(0));
        await interaction.ActivateSelectedChannelAsync();

        viewModel.SourceText = "pcp://example/manual";
        var activated = await interaction.ActivateSelectedChannelAsync();

        Assert.False(activated);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal(channel.Source, viewModel.CurrentSource);
        Assert.Equal("pcp://example/manual", viewModel.SourceText);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.CanPlay);
        Assert.True(viewModel.CanStop);
    }

    [Fact]
    public async Task UnavailableIndexedActivationPreservesCurrentSelectionAndSource()
    {
        var current = new GenesisChannelItem(
            "Current",
            "pcp://example/current",
            "[game - current]");
        var other = new GenesisChannelItem(
            "Other",
            "pcp://example/other",
            "[game - other]");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([current, other]);
        var interaction = new GenesisMainWindowInteraction(viewModel);
        Assert.True(interaction.SelectChannel(0));
        Assert.True(await interaction.ActivateSelectedChannelAsync());

        var activated = await interaction.ActivateChannelAsync(1);

        Assert.False(activated);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal(current.Source, viewModel.CurrentSource);
        Assert.Same(current, viewModel.SelectedChannel);
        Assert.Equal(current.Source, viewModel.SourceText);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.CanPlay);
        Assert.True(viewModel.CanStop);
    }
}

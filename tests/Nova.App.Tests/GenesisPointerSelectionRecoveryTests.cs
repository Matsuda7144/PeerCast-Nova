namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPointerSelectionRecoveryTests
{
    [Fact]
    public void StaleNativeRowClearsManagedSelectionWithoutErasingManualSource()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;
        viewModel.SourceText = "pcp://manual/override";
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var nativeSelectionCleared = false;

        var synchronized = GenesisChannelListNativeSelection.SelectRowWithoutEnsureVisible(
            (nint)7144,
            1,
            static (_, _) => true,
            (_, _) =>
            {
                nativeSelectionCleared = true;
                return true;
            },
            interaction.SelectChannel);

        Assert.False(synchronized);
        Assert.True(nativeSelectionCleared);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal("pcp://manual/override", viewModel.SourceText);
    }
}

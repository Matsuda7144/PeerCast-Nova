namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPlaybackCommandTests
{
    [Fact]
    public void PlayRequiresAPlayableSelectedRowAndCanPlayState()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = Channel("Live", "live");
        var liveRow = new GenesisGuiNextNativeTabRow(channel, channel);

        Assert.True(GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Channels,
            liveRow).IsPlayEnabled);
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Channels,
            selectedRow: null).IsPlayEnabled);
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Favorites,
            new GenesisGuiNextNativeTabRow(channel, InteractiveChannel: null)).IsPlayEnabled);
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Playing,
            liveRow).IsPlayEnabled);
    }

    [Fact]
    public async Task PlayIsDisabledAndStopEnabledWhileTheExistingSessionIsActive()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var channel = Channel("Live", "live");
        await interaction.PlaySourceAsync("pcp://example/current");

        var presentation = GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Channels,
            new GenesisGuiNextNativeTabRow(channel, channel));
        var playingTabPresentation = GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Playing,
            new GenesisGuiNextNativeTabRow(channel, InteractiveChannel: null));

        Assert.False(presentation.IsPlayEnabled);
        Assert.True(presentation.IsStopEnabled);
        Assert.False(playingTabPresentation.IsPlayEnabled);
        Assert.True(playingTabPresentation.IsStopEnabled);
    }

    [Fact]
    public async Task PlayUsesTheSelectedInteractiveChannelThroughExistingActivationBoundary()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = Channel("Selected", "selected");
        viewModel.ReplaceChannels([channel]);
        viewModel.SourceText = "pcp://manual/source-must-not-be-used";
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        var activated = await GenesisGuiNextPlaybackCommandDispatch
            .ActivateSelectedChannelAsync(
                viewModel,
                interaction,
                GenesisGuiNextTab.Channels,
                row);

        Assert.True(activated);
        Assert.Same(channel, viewModel.SelectedChannel);
        Assert.Equal(channel.Source, viewModel.CurrentSource);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
    }

    [Fact]
    public async Task PlayRejectsPlayingAndNonInteractiveRowsWithoutChangingSource()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = Channel("Display only", "display-only");
        var interaction = new GenesisMainWindowInteraction(viewModel);
        viewModel.SourceText = "pcp://manual/unchanged";

        var playingResult = await GenesisGuiNextPlaybackCommandDispatch
            .ActivateSelectedChannelAsync(
                viewModel,
                interaction,
                GenesisGuiNextTab.Playing,
                new GenesisGuiNextNativeTabRow(channel, InteractiveChannel: null));
        var offlineFavoriteResult = await GenesisGuiNextPlaybackCommandDispatch
            .ActivateSelectedChannelAsync(
                viewModel,
                interaction,
                GenesisGuiNextTab.Favorites,
                new GenesisGuiNextNativeTabRow(channel, InteractiveChannel: null));

        Assert.False(playingResult);
        Assert.False(offlineFavoriteResult);
        Assert.Equal("pcp://manual/unchanged", viewModel.SourceText);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
    }

    [Fact]
    public async Task StopUsesExistingInteractionAndDoesNotRequireASelectedRow()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);

        await GenesisGuiNextPlaybackCommandDispatch.StopAsync(viewModel, interaction);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);

        await interaction.PlaySourceAsync("pcp://example/channel");
        Assert.True(GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Channels,
            selectedRow: null).IsStopEnabled);

        await GenesisGuiNextPlaybackCommandDispatch.StopAsync(viewModel, interaction);

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    [Theory]
    [InlineData(null, "Enter a channel or source URL.")]
    [InlineData("   ", "Enter a channel or source URL.")]
    public void SourceDialogSharesExistingEmptySourceValidation(
        string? source,
        string expected)
    {
        Assert.Equal(expected, GenesisSourceUrlPlaybackDialog.GetValidationError(source));
    }

    [Fact]
    public void SourceDialogSharesExistingMaximumLengthAndTrimValidation()
    {
        Assert.Equal(
            "The channel or source URL is too long.",
            GenesisSourceUrlPlaybackDialog.GetValidationError(
                new string('a', GenesisMainViewModel.MaximumSourceCharacters + 1)));
        Assert.Null(GenesisSourceUrlPlaybackDialog.GetValidationError(" pcp://example/source "));
    }

    [Fact]
    public void ManualSourceUrlIsAnExplicitContextMenuCommand()
    {
        Assert.Equal(5, (int)GenesisChannelContextMenuCommand.ManualSourceUrl);
    }

    [Fact]
    public void SourceDialogDrawFunctionsUseTheirWindowsApiModule()
    {
        Assert.Equal("user32.dll", GetImportModule("DrawTextW"));
        Assert.Equal("user32.dll", GetImportModule("DrawFocusRect"));
        Assert.Equal("gdi32.dll", GetImportModule("GetStockObject"));
    }

    private static GenesisChannelItem Channel(string name, string token) =>
        new(name, $"pcp://example/{token}", $"[{token}]");

    private static string? GetImportModule(string methodName) =>
        typeof(GenesisSourceUrlPlaybackDialog)
            .GetMethod(
                methodName,
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)
            ?.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute), inherit: false)
            .Cast<System.Runtime.InteropServices.DllImportAttribute>()
            .SingleOrDefault()
            ?.Value;
}

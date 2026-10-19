namespace PeerCast.Nova.App.Tests;

public sealed class GenesisSourceInputLimitTests
{
    [Fact]
    public void NativeSourceLimitMatchesViewModelValidation()
    {
        nint configuredWindow = 0;
        uint configuredMessage = 0;
        nuint configuredMaximum = 0;
        nint configuredUnused = -1;

        var configured = GenesisMainWindowNativeSurface.LimitSourceTextInput(
            (nint)7144,
            (window, message, maximum, unused) =>
            {
                configuredWindow = window;
                configuredMessage = message;
                configuredMaximum = maximum;
                configuredUnused = unused;
                return 0;
            });

        Assert.True(configured);
        Assert.Equal((nint)7144, configuredWindow);
        Assert.Equal(0x00C5u, configuredMessage);
        Assert.Equal(
            (nuint)GenesisMainViewModel.MaximumSourceCharacters,
            configuredMaximum);
        Assert.Equal((nint)0, configuredUnused);
    }

    [Fact]
    public async Task SourceBeyondNativeInputLimitIsRejected()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = new string(
                'x',
                GenesisMainViewModel.MaximumSourceCharacters + 1),
        };

        await viewModel.PlayAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.Equal(
            "The channel or source URL is too long.",
            viewModel.ErrorMessage);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }
}

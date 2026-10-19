namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackModeButtonBridgeTests
{
    [Fact]
    public async Task BackgroundViewModelChangeQueuesRefreshWithoutTouchingButtons()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var enableCalls = 0;
        var postedContainers = new List<nint>();
        using var state = new GenesisPlaybackModeButtonBridge.ModeState(
            viewModel,
            (nint)7144,
            (nint)1,
            (nint)2,
            (nint)3,
            (_, _) =>
            {
                enableCalls++;
                return true;
            },
            container =>
            {
                postedContainers.Add(container);
                return true;
            });

        await Task.Run(() => viewModel.SourceText = "pcp://example");

        Assert.Equal(0, enableCalls);
        Assert.Single(postedContainers);
        Assert.Equal((nint)7144, postedContainers[0]);
    }

    [Fact]
    public async Task PlaybackModeButtonsStayDisabledWhenPlayingWithoutPlaybackSurface()
    {
        var viewModel = new GenesisMainViewModel(
            new FakePlaybackSessionController(),
            hasPlaybackWindowSurface: false)
        {
            SourceText = "pcp://example",
        };
        await viewModel.PlayAsync();

        var enabledStates = new List<bool>();
        using var state = new GenesisPlaybackModeButtonBridge.ModeState(
            viewModel,
            (nint)7144,
            (nint)1,
            (nint)2,
            (nint)3,
            (_, enabled) =>
            {
                enabledStates.Add(enabled);
                return true;
            },
            static _ => true);

        state.RefreshButtons();

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Equal([false, false, false], enabledStates);
        var interaction = new GenesisMainWindowInteraction(viewModel);
        Assert.False(interaction.RestorePlaybackWindow());
        Assert.False(interaction.ShowVideoOnly());
        Assert.False(interaction.ShowFullscreen());
    }
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisKeyboardInteractionTests
{
    [Theory]
    [InlineData(0x79, true, true)]
    [InlineData(0x79, false, false)]
    [InlineData(0x5D, false, true)]
    [InlineData(0x0D, true, false)]
    public void ContextMenuOpenGestureMatchesWindowsConventions(
        int virtualKey,
        bool shiftPressed,
        bool expected)
    {
        Assert.Equal(
            expected,
            GenesisChannelContextMenuKeyboard.IsOpenGesture(
                (ushort)virtualKey,
                shiftPressed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerContextMenuUsesRenderedTargetRegardlessOfSelectionSynchronization(
        bool selectionSynchronized)
    {
        var menuOpened = false;

        var opened = GenesisChannelContextMenuPointer.TryOpenAfterSelection(
            selectionSynchronized,
            () => menuOpened = true);

        Assert.True(opened);
        Assert.True(menuOpened);
    }

    [Theory]
    [InlineData(0x0D, true)]
    [InlineData(0x20, false)]
    [InlineData(0x74, false)]
    public void SourcePlayGestureUsesEnter(int virtualKey, bool expected)
    {
        Assert.Equal(
            expected,
            GenesisSourceKeyboard.IsPlayGesture((ushort)virtualKey));
    }

    [Theory]
    [InlineData(0x1B, true)]
    [InlineData(0x0D, false)]
    [InlineData(0x74, false)]
    public void SourceStopGestureUsesEscape(int virtualKey, bool expected)
    {
        Assert.Equal(
            expected,
            GenesisSourceKeyboard.IsStopGesture((ushort)virtualKey));
    }

    [Theory]
    [InlineData(0x74, true)]
    [InlineData(0x0D, false)]
    [InlineData(0x1B, false)]
    public void SourceRefreshGestureUsesF5(int virtualKey, bool expected)
    {
        Assert.Equal(
            expected,
            GenesisSourceKeyboard.IsRefreshGesture((ushort)virtualKey));
    }

    [Fact]
    public async Task ListKeyboardTargetUsesRenderedNativeSelection()
    {
        var rendered = new GenesisChannelItem(
            "Rendered",
            "pcp://example/rendered");
        var managed = new GenesisChannelItem(
            "Managed",
            "pcp://example/managed");
        IReadOnlyList<GenesisChannelItem> channels = [rendered, managed];
        var requestedRow = -1;

        var target = GenesisChannelKeyboardSelection.ResolveTarget(
            (nint)7144,
            managed,
            channels,
            (_, row) =>
            {
                requestedRow = row;
                return 0;
            },
            (_, row) => row == 0 ? rendered : null);

        Assert.NotNull(target);
        Assert.Equal(1, requestedRow);
        Assert.Equal(0, target.Value.Row);
        Assert.Same(rendered, target.Value.Channel);

        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels(channels);
        viewModel.SelectedChannel = managed;
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateChannelAsync(target.Value.Channel);

        Assert.True(activated);
        Assert.Equal(rendered.Source, viewModel.CurrentSource);
        Assert.Same(rendered, viewModel.SelectedChannel);
    }

    [Fact]
    public void ListKeyboardTargetFailsClosedWhenManagedSelectionIsStale()
    {
        var current = new GenesisChannelItem(
            "Current",
            "pcp://example/current");
        var stale = new GenesisChannelItem(
            "Stale",
            "pcp://example/stale");
        var rowResolutionAttempted = false;

        var target = GenesisChannelKeyboardSelection.ResolveTarget(
            (nint)7144,
            stale,
            [current],
            (_, _) =>
            {
                rowResolutionAttempted = true;
                return 0;
            },
            (_, _) => current);

        Assert.Null(target);
        Assert.False(rowResolutionAttempted);
    }

    [Fact]
    public async Task SelectedChannelActivationUsesSelectedSource()
    {
        var first = new GenesisChannelItem(
            "First",
            "pcp://example/first");
        var selected = new GenesisChannelItem(
            "Selected",
            "pcp://example/selected");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([first, selected]);
        viewModel.SelectedChannel = selected;
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateSelectedChannelAsync();

        Assert.True(activated);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal(selected.Source, viewModel.CurrentSource);
        Assert.Same(selected, viewModel.SelectedChannel);
    }

    [Fact]
    public async Task SelectedChannelActivationWithoutSelectionIsNoOp()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateSelectedChannelAsync();

        Assert.False(activated);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.Null(viewModel.CurrentSource);
        Assert.Null(viewModel.SelectedChannel);
    }
}

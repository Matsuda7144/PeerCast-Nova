namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelListRenderRecoveryTests
{
    [Fact]
    public void FailedReplacementDiscardsRenderedIdentityAndManagedSelection()
    {
        var channel = new GenesisChannelItem(
            "Rendered",
            "pcp://example/rendered");
        var managedSelectionCleared = false;

        var rendered = GenesisChannelListRenderRecovery.Resolve(
            rowsReplaced: false,
            [channel],
            () => managedSelectionCleared = true);

        Assert.Empty(rendered);
        Assert.True(managedSelectionCleared);
    }

    [Fact]
    public void SuccessfulReplacementAdoptsNewRenderedIdentity()
    {
        IReadOnlyList<GenesisChannelItem> channels =
        [
            new GenesisChannelItem(
                "Rendered",
                "pcp://example/rendered"),
        ];
        var managedSelectionCleared = false;

        var rendered = GenesisChannelListRenderRecovery.Resolve(
            rowsReplaced: true,
            channels,
            () => managedSelectionCleared = true);

        Assert.Same(channels, rendered);
        Assert.False(managedSelectionCleared);
    }
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNativeStatusClockTests
{
    [Fact]
    public async Task StatusClockIsScopedToGuiNextSession()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var legacySession = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);

        Assert.True(SystemGenesisMainWindowInteractionBridgeFactory.RequiresStatusClock(null, modern: true));
        Assert.False(SystemGenesisMainWindowInteractionBridgeFactory.RequiresStatusClock(null, modern: false));
        Assert.False(
            SystemGenesisMainWindowInteractionBridgeFactory.RequiresStatusClock(
                legacySession));

        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        await using var guiNextSession = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);

        Assert.True(
            SystemGenesisMainWindowInteractionBridgeFactory.RequiresStatusClock(
                guiNextSession));
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            SystemGenesisMainWindowInteractionBridgeFactory.GuiNextStatusRefreshInterval);
    }

    [Fact]
    public async Task StatusClockCanAdvanceAgeWithoutDiagnosticsMutation()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync());
        var diagnostics = session.Diagnostics;
        var lastSuccessfulAt = Assert.IsType<DateTimeOffset>(
            diagnostics.LastSuccessfulAt);

        Assert.Equal(
            "Ready · YP: updated 0s ago",
            SystemGenesisMainWindowInteractionBridgeFactory.CreateStatusText(
                viewModel,
                session,
                lastSuccessfulAt));
        Assert.Equal(
            "Ready · YP: updated 1m ago",
            SystemGenesisMainWindowInteractionBridgeFactory.CreateStatusText(
                viewModel,
                session,
                lastSuccessfulAt + TimeSpan.FromSeconds(61)));
        Assert.Same(diagnostics, session.Diagnostics);
    }
}

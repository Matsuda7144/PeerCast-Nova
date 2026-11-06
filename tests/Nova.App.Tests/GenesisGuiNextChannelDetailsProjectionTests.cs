namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextChannelDetailsProjectionTests
{
    [Fact]
    public void MetadataLinesUseOnlySelectedProjectionValuesAndKeepUnknownsHonest()
    {
        Assert.Empty(GenesisChannelDetailsPresentation.Empty.MetadataLines);
        var item = new GenesisChannelItem("name", "pcp://tracker.invalid/channel", Listeners: 12, ChannelId: "A0123456789ABCDEF0123456789ABCDEF");
        var details = GenesisGuiNextChannelDetailsProjection.FromChannel(item);
        Assert.Contains("12 / —", details.MetadataLines);
        Assert.Contains("ビットレート: —", details.MetadataLines);
        Assert.Contains("形式: —", details.MetadataLines);
        Assert.Equal(5, details.MetadataLines.Split("\r\n").Length);
        Assert.Contains(item.ChannelId!, details.IdentityText);
        Assert.Contains(item.Source, details.CopyText);
        Assert.DoesNotContain("—", details.CopyText); // display placeholders are not invented source facts
    }

    [Fact]
    public void SelectedChannelProjectsKnownMetadataAndIdentity()
    {
        var channel = new GenesisChannelItem(
            "Channel A",
            "pcp://tracker.example/channel/a",
            Description: "Known description",
            Listeners: 12,
            Relays: 4,
            BitrateKbps: 256,
            Uptime: TimeSpan.FromHours(3) + TimeSpan.FromMinutes(14),
            Type: "FLV",
            ChannelId: "A0123456789ABCDEF0123456789ABCDEF")
        {
            YellowPage = "SP",
        };
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows =
        [
            new(channel, channel),
        ];

        var details = GenesisGuiNextChannelDetailsProjection.FromSelection(rows, 0);

        Assert.False(details.IsEmpty);
        Assert.Equal(channel.Name, details.Name);
        Assert.Equal(channel.ChannelId, details.ChannelId);
        Assert.Equal(channel.Source, details.Source);
        Assert.Equal(channel.Description, details.Description);
        Assert.Equal("12", details.Listeners);
        Assert.Equal("4", details.Relays);
        Assert.Equal("256 kbps", details.Bitrate);
        Assert.Equal("FLV", details.Format);
        Assert.Equal("03:14", details.Uptime);
        Assert.Equal("SP", details.YellowPage);
    }

    [Fact]
    public void SelectingDifferentRowReplacesEveryDisplayedValue()
    {
        var first = Channel("Shared name", "pcp://tracker.example/a", "A-id");
        var second = Channel("Shared name", "pcp://tracker.example/b", "B-id") with
        {
            Description = "B description",
            Listeners = 9,
        };
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows =
        [
            new(first, first),
            new(second, second),
        ];

        var firstDetails = GenesisGuiNextChannelDetailsProjection.FromSelection(rows, 0);
        var secondDetails = GenesisGuiNextChannelDetailsProjection.FromSelection(rows, 1);

        Assert.Equal("A-id", firstDetails.ChannelId);
        Assert.Equal("B-id", secondDetails.ChannelId);
        Assert.Equal(second.Source, secondDetails.Source);
        Assert.Equal("B description", secondDetails.Description);
        Assert.Equal("9", secondDetails.Listeners);
        Assert.DoesNotContain("tracker.example/a", secondDetails.Source);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void MissingOrInvalidSelectionReturnsEmptyDetails(int selectedIndex)
    {
        var channel = Channel("Channel A", "pcp://tracker.example/a", "A-id");
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows =
        [
            new(channel, channel),
        ];

        var details = GenesisGuiNextChannelDetailsProjection.FromSelection(rows, selectedIndex);

        Assert.True(details.IsEmpty);
        Assert.Equal("チャンネルを選択してください", details.EmptyStateText);
        Assert.Empty(details.Name);
        Assert.Empty(details.ChannelId);
        Assert.Empty(details.Source);
    }

    [Fact]
    public void RefreshUsesStableIdentityAndClearsWhenSelectedIdentityDisappears()
    {
        var selected = Channel(
            "Before refresh",
            "pcp://tracker-a.example/channel",
            "0123456789ABCDEF0123456789ABCDEF");
        var refreshed = selected with
        {
            Name = "After refresh",
            Source = "pcp://tracker-b.example/channel",
            Description = "Refreshed description",
        };
        var refreshedRows = GenesisGuiNextNativeTabProjection.CreateLiveChannels([refreshed]);
        var restoredIndex = SystemGenesisMainWindowInteractionBridgeFactory.FindSelectedRow(
            refreshedRows,
            selected);

        var details = GenesisGuiNextChannelDetailsProjection.FromSelection(
            refreshedRows,
            restoredIndex);
        var vanishedIndex = SystemGenesisMainWindowInteractionBridgeFactory.FindSelectedRow(
            GenesisGuiNextNativeTabProjection.CreateLiveChannels([]),
            selected);
        var cleared = GenesisGuiNextChannelDetailsProjection.FromSelection(
            GenesisGuiNextNativeTabProjection.CreateLiveChannels([]),
            vanishedIndex);

        Assert.Equal(0, restoredIndex);
        Assert.Equal(refreshed.Name, details.Name);
        Assert.Equal(refreshed.Source, details.Source);
        Assert.Equal("Refreshed description", details.Description);
        Assert.Equal(-1, vanishedIndex);
        Assert.True(cleared.IsEmpty);
    }

    [Fact]
    public void DuplicateNamesKeepDetailsBoundToSelectedChannelIdentity()
    {
        var first = Channel("Same name", "pcp://tracker.example/first", "first-id");
        var second = Channel("Same name", "pcp://tracker.example/second", "second-id");
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows =
        [
            new(first, first),
            new(second, second),
        ];

        var details = GenesisGuiNextChannelDetailsProjection.FromSelection(rows, 1);

        Assert.Equal("Same name", details.Name);
        Assert.Equal("second-id", details.ChannelId);
        Assert.Equal(second.Source, details.Source);
    }

    [Fact]
    public void OfflineFavoriteDetailsUseOnlySavedProjectionMetadata()
    {
        var saved = new GenesisChannelItem(
            "Offline favorite",
            "pcp://saved.example/channel",
            Description: "Saved description",
            Type: "FLV",
            ChannelId: "saved-id")
        {
            YellowPage = "Saved YP",
        };
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]));
        tabs.ApplyChannelViews(GenesisChannelViewProjectionSnapshot.FromCatalog(
            GenesisChannelCatalogSnapshot.Empty));
        var rows = GenesisGuiNextNativeTabProjection.Create(
            tabs,
            GenesisGuiNextTab.Favorites);

        var details = GenesisGuiNextChannelDetailsProjection.FromSelection(rows, 0);

        Assert.False(rows[0].IsInteractive);
        Assert.False(details.IsEmpty);
        Assert.Equal(saved.Name, details.Name);
        Assert.Equal(saved.Description, details.Description);
        Assert.Equal(saved.ChannelId, details.ChannelId);
        Assert.Equal(saved.Source, details.Source);
        Assert.Equal("Saved YP", details.YellowPage);
        Assert.Empty(details.Listeners);
        Assert.Empty(details.Relays);
    }

    [Fact]
    public void ChannelsNewFavoritesAndPlayingUseTheSameDetailProjection()
    {
        var live = Channel("Shared", "pcp://tracker.example/shared", "shared-id");
        var favoriteStore = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(live),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favoriteStore);
        tabs.ApplyChannelViews(new GenesisChannelViewProjectionSnapshot([live], [live]));

        var channels = GenesisGuiNextNativeTabProjection.Create(tabs, GenesisGuiNextTab.Channels);
        var newRows = GenesisGuiNextNativeTabProjection.Create(tabs, GenesisGuiNextTab.New);
        var favoriteRows = GenesisGuiNextNativeTabProjection.Create(tabs, GenesisGuiNextTab.Favorites);
        tabs.UpdatePlayback(new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            live.Source,
            null)
        {
            ChannelName = live.Name,
            ChannelId = live.ChannelId,
        });
        var playingRows = GenesisGuiNextNativeTabProjection.Create(tabs, GenesisGuiNextTab.Playing);

        var channelDetails = GenesisGuiNextChannelDetailsProjection.FromSelection(channels, 0);
        var newDetails = GenesisGuiNextChannelDetailsProjection.FromSelection(newRows, 0);
        var favoriteDetails = GenesisGuiNextChannelDetailsProjection.FromSelection(favoriteRows, 0);
        var playingDetails = GenesisGuiNextChannelDetailsProjection.FromSelection(playingRows, 0);

        Assert.Equal(typeof(GenesisChannelDetailsPresentation), channelDetails.GetType());
        Assert.Equal(channelDetails, newDetails);
        Assert.Equal(channelDetails, favoriteDetails);
        Assert.Equal(live.Name, playingDetails.Name);
        Assert.Equal(live.ChannelId, playingDetails.ChannelId);
        Assert.Equal(live.Source, playingDetails.Source);
        Assert.Empty(playingDetails.YellowPage);
        Assert.Empty(playingDetails.Listeners);
    }

    [Fact]
    public void PlayingProjectionDoesNotRestoreACompletedSessionAfterReplacement()
    {
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdatePlayback(new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://manual/session-a",
            null)
        {
            ChannelName = "Session A",
            ChannelId = "session-a",
        });
        var oldSessionRows = GenesisGuiNextNativeTabProjection.Create(
            tabs,
            GenesisGuiNextTab.Playing);
        tabs.UpdatePlayback(new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://manual/session-b",
            null)
        {
            ChannelName = "Session B",
            ChannelId = "session-b",
        });
        var currentSessionRows = GenesisGuiNextNativeTabProjection.Create(
            tabs,
            GenesisGuiNextTab.Playing);

        var details = GenesisGuiNextChannelDetailsProjection.FromSelection(
            currentSessionRows,
            0);

        Assert.Equal("session-b", details.ChannelId);
        Assert.Equal("pcp://manual/session-b", details.Source);
        Assert.DoesNotContain(
            oldSessionRows[0].DisplayChannel.Source,
            details.Source,
            StringComparison.Ordinal);
        Assert.Empty(details.YellowPage);
    }

    private static GenesisChannelItem Channel(string name, string source, string id) =>
        new(name, source, Type: "FLV", ChannelId: id);
}

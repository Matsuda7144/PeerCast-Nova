using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextNativeTabProjectionTests
{
    [Fact]
    public void ChannelsUsesVisibleLiveRowsAndPreservesIdentity()
    {
        var tabs = CreateTabs();
        var alpha = Channel("Alpha", "alpha");
        var beta = Channel("Beta", "beta");
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([alpha, beta], [beta]));
        tabs.SearchText = "beta";

        var rows = GenesisGuiNextNativeTabProjection.Create(tabs);

        var row = Assert.Single(rows);
        Assert.Same(beta, row.DisplayChannel);
        Assert.Same(beta, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void NewTabUsesOnlyVisibleNewChannels()
    {
        var tabs = CreateTabs();
        var existing = Channel("Existing", "existing");
        var added = Channel("Added", "added");
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([existing, added], [added]));
        tabs.SelectedTab = GenesisGuiNextTab.New;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Same(added, row.DisplayChannel);
        Assert.Same(added, row.InteractiveChannel);
    }

    [Fact]
    public void OnlineFavoriteKeepsCurrentLiveChannelInteractive()
    {
        var saved = Channel("Saved", "shared");
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(saved),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        var current = new GenesisChannelItem(
            "Current",
            saved.Source,
            Description: "Current metadata",
            Type: "FLV",
            ChannelId: saved.ChannelId);
        tabs.ApplyChannelViews(
            new GenesisChannelViewProjectionSnapshot([current], []));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Same(current, row.DisplayChannel);
        Assert.Same(current, row.InteractiveChannel);
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void UnobservedFavoriteHidesOldDetailsButRetainsRemovalIdentity()
    {
        var savedChannel = new GenesisChannelItem(
            "Saved offline",
            "pcp://example/offline",
            Description: "Last known description",
            Type: "FLV",
            ChannelId: "offline-id")
        {
            YellowPage = "SP",
        };
        var favorites = new GenesisFavoriteChannelCollection([
            GenesisFavoriteChannel.FromChannel(savedChannel),
        ]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(
            GenesisChannelViewProjectionSnapshot.FromCatalog(
                GenesisChannelCatalogSnapshot.Empty));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Equal(savedChannel.Name, row.DisplayChannel.Name);
        Assert.Equal(savedChannel.Source, row.DisplayChannel.Source);
        Assert.Equal("現在の詳細は未取得", row.DisplayChannel.Description);
        Assert.Null(row.DisplayChannel.Type);
        Assert.Null(row.DisplayChannel.YellowPage);
        Assert.Equal(["Saved offline", "現在の詳細は未取得", "—", "—", "—", "—", "—"],
            ModernValues(row, GenesisGuiNextTab.Favorites));
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
        Assert.True(row.IsUnobservedFavorite);
        Assert.Equal(Assert.Single(favorites.Snapshot).StableKey, row.StableSelectionKey);
        Assert.True(tabs.IsFavorite(row.DisplayChannel));
        Assert.Equal(GenesisGuiNextFavoriteToggleResult.Removed,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.Empty(tabs.FavoriteChannels);
    }

    [Fact]
    public void PlayingTabShowsChannelIdentityAndStateWithoutInferringInteraction()
    {
        var tabs = CreateTabs();
        tabs.UpdatePlayback(new PlaybackSessionSnapshot(
            PlaybackSessionState.Playing,
            "pcp://example/current",
            null));
        tabs.SelectedTab = GenesisGuiNextTab.Playing;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Equal("pcp://example/current", row.DisplayChannel.Name);
        Assert.Equal("pcp://example/current", row.DisplayChannel.Source);
        Assert.Contains(nameof(PlaybackSessionState.Playing), row.DisplayChannel.DetailsText);
        Assert.Contains("pcp://example/current", row.DisplayChannel.DetailsText);
        Assert.Equal(["pcp://example/current", "Playing · pcp://example/current", "—", "—", "—", "—", "—"],
            ModernValues(row, GenesisGuiNextTab.Playing));
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
    }

    [Fact]
    public void RecordingTabProjectsManualSessionByChannelIdWithoutCatalogInteraction()
    {
        var tabs = CreateTabs();
        var channelId = new ChannelId(Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));
        var sessionId = new RecordingSessionId(Guid.ParseExact(
            "102132435465768798A9BABBDCDDEF00",
            "N"));
        var recording = new GenesisGuiNextActiveRecording(
            channelId,
            sessionId,
            RecordingSessionOrigin.Manual,
            "Channel A",
            "録画中",
            null,
            null);
        tabs.UpdateActiveRecordings([recording]);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Equal("Channel A", row.DisplayChannel.Name);
        Assert.Equal(channelId.Value.ToString("N").ToUpperInvariant(),
            row.DisplayChannel.ChannelId);
        Assert.Equal("手動録画", row.DisplayChannel.Type);
        Assert.Contains("状態: 録画中", row.DisplayChannel.Description);
        Assert.Equal(string.Empty, row.DisplayChannel.Source);
        Assert.Same(recording, row.ActiveRecording);
        Assert.Equal(["Channel A", "状態: 録画中", "—", "—", "—", "手動録画", "—"],
            ModernValues(row, GenesisGuiNextTab.Recording));
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
    }

    private static string[] ModernValues(GenesisGuiNextNativeTabRow row, GenesisGuiNextTab tab) =>
        GenesisGuiNextRecordingListPresentation.ModernValues(row, tab,
            GenesisGuiNextRecordingListPresentation.Columns(tab, GenesisGuiNextRecordingPageMode.ActiveRecordings));

    [Fact]
    public void RecordingTabProjectsAutomaticSessionAsReadOnlyWithRuleIdentity()
    {
        var tabs = CreateTabs();
        var channelId = new ChannelId(Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));
        var sessionId = new RecordingSessionId(Guid.ParseExact(
            "102132435465768798A9BABBDCDDEF00",
            "N"));
        var ruleId = Guid.Parse("0c68c7fa-1f31-4316-8a3b-5a3c0c5da22c");
        var recording = new GenesisGuiNextActiveRecording(
            channelId,
            sessionId,
            RecordingSessionOrigin.AutomaticRule,
            "Channel 531DC8DF",
            "録画中",
            ruleId,
            null);
        tabs.UpdateActiveRecordings([recording]);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Equal("自動録画", row.DisplayChannel.Type);
        Assert.Equal("Channel 531DC8DF", row.DisplayChannel.Name);
        Assert.Equal(ruleId, row.ActiveRecording?.AutomaticRuleId);
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
        Assert.Equal(
            "録画中のチャンネルを選択してください",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                tabs.ActiveRecordingsAvailable,
                tabs.ActiveRecordingCount));
    }

    [Fact]
    public void UnavailableSnapshotWithRetainedRowsShowsUnavailableHeading()
    {
        var tabs = CreateTabs();
        var recording = ActiveRecording();
        tabs.UpdateActiveRecordings([recording]);
        tabs.UpdateActiveRecordings(tabs.ActiveRecordings, isAvailable: false);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;

        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Same(recording, row.ActiveRecording);
        Assert.False(tabs.ActiveRecordingsAvailable);
        Assert.Equal("録画状態を確認できません", RecordingHeading(tabs));
        Assert.NotEqual("録画中のチャンネルを選択してください", RecordingHeading(tabs));
    }

    [Fact]
    public void UnavailableSnapshotWithNoRowsShowsUnavailableHeading()
    {
        Assert.Equal(
            "録画状態を確認できません",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                snapshotAvailable: false,
                activeRecordingCount: 0));
    }

    [Fact]
    public void AvailableSnapshotWithRowsShowsSelectionHeading()
    {
        Assert.Equal(
            "録画中のチャンネルを選択してください",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                snapshotAvailable: true,
                activeRecordingCount: 1));
    }

    [Fact]
    public void AvailableEmptySnapshotShowsExactEmptyHeading()
    {
        Assert.Equal(
            "現在録画中のチャンネルはありません",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                snapshotAvailable: true,
                activeRecordingCount: 0));
    }

    [Fact]
    public void SuccessfulSnapshotRestoresHeadingAfterUnavailableState()
    {
        var tabs = CreateTabs();
        var recording = ActiveRecording();
        tabs.UpdateActiveRecordings([recording]);
        tabs.UpdateActiveRecordings(tabs.ActiveRecordings, isAvailable: false);
        Assert.Equal("録画状態を確認できません", RecordingHeading(tabs));

        tabs.UpdateActiveRecordings(tabs.ActiveRecordings, isAvailable: true);

        Assert.Equal("録画中のチャンネルを選択してください", RecordingHeading(tabs));
        Assert.Same(recording, Assert.Single(tabs.ActiveRecordings));
    }

    [Fact]
    public void SuccessfulEmptySnapshotRestoresEmptyHeadingAfterUnavailableState()
    {
        var tabs = CreateTabs();
        tabs.UpdateActiveRecordings([ActiveRecording()]);
        tabs.UpdateActiveRecordings(tabs.ActiveRecordings, isAvailable: false);
        Assert.Equal("録画状態を確認できません", RecordingHeading(tabs));

        tabs.UpdateActiveRecordings(Array.Empty<GenesisGuiNextActiveRecording>());

        Assert.Equal("現在録画中のチャンネルはありません", RecordingHeading(tabs));
        Assert.Empty(tabs.ActiveRecordings);
    }

    private static string RecordingHeading(GenesisGuiNextTabsViewModel tabs) =>
        GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
            tabs.ActiveRecordingsAvailable,
            tabs.ActiveRecordingCount);

    private static GenesisGuiNextActiveRecording ActiveRecording() => new(
        new ChannelId(Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N")),
        new RecordingSessionId(Guid.ParseExact("102132435465768798A9BABBDCDDEF00", "N")),
        RecordingSessionOrigin.Manual,
        "Channel 531DC8DF",
        "録画中",
        null,
        null);

    private static GenesisGuiNextTabsViewModel CreateTabs() =>
        new(new GenesisFavoriteChannelCollection());

    private static GenesisChannelItem Channel(string name, string key) =>
        new(
            name,
            $"pcp://example/{key}",
            Description: $"{name} description",
            Type: "FLV",
            ChannelId: key);
}

namespace PeerCast.Nova.App.Tests;

public sealed class HttpYellowPageNoticeRowTests
{
    private const string ChannelId = "0123456789ABCDEF0123456789ABCDEF";
    private static readonly string ZeroChannelId = new('0', 32);

    [Fact]
    public void ParserSeparatesZeroIdRawNoticeFromPlayableChannels()
    {
        var playable = BuildRow(
            "Playable",
            ChannelId,
            "203.0.113.10:7144",
            type: "FLV");
        var notice = BuildRow(
            "SP&amp;Status",
            ZeroChannelId,
            string.Empty,
            genre: "info",
            detail: "Service &lt;notice&gt;",
            type: "RAW",
            comment: "Maintenance &amp; status");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(
            $"{playable}\n{notice}\n");

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("Playable", channel.Name);
        Assert.Equal(ChannelId, channel.ChannelId);
        Assert.StartsWith("pcp://203.0.113.10:7144/channel/", channel.Source);

        var parsedNotice = Assert.Single(snapshot.Notices);
        Assert.Equal("SP&Status", parsedNotice.Name);
        Assert.Equal(
            "[info - Service <notice>] Maintenance & status",
            parsedNotice.Description);
        Assert.Equal("RAW", parsedNotice.Type);
    }

    [Fact]
    public void ParserIgnoresZeroIdRowsThatAreNotRawNotices()
    {
        var row = BuildRow(
            "Not a notice",
            ZeroChannelId,
            string.Empty,
            type: "FLV");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(row);

        Assert.Empty(snapshot.Channels);
        Assert.Empty(snapshot.Notices);
    }

    [Fact]
    public void SanitizerBoundsNoticesWithoutRequiringPlaybackSource()
    {
        var snapshot = new GenesisChannelCatalogSnapshot(
            Array.Empty<GenesisChannelItem>(),
            [
                new GenesisYellowPageNotice(
                    new string(
                        'N',
                        GenesisChannelCatalogSanitizer.MaximumNameCharacters + 20),
                    new string(
                        'D',
                        GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters + 20),
                    " RAW "),
                new GenesisYellowPageNotice("Wrong type", null, "FLV"),
            ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        Assert.Empty(sanitized.Channels);
        var notice = Assert.Single(sanitized.Notices);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumNameCharacters,
            notice.Name.Length);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters,
            notice.Description?.Length);
        Assert.Equal("RAW", notice.Type);
    }

    [Fact]
    public async Task AggregationKeepsNoticeProviderAndSourceOrderWithoutChannelIdentity()
    {
        var first = Definition("first", "First YP");
        var second = Definition("second", "Second YP");
        var sameNotice = new GenesisYellowPageNotice("Status", "same text", "RAW");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            configuration,
            new FixedFactory(new Dictionary<string, IGenesisChannelCatalog>(
                StringComparer.Ordinal)
            {
                [first.Id] = new FixedCatalog(new(
                    Array.Empty<GenesisChannelItem>(),
                    [sameNotice, new GenesisYellowPageNotice("First extra", null, "RAW")])),
                [second.Id] = new FixedCatalog(new(
                    Array.Empty<GenesisChannelItem>(),
                    [sameNotice])),
            }));

        var detailed = await catalog.ReadDetailedAsync();

        Assert.Empty(detailed.Entries);
        Assert.Equal(3, detailed.Notices.Count);
        Assert.Equal(3, detailed.Catalog.Notices.Count);
        Assert.Equal("first", detailed.Notices[0].YellowPageId);
        Assert.Equal("First YP", detailed.Notices[0].YellowPageName);
        Assert.Equal(0, detailed.Notices[0].SourceIndex);
        Assert.Equal("First extra", detailed.Notices[1].Notice.Name);
        Assert.Equal(1, detailed.Notices[1].SourceIndex);
        Assert.Equal("second", detailed.Notices[2].YellowPageId);
        Assert.Equal("Second YP", detailed.Notices[2].YellowPageName);
        Assert.Equal(0, detailed.Notices[2].SourceIndex);
    }

    [Fact]
    public void PresenceIgnoresNoticesCompletely()
    {
        var definition = Definition("yp", "YP");
        var detailed = new GenesisAggregatedChannelCatalogSnapshot(
            Array.Empty<GenesisAggregatedChannelCatalogEntry>(),
            [
                new GenesisYellowPageCatalogReadStatus(
                    definition,
                    GenesisYellowPageCatalogReadState.Succeeded),
            ],
            [
                new GenesisAggregatedYellowPageNotice(
                    new GenesisYellowPageNotice("Status", null, "RAW"),
                    definition.Id,
                    definition.Name,
                    0),
            ]);
        var tracker = new GenesisChannelPresenceTracker();

        var presence = tracker.ApplySuccessfulRefresh(detailed);

        Assert.Empty(presence.Entries);
        Assert.Empty(presence.Active);
        Assert.Empty(presence.New);
        Assert.Empty(presence.Transitions);
    }

    [Fact]
    public void TabsSearchAndYellowPageFilterNoticesWithoutChangingChannelCounts()
    {
        var tabs = CreateTabs();
        var channel = new GenesisChannelItem(
            "Playable",
            "pcp://first.example/channel/0123456789ABCDEF0123456789ABCDEF",
            Type: "FLV",
            ChannelId: ChannelId)
        {
            YellowPage = "First YP",
        };
        var first = Definition("first", "First YP");
        var second = Definition("second", "Second YP");
        var presence = new GenesisChannelPresenceTracker().ApplySuccessfulRefresh(
            new GenesisAggregatedChannelCatalogSnapshot(
                [
                    new GenesisAggregatedChannelCatalogEntry(channel, [first.Id]),
                ],
                [
                    new GenesisYellowPageCatalogReadStatus(
                        first,
                        GenesisYellowPageCatalogReadState.Succeeded),
                    new GenesisYellowPageCatalogReadStatus(
                        second,
                        GenesisYellowPageCatalogReadState.Succeeded),
                ]));
        var views = new GenesisChannelViewProjectionSnapshot(
            [channel],
            [channel],
            [
                new GenesisYellowPageNoticeView(
                    new GenesisYellowPageNotice(
                        "SP Status",
                        "maintenance message",
                        "RAW"),
                    first.Id,
                    first.Name,
                    0),
                new GenesisYellowPageNoticeView(
                    new GenesisYellowPageNotice(
                        "Other Status",
                        "different message",
                        "RAW"),
                    second.Id,
                    second.Name,
                    0),
            ]);

        tabs.ApplyChannelViews(views, presence);

        Assert.Equal(1, tabs.ChannelCount);
        Assert.Equal(1, tabs.NewChannelCount);
        Assert.Equal(2, tabs.NoticeCount);
        Assert.Equal(2, tabs.VisibleNoticeCount);

        tabs.SelectedYellowPageId = first.Id;
        var filtered = Assert.Single(tabs.VisibleNotices);
        Assert.Equal("SP Status", filtered.Notice.Name);
        Assert.Single(tabs.VisibleChannels);

        tabs.SearchText = "maintenance first";
        Assert.Single(tabs.VisibleNotices);
        Assert.Empty(tabs.VisibleChannels);
        Assert.Equal(1, tabs.ChannelCount);
        Assert.Equal(1, tabs.NewChannelCount);
    }

    [Fact]
    public void NativeChannelsAppendDisplayOnlyNoticeAfterPlayableRows()
    {
        var tabs = CreateTabs();
        var channel = new GenesisChannelItem(
            "Playable",
            "pcp://example/channel/0123456789ABCDEF0123456789ABCDEF",
            Type: "FLV",
            ChannelId: ChannelId);
        tabs.ApplyChannelViews(new GenesisChannelViewProjectionSnapshot(
            [channel],
            Array.Empty<GenesisChannelItem>(),
            [
                new GenesisYellowPageNoticeView(
                    new GenesisYellowPageNotice(
                        "SP◆Status",
                        "service status",
                        "RAW"),
                    "sp",
                    "SP",
                    0),
            ]));

        var rows = GenesisGuiNextNativeTabProjection.Create(tabs);

        Assert.Equal(2, rows.Count);
        Assert.Same(channel, rows[0].InteractiveChannel);
        var notice = rows[1];
        Assert.Equal("SP◆Status", notice.DisplayChannel.Name);
        Assert.Equal("service status", notice.DisplayChannel.Description);
        Assert.Equal("RAW", notice.DisplayChannel.TypeText);
        Assert.Equal("SP", notice.DisplayChannel.YellowPageText);
        Assert.Equal(string.Empty, notice.DisplayChannel.Source);
        Assert.Null(notice.DisplayChannel.ChannelId);
        Assert.Null(notice.InteractiveChannel);
        Assert.False(notice.IsInteractive);
        Assert.NotNull(notice.StableSelectionKey);

        tabs.SelectedTab = GenesisGuiNextTab.New;
        Assert.Empty(GenesisGuiNextNativeTabProjection.Create(tabs));
        tabs.SelectedTab = GenesisGuiNextTab.Favorites;
        Assert.Empty(GenesisGuiNextNativeTabProjection.Create(tabs));
    }

    [Fact]
    public void WindowsChannelsRendererIncludesAndSearchesDisplayOnlyNotices()
    {
        var tabs = CreateTabs();
        var first = new GenesisChannelItem(
            "First",
            "pcp://example/channel/0123456789ABCDEF0123456789ABCDEF",
            ChannelId: ChannelId);
        var secondId = "FEDCBA9876543210FEDCBA9876543210";
        var second = new GenesisChannelItem(
            "Second",
            $"pcp://example/channel/{secondId}",
            ChannelId: secondId);
        tabs.ApplyChannelViews(new GenesisChannelViewProjectionSnapshot(
            [first, second],
            Array.Empty<GenesisChannelItem>(),
            [
                new GenesisYellowPageNoticeView(
                    new GenesisYellowPageNotice(
                        "SP◆Status",
                        "service status",
                        "RAW"),
                    "sp",
                    "SP",
                    0),
            ]));

        var rows = SystemGenesisMainWindowInteractionBridgeFactory
            .CreateChannelsRowsForNativeList([second, first], tabs);

        Assert.Equal(3, rows.Count);
        Assert.Same(second, rows[0].InteractiveChannel);
        Assert.Same(first, rows[1].InteractiveChannel);
        Assert.Equal("SP◆Status", rows[2].DisplayChannel.Name);
        Assert.Null(rows[2].InteractiveChannel);
        Assert.Equal("SP", rows[2].DisplayChannel.YellowPageText);

        tabs.SearchText = "Status";
        var filtered = SystemGenesisMainWindowInteractionBridgeFactory
            .CreateChannelsRowsForNativeList([second, first], tabs);
        var notice = Assert.Single(filtered);
        Assert.Equal("SP◆Status", notice.DisplayChannel.Name);
        Assert.Null(notice.InteractiveChannel);
    }

    [Fact]
    public void RenderedNoticeCannotBecomeInteractiveSelection()
    {
        const nint listView = 4242;
        GenesisChannelListRenderedChannels.Remove(listView);
        try
        {
            var channel = new GenesisChannelItem(
                "Playable",
                "pcp://example/channel/0123456789ABCDEF0123456789ABCDEF",
                ChannelId: ChannelId);
            var rows = Array.AsReadOnly(new[]
            {
                new GenesisGuiNextNativeTabRow(channel, channel),
                new GenesisGuiNextNativeTabRow(
                    new GenesisChannelItem("Status", string.Empty, Type: "RAW"),
                    InteractiveChannel: null,
                    StableSelectionKey: "notice:sp:0:Status"),
            });

            GenesisChannelListRenderedChannels.SetRows(
                listView,
                rows,
                static (_, _) => true);

            Assert.Same(channel, GenesisChannelListRenderedChannels.Find(listView, 0));
            Assert.Null(GenesisChannelListRenderedChannels.Find(listView, 1));

            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 1);
            Assert.Equal(
                1,
                GenesisChannelListRenderedChannels.FindAnchoredRow(listView, rows));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }

    [Fact]
    public async Task CoordinatorPublishesNoticesToGuiWithoutPresenceTransitions()
    {
        var definition = Definition("sp", "SP");
        var detailed = new GenesisAggregatedChannelCatalogSnapshot(
            Array.Empty<GenesisAggregatedChannelCatalogEntry>(),
            [
                new GenesisYellowPageCatalogReadStatus(
                    definition,
                    GenesisYellowPageCatalogReadState.Succeeded),
            ],
            [
                new GenesisAggregatedYellowPageNotice(
                    new GenesisYellowPageNotice("SP◆Status", "ready", "RAW"),
                    definition.Id,
                    definition.Name,
                    0),
            ]);
        var tabs = CreateTabs();
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var main = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(
            new FixedAggregatedCatalog(detailed),
            main,
            publisher);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Empty(main.Channels);
        Assert.Empty(tabs.Channels);
        var notice = Assert.Single(tabs.Notices);
        Assert.Equal("SP◆Status", notice.Notice.Name);
        Assert.Empty(tabs.NewChannels);
        Assert.Empty(coordinator.PresenceSnapshot!.Transitions);
        Assert.Empty(coordinator.PresenceSnapshot.New);
    }

    private static GenesisGuiNextTabsViewModel CreateTabs() =>
        new(new GenesisFavoriteChannelCollection());

    private static GenesisYellowPageDefinition Definition(string id, string name) =>
        new(
            id,
            name,
            new Uri($"https://{id}.example/index.txt"));

    private static string BuildRow(
        string name,
        string channelId,
        string tracker,
        string genre = "",
        string detail = "",
        string type = "",
        string comment = "")
    {
        var fields = new[]
        {
            name,
            channelId,
            tracker,
            "http://example.invalid/contact",
            genre,
            detail,
            "-9",
            "-9",
            "0",
            type,
            "",
            "",
            "",
            "",
            "",
            "0:00",
            "",
            comment,
        };
        return string.Join("<>", fields);
    }

    private sealed class FixedCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public FixedCatalog(GenesisChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }
    }

    private sealed class FixedFactory : IGenesisYellowPageChannelCatalogFactory
    {
        private readonly IReadOnlyDictionary<string, IGenesisChannelCatalog> _catalogs;

        public FixedFactory(IReadOnlyDictionary<string, IGenesisChannelCatalog> catalogs)
        {
            _catalogs = catalogs;
        }

        public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition) =>
            _catalogs[definition.Id];
    }

    private sealed class FixedAggregatedCatalog : IGenesisAggregatedChannelCatalog
    {
        private readonly GenesisAggregatedChannelCatalogSnapshot _snapshot;

        public FixedAggregatedCatalog(GenesisAggregatedChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot.Catalog);
        }

        public ValueTask<GenesisAggregatedChannelCatalogSnapshot> ReadDetailedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }
    }
}

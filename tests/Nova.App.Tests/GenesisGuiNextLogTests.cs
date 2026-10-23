namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly string[] LogHeaders = ["日時", "種類", "内容"];

    [Fact]
    public void NavigationKeepsBaselineOrderAndPreservesLogsAlongsidePublishing()
    {
        Assert.Equal("ログ", GenesisGuiNextNavigationPresentation.Items[8]);
        Assert.True(GenesisGuiNextNavigationPresentation.IsImplemented(8));
        Assert.True(GenesisGuiNextNavigationPresentation.TryResolveExistingView(8, out var tab));
        Assert.Equal(GenesisGuiNextTab.Logs, tab);
        Assert.True(GenesisGuiNextNavigationPresentation.IsImplemented(6));
        Assert.False(GenesisGuiNextNavigationPresentation.TryResolveExistingView(7, out _));
        Assert.False(GenesisGuiNextNavigationPresentation.TryResolvePrimaryCommand(8, out _));
    }

    [Fact]
    public void NoFactsProduceAnHonestEmptyView()
    {
        Assert.Empty(GenesisGuiNextLogProjection.Create(GenesisChannelCatalogRefreshDiagnostics.Empty));
        Assert.Equal("表示できるログはありません。", GenesisGuiNextLogProjection.EmptyText);
    }

    [Theory]
    [InlineData(0, 0, "情報")]
    [InlineData(1, 1, "警告")]
    [InlineData(2, 2, "エラー")]
    public void TypedOutcomesProvideBoundedSafeTextAndDistinctSeverity(
        int resultValue, int kindValue, string label)
    {
        var result = (GenesisChannelCatalogRefreshResult)resultValue;
        var kind = (GenesisGuiNextLogKind)kindValue;
        var row = Assert.Single(GenesisGuiNextLogProjection.Create(new(Now, null, result)));
        Assert.Equal(Now, row.Timestamp);
        Assert.Equal(kind, row.Kind);
        Assert.Equal(label, row.KindLabel);
        Assert.InRange(row.Content.Length, 1, 100);
        Assert.DoesNotContain("http", row.Content);
        Assert.DoesNotContain("pcp", row.Content);
        Assert.Equal(3, GenesisGuiNextLogProjection.Values(row).Length);
    }

    [Fact]
    public void ErrorWarningInformationHaveDifferentColorAndReadableLabels()
    {
        var colors = Enum.GetValues<GenesisGuiNextLogKind>().Select(GenesisGuiNextLogProjection.TextColor).ToArray();
        Assert.Equal(3, colors.Distinct().Count());
    }

    [Fact]
    public void SuccessUsesCompletionTimestampWithoutDuplicatingTheSameFact()
    {
        var row = Assert.Single(GenesisGuiNextLogProjection.Create(new(Now, Now.AddSeconds(2), GenesisChannelCatalogRefreshResult.Completed)));
        Assert.Equal(Now.AddSeconds(2), row.Timestamp);
    }

    [Fact]
    public void DisplayIsCappedAtTwoExistingFactsAndOrderedByTheirOriginalTimes()
    {
        var rows = GenesisGuiNextLogProjection.Create(new(Now, Now.AddMinutes(-1), GenesisChannelCatalogRefreshResult.Faulted));
        Assert.Equal(GenesisGuiNextLogProjection.MaximumRows, rows.Count);
        Assert.Equal(Now, rows[0].Timestamp);
        Assert.Equal(GenesisGuiNextLogKind.Error, rows[0].Kind);
        Assert.Equal(Now.AddMinutes(-1), rows[1].Timestamp);
        Assert.Equal(GenesisGuiNextLogKind.Information, rows[1].Kind);
    }

    [Fact]
    public void InFlightRefreshDoesNotAttachOldFailureToTheNewAttemptTime()
    {
        var rows = GenesisGuiNextLogProjection.Create(new(Now, Now.AddMinutes(-1), GenesisChannelCatalogRefreshResult.Faulted), isRefreshing: true);
        var row = Assert.Single(rows);
        Assert.Equal(Now.AddMinutes(-1), row.Timestamp);
        Assert.Equal(GenesisGuiNextLogKind.Information, row.Kind);
        Assert.Empty(GenesisGuiNextLogProjection.Create(new(Now, null, GenesisChannelCatalogRefreshResult.Faulted), isRefreshing: true));
    }

    [Fact]
    public async Task RepeatedSnapshotsDoNotCollectHistoryAndDisposalRejectsLateUpdates()
    {
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var diagnostic = new GenesisChannelCatalogRefreshDiagnostics(Now, null, GenesisChannelCatalogRefreshResult.Faulted);
        tabs.UpdateLogDiagnostics(diagnostic);
        var rows = tabs.LogRows;
        tabs.UpdateLogDiagnostics(diagnostic);
        Assert.Same(rows, tabs.LogRows);
        tabs.UpdateLogDiagnostics(new(Now.AddMinutes(1), Now.AddMinutes(1), GenesisChannelCatalogRefreshResult.Completed));
        Assert.Single(tabs.LogRows);
        await tabs.DisposeAsync();
        var final = tabs.LogRows;
        tabs.UpdateLogDiagnostics(diagnostic);
        Assert.Same(final, tabs.LogRows);
    }

    [Fact]
    public async Task LogRowsAreInertAndUseTheirOwnSchemaAcrossNavigation()
    {
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdateLogDiagnostics(new(Now, Now, GenesisChannelCatalogRefreshResult.Completed));
        tabs.SelectedTab = GenesisGuiNextTab.Logs;
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.NotNull(row.Log);
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
        Assert.False(GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(tabs, row).IsEnabled);
        Assert.Empty(row.DisplayChannel.Source);
        Assert.Equal(LogHeaders, GenesisGuiNextRecordingListPresentation.Columns(tabs.SelectedTab, tabs.RecordingPageMode).Select(c => c.Header));
        Assert.Equal(3, GenesisGuiNextRecordingListPresentation.Values(row).Length);
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        Assert.Empty(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(7, GenesisGuiNextRecordingListPresentation.Columns(tabs.SelectedTab, tabs.RecordingPageMode).Count);
        Assert.Single(tabs.LogRows);
    }

    [Fact]
    public void LogRowDoesNotOpenChannelContextMenuOrSynchronizeAChannelSelection()
    {
        var row = new GenesisGuiNextNativeTabRow(new(string.Empty, string.Empty), null,
            Log: new(Now, GenesisGuiNextLogKind.Error, "YP一覧の更新に失敗しました。"));
        Assert.False(GenesisChannelDirectContextMenuDispatch.TryOpen(row,
            _ => throw new InvalidOperationException("Must not synchronize channel"),
            _ => throw new InvalidOperationException("Must not open channel menu")));
    }

    [Fact]
    public async Task LogNavigationPreservesChannelAndNoticeRowsAndStableKeys()
    {
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var channel = new GenesisChannelItem("Playable", "pcp://example/channel",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var notice = new GenesisYellowPageNoticeView(
            new GenesisYellowPageNotice("SP Status", "service status", "RAW"), "sp", "SP", 0);
        tabs.ApplyChannelViews(new GenesisChannelViewProjectionSnapshot([channel], [], [notice]));
        var channelRows = GenesisGuiNextNativeTabProjection.Create(tabs);
        Assert.Equal(2, channelRows.Count);
        var noticeKey = channelRows[1].StableSelectionKey;
        Assert.NotNull(noticeKey);

        tabs.UpdateLogDiagnostics(new(Now, Now, GenesisChannelCatalogRefreshResult.Completed));
        tabs.SelectedTab = GenesisGuiNextTab.Logs;
        var log = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.NotNull(log.Log);
        Assert.False(log.IsInteractive);
        Assert.Null(log.StableSelectionKey);
        Assert.Equal(1, tabs.ChannelCount);
        Assert.Equal(1, tabs.NoticeCount);

        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        var restored = GenesisGuiNextNativeTabProjection.Create(tabs);
        Assert.Equal(2, restored.Count);
        Assert.Same(channel, restored[0].InteractiveChannel);
        Assert.Equal(noticeKey, restored[1].StableSelectionKey);
        Assert.Equal("RAW", restored[1].DisplayChannel.TypeText);
        Assert.Null(restored[1].Log);
        Assert.False(restored[1].IsInteractive);
    }

    [Fact]
    public void LogSelectionSurvivesRebuildWithoutAliasingNoticeSelection()
    {
        const nint listView = 269302;
        var notice = new GenesisGuiNextNativeTabRow(new("Status", string.Empty), null,
            StableSelectionKey: "notice:sp:0:Status");
        var firstLog = new GenesisGuiNextNativeTabRow(new(string.Empty, string.Empty), null,
            Log: new(Now, GenesisGuiNextLogKind.Error, "YP一覧の更新に失敗しました。"));
        var secondLog = new GenesisGuiNextNativeTabRow(new(string.Empty, string.Empty), null,
            Log: new(Now.AddMinutes(-1), GenesisGuiNextLogKind.Information, "YP一覧の最終成功日時です。"));
        GenesisGuiNextNativeTabRow[] rows = [notice, firstLog, secondLog];
        try
        {
            GenesisChannelListRenderedChannels.SetRows(listView, rows, static (_, _) => true);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 2);
            GenesisGuiNextNativeTabRow[] rebuilt = [secondLog with { }, notice, firstLog with { }];
            GenesisChannelListRenderedChannels.SetRows(listView, rebuilt, static (_, _) => true);
            Assert.Equal(0, GenesisChannelListRenderedChannels.FindAnchoredRow(listView, rebuilt));
            Assert.Null(GenesisChannelListRenderedChannels.Find(listView, 0));
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 1);
            Assert.Equal(0, GenesisChannelListRenderedChannels.FindAnchoredRow(listView, rows));
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(listView, [firstLog, secondLog]));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }

    [Fact]
    public async Task LogRefreshAndF5BoundaryReadExistingSnapshotWithoutCatalogWork()
    {
        var catalog = new CountingCatalog();
        var vm = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        await using var session = GenesisChannelCatalogSession.Create(catalog, vm, publisher);
        await session.RefreshAsync();
        Assert.Equal(1, catalog.Count);
        tabs.SelectedTab = GenesisGuiNextTab.Logs;
        var interaction = new GenesisMainWindowInteraction(vm, session);
        Assert.True(interaction.CanRefreshCurrentView);
        await interaction.RefreshCurrentViewAsync();
        Assert.Single(tabs.LogRows);
        await interaction.RefreshCurrentViewAsync(new CancellationToken(true));
        Assert.Equal(1, catalog.Count);
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        await interaction.RefreshCurrentViewAsync();
        Assert.Equal(2, catalog.Count);
    }

    private sealed class CountingCatalog : IGenesisChannelCatalog
    {
        public int Count { get; private set; }
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
        }
    }
}

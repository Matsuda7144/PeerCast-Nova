namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPriorityBUxTests
{
    [Fact]
    public void ColumnWidthMemoryPreservesAdjustedWidthsPerSchemaAcrossTabSwitches()
    {
        var memory = new GenesisNativeColumnWidthMemory();
        var channelColumns = GenesisGuiNextRecordingListPresentation.Columns(
            GenesisGuiNextTab.Channels,
            GenesisGuiNextRecordingPageMode.ActiveRecordings);
        var logColumns = GenesisGuiNextRecordingListPresentation.Columns(
            GenesisGuiNextTab.Logs,
            GenesisGuiNextRecordingPageMode.ActiveRecordings);

        var customWidths = Enumerable.Range(0, channelColumns.Count)
            .Select(i => 100 + i * 20)
            .ToArray();
        customWidths[0] = 5; // 最小幅へクランプされること

        memory.Capture(channelColumns, customWidths);

        // 別タブ（ログ）には保存していないため既定スキーマがそのまま返る
        Assert.Same(logColumns, memory.ApplySavedWidths(logColumns));

        // チャンネル系タブへ戻ると手動変更後の幅が復元される
        var restored = memory.ApplySavedWidths(channelColumns);
        Assert.Equal(channelColumns.Count, restored.Count);
        Assert.Equal(24, restored[0].PreferredWidth);
        for (var i = 1; i < channelColumns.Count; i++)
        {
            Assert.Equal(customWidths[i], restored[i].PreferredWidth);
        }
    }

    [Fact]
    public void NewAndFavoritesTabsApplyColumnSortAndKeepOfflineFavoritesOrdered()
    {
        var low = new GenesisChannelItem("Alpha", "pcp://example/a", Listeners: 2, Relays: 1, Type: "FLV");
        var high = new GenesisChannelItem("Beta", "pcp://example/b", Listeners: 20, Relays: 3, Type: "FLV");

        var byListenersDesc = new GenesisChannelSort(GenesisChannelColumn.ListenerRelay, GenesisChannelSortDirection.Descending);
        var newRows = SystemGenesisMainWindowInteractionBridgeFactory.CreateNewRowsForNativeList(
            [low, high],
            byListenersDesc);
        Assert.Equal(["Beta", "Alpha"], newRows.Select(r => r.DisplayChannel.Name));

        var favorites = new GenesisFavoriteChannelCollection();
        var offlineChannel = new GenesisChannelItem("Zeta Offline", "pcp://example/z", ChannelId: "00000000000000000000000000000001");
        _ = favorites.AddOrUpdate(offlineChannel);
        _ = favorites.AddOrUpdate(low);
        _ = favorites.AddOrUpdate(high);

        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(new GenesisChannelViewProjectionSnapshot([low, high], [low, high], []));

        var favoriteRows = SystemGenesisMainWindowInteractionBridgeFactory.CreateFavoriteRowsForNativeList(
            tabs,
            byListenersDesc);
        Assert.Equal(["Beta", "Alpha", "Zeta Offline"], favoriteRows.Select(r => r.DisplayChannel.Name));
    }

    [Fact]
    public void MainWindowPlacementStoreRoundTripsAndClampsOffscreenCoordinates()
    {
        var tempDir = Directory.CreateTempSubdirectory("nova-main-placement-");
        try
        {
            var path = Path.Combine(tempDir.FullName, "main-window-placement.json");
            var store = new GenesisMainWindowPlacementStore(path);
            Assert.Null(store.Load());

            var saved = new GenesisMainWindowPlacement(120, 90, 1280, 760, Maximized: true);
            store.Save(saved);
            Assert.Equal(saved, store.Load());

            // 破損 JSON でも例外にならず null を返す
            File.WriteAllText(path, "{ invalid json");
            Assert.Null(store.Load());

            // モニタ外座標でもワークエリア内に収まるようクランプする
            var offscreen = new GenesisMainWindowPlacement(3000, -500, 1400, 900, Maximized: false);
            var clamped = offscreen.ClampToWorkArea(0, 0, 1920, 1040, minWidth: 800, minHeight: 500);
            Assert.Equal(new GenesisMainWindowPlacement(520, 0, 1400, 900, Maximized: false), clamped);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void SearchInputGateDefersDuringImeCompositionAndDebouncesNormalTyping()
    {
        var gate = new GenesisChannelSearchInputGate();

        // 通常文字入力はデバウンス待ち、空文字は即反映
        Assert.Equal(GenesisChannelSearchInputAction.ScheduleDebounce, gate.OnTextChanged("abc"));
        Assert.Equal(GenesisChannelSearchInputAction.CommitImmediately, gate.OnTextChanged(string.Empty));
        Assert.True(gate.OnDebounceTimerElapsed());

        // IME 変換中は更新を保留し、変換中はタイマー発火や Enter でも確定しない
        gate.OnCompositionStart();
        Assert.True(gate.IsComposing);
        Assert.Equal(GenesisChannelSearchInputAction.DeferWhileComposing, gate.OnTextChanged("にほ"));
        Assert.False(gate.OnDebounceTimerElapsed());
        Assert.False(gate.OnEnterKey());

        // 変換確定後は即時反映を許可する
        Assert.True(gate.OnCompositionEnd());
        Assert.False(gate.IsComposing);
        Assert.True(gate.OnEnterKey());
    }

    [Fact]
    public void TabNavigationOrdersSidebarBeforeContentAreaAndPreservesTopToBottomWithoutSidebar()
    {
        var sidebarTabs = new List<GenesisGuiNextMainWindowKeyboardNavigation.TabStopBounds>
        {
            // 上部バー (右ペイン)
            new(Window: 101, Left: 260, Top: 14, Right: 420, Bottom: 40),
            new(Window: 102, Left: 620, Top: 14, Right: 700, Bottom: 40),
            new(Window: 103, Left: 430, Top: 14, Right: 600, Bottom: 40),
            // 左サイドバー
            new(Window: 1, Left: 12, Top: 14, Right: 140, Bottom: 44),
            new(Window: 2, Left: 12, Top: 48, Right: 140, Bottom: 78),
            new(Window: 3, Left: 12, Top: 82, Right: 140, Bottom: 112),
            new(Window: 4, Left: 12, Top: 500, Right: 72, Bottom: 528),
            // 一覧 & 詳細ボタン
            new(Window: 201, Left: 160, Top: 56, Right: 700, Bottom: 420),
            new(Window: 302, Left: 600, Top: 432, Right: 690, Bottom: 458),
            new(Window: 301, Left: 500, Top: 430, Right: 590, Bottom: 456),
        };

        GenesisGuiNextMainWindowKeyboardNavigation.SortTabStops(
            sidebarTabs,
            ownerClientLeft: 0,
            ownerClientTop: 0,
            sidebarSplitPixels: 152,
            rowBandHeight: 12);

        // 左サイドバー (1,2,3,4) → 上部バー左から右 (101,103,102) → 一覧 (201) → 詳細ボタン左から右 (301,302)
        Assert.Equal([1, 2, 3, 4, 101, 103, 102, 201, 301, 302], sidebarTabs.Select(s => (int)s.Window));

        // サイドバー無しレイアウトでは通常の上から下・左から右
        var classicTabs = new List<GenesisGuiNextMainWindowKeyboardNavigation.TabStopBounds>
        {
            new(Window: 20, Left: 200, Top: 40, Right: 280, Bottom: 64),
            new(Window: 10, Left: 12, Top: 12, Right: 180, Bottom: 36),
            new(Window: 11, Left: 190, Top: 14, Right: 260, Bottom: 38),
        };
        GenesisGuiNextMainWindowKeyboardNavigation.SortTabStops(
            classicTabs,
            ownerClientLeft: 0,
            ownerClientTop: 0,
            sidebarSplitPixels: 152,
            rowBandHeight: 12);
        Assert.Equal([10, 11, 20], classicTabs.Select(s => (int)s.Window));
    }
}

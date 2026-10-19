namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelListNativeSelectionTests
{
    [Fact]
    public void SuccessfulSelectionEnsuresRestoredRowIsVisible()
    {
        var calls = new List<(string Operation, nint Window, int Row)>();

        var selected = GenesisChannelListNativeSelection.SelectRow(
            (nint)7144,
            42,
            (window, row) =>
            {
                calls.Add(("select", window, row));
                return true;
            },
            (window, row) =>
            {
                calls.Add(("ensure-visible", window, row));
                return true;
            });

        Assert.True(selected);
        Assert.Equal(2, calls.Count);
        Assert.Equal(("select", (nint)7144, 42), calls[0]);
        Assert.Equal(("ensure-visible", (nint)7144, 42), calls[1]);
    }

    [Fact]
    public void FailedSelectionDoesNotScrollAnotherRowIntoView()
    {
        var ensureVisibleCalled = false;

        var selected = GenesisChannelListNativeSelection.SelectRow(
            (nint)7144,
            42,
            static (_, _) => false,
            (_, _) =>
            {
                ensureVisibleCalled = true;
                return true;
            });

        Assert.False(selected);
        Assert.False(ensureVisibleCalled);
    }

    [Fact]
    public void FailedRestoredSelectionClearsManagedSelectionWithoutErasingManualSource()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;
        viewModel.SourceText = "pcp://manual/override";
        var ensureVisibleCalled = false;

        var restored = GenesisChannelListNativeSelection.RestoreRow(
            (nint)7144,
            0,
            static (_, _) => false,
            (_, _) =>
            {
                ensureVisibleCalled = true;
                return true;
            },
            () => viewModel.SelectedChannel = null);

        Assert.False(restored);
        Assert.False(ensureVisibleCalled);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal("pcp://manual/override", viewModel.SourceText);
    }

    [Fact]
    public void FailedRestoreVisibilityKeepsManagedSelection()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;
        var managedSelectionCleared = false;

        var restored = GenesisChannelListNativeSelection.RestoreRow(
            (nint)7144,
            0,
            static (_, _) => true,
            static (_, _) => false,
            () =>
            {
                managedSelectionCleared = true;
                viewModel.SelectedChannel = null;
            });

        Assert.False(restored);
        Assert.False(managedSelectionCleared);
        Assert.Same(channel, viewModel.SelectedChannel);
    }

    [Fact]
    public void FailedPointerSelectionDoesNotChangeManagedSelection()
    {
        var first = new GenesisChannelItem(
            "First",
            "pcp://example/first");
        var clicked = new GenesisChannelItem(
            "Clicked",
            "pcp://example/clicked");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([first, clicked]);
        viewModel.SelectedChannel = first;
        var interaction = new GenesisMainWindowInteraction(viewModel);
        var managedSelectionCalled = false;

        var selected = GenesisChannelListNativeSelection.SelectRowWithoutEnsureVisible(
            (nint)7144,
            1,
            static (_, _) => false,
            row =>
            {
                managedSelectionCalled = true;
                return interaction.SelectChannel(row);
            });

        Assert.False(selected);
        Assert.False(managedSelectionCalled);
        Assert.Same(first, viewModel.SelectedChannel);
    }

    [Fact]
    public void FailedManagedPointerSelectionClearsNativeSelection()
    {
        var calls = new List<string>();

        var selected = GenesisChannelListNativeSelection.SelectRowWithoutEnsureVisible(
            (nint)7144,
            1,
            (window, row) =>
            {
                Assert.Equal((nint)7144, window);
                Assert.Equal(1, row);
                calls.Add("select-native");
                return true;
            },
            (window, row) =>
            {
                Assert.Equal((nint)7144, window);
                Assert.Equal(1, row);
                calls.Add("clear-native");
                return true;
            },
            row =>
            {
                Assert.Equal(1, row);
                calls.Add("select-managed");
                return false;
            });

        Assert.False(selected);
        Assert.Equal(
            ["select-native", "select-managed", "clear-native"],
            calls);
    }

    [Fact]
    public void NativeDeselectionClearsManagedSelectionWithoutErasingManualSource()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;
        viewModel.SourceText = "pcp://manual/override";
        var managedSelectionCalled = false;

        var synchronized = GenesisChannelListNativeSelection.SynchronizeManagedSelection(
            -1,
            selectionChangesSuppressed: false,
            row =>
            {
                managedSelectionCalled = true;
                return row >= 0;
            },
            () => viewModel.SelectedChannel = null);

        Assert.True(synchronized);
        Assert.False(managedSelectionCalled);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal("pcp://manual/override", viewModel.SourceText);
    }

    [Fact]
    public void RebuildDeselectionDoesNotClearManagedSelection()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;
        var managedSelectionCalled = false;
        var managedSelectionCleared = false;

        var synchronized = GenesisChannelListNativeSelection.SynchronizeManagedSelection(
            -1,
            selectionChangesSuppressed: true,
            row =>
            {
                managedSelectionCalled = true;
                return row >= 0;
            },
            () =>
            {
                managedSelectionCleared = true;
                viewModel.SelectedChannel = null;
            });

        Assert.False(synchronized);
        Assert.False(managedSelectionCalled);
        Assert.False(managedSelectionCleared);
        Assert.Same(channel, viewModel.SelectedChannel);
    }

    [Fact]
    public void GuiNextRestoreFollowsChannelIdAcrossSourceChange()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var selected = new GenesisChannelItem(
            "Selected",
            "pcp://tracker-a.example/channel/id",
            ChannelId: channelId);
        var refreshed = selected with
        {
            Source = "pcp://tracker-b.example/channel/id",
        };
        var rows = GenesisGuiNextNativeTabProjection.CreateLiveChannels([refreshed]);

        var row = SystemGenesisMainWindowInteractionBridgeFactory.FindSelectedRow(
            rows,
            selected);

        Assert.Equal(0, row);
    }

    [Fact]
    public void GuiNextRestoreDoesNotAliasDifferentChannelIdByMatchingSource()
    {
        var selected = new GenesisChannelItem(
            "Selected",
            "pcp://tracker.example/channel/id",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var differentChannel = new GenesisChannelItem(
            "Different",
            selected.Source,
            ChannelId: "ABCDEF0123456789ABCDEF0123456789");
        var rows = GenesisGuiNextNativeTabProjection.CreateLiveChannels([differentChannel]);

        var row = SystemGenesisMainWindowInteractionBridgeFactory.FindSelectedRow(
            rows,
            selected);

        Assert.Equal(-1, row);
    }

    [Fact]
    public void GuiNextRestoreSkipsDisplayOnlyRowsBeforeInteractiveIdentityMatch()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var selected = new GenesisChannelItem(
            "Selected",
            "pcp://example/channel",
            ChannelId: channelId);
        var offlineDisplay = selected with { Name = "Offline" };
        var live = selected with { Name = "Live" };
        IReadOnlyList<GenesisGuiNextNativeTabRow> rows =
        [
            new(offlineDisplay, InteractiveChannel: null),
            new(live, live),
        ];

        var row = SystemGenesisMainWindowInteractionBridgeFactory.FindSelectedRow(
            rows,
            selected);

        Assert.Equal(1, row);
    }

    [Fact]
    public void RenderedSelectionAnchorFollowsStableIdentityAcrossRefreshReorder()
    {
        var listView = (nint)0x71440002;
        const string selectedId = "0123456789ABCDEF0123456789ABCDEF";
        var selected = new GenesisChannelItem(
            "Selected",
            "pcp://tracker-a.example/channel/id",
            ChannelId: selectedId);
        var other = new GenesisChannelItem(
            "Other",
            "pcp://example/other",
            ChannelId: "ABCDEF0123456789ABCDEF0123456789");
        var refreshed = selected with
        {
            Name = "Selected refreshed",
            Source = "pcp://tracker-b.example/channel/id",
        };
        var restoredRow = -1;

        try
        {
            GenesisChannelListRenderedChannels.SetRows(
                listView,
                GenesisGuiNextNativeTabProjection.CreateLiveChannels([selected, other]));
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 0);

            GenesisChannelListRenderedChannels.SetRows(
                listView,
                GenesisGuiNextNativeTabProjection.CreateLiveChannels([other, refreshed]),
                (window, row) =>
                {
                    Assert.Equal(listView, window);
                    restoredRow = row;
                    return true;
                });

            Assert.Equal(1, restoredRow);
            Assert.Same(
                refreshed,
                GenesisChannelListRenderedChannels.FindRow(listView, restoredRow)?.DisplayChannel);
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }

    [Fact]
    public void RenderedSelectionAnchorClearsWhenSelectedRowDisappears()
    {
        var listView = (nint)0x71440003;
        var selected = new GenesisChannelItem(
            "Selected",
            "pcp://example/selected",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var other = new GenesisChannelItem(
            "Other",
            "pcp://example/other",
            ChannelId: "ABCDEF0123456789ABCDEF0123456789");
        var restoreCalls = 0;

        try
        {
            GenesisChannelListRenderedChannels.SetRows(
                listView,
                GenesisGuiNextNativeTabProjection.CreateLiveChannels([selected, other]));
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 0);

            GenesisChannelListRenderedChannels.SetRows(
                listView,
                GenesisGuiNextNativeTabProjection.CreateLiveChannels([other]),
                (_, _) =>
                {
                    restoreCalls++;
                    return true;
                });

            Assert.Equal(0, restoreCalls);
            Assert.Equal(
                -1,
                GenesisChannelListRenderedChannels.FindAnchoredRow(
                    listView,
                    GenesisGuiNextNativeTabProjection.CreateLiveChannels([selected, other])));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }

    [Fact]
    public void RecordingSelectionAnchorDoesNotFollowReplacementSessionGeneration()
    {
        var listView = (nint)0x71440004;
        var channelId = new PeerCast.Nova.Core.Channels.ChannelId(
            Guid.ParseExact("0123456789ABCDEF0123456789ABCDEF", "N"));
        var display = new GenesisChannelItem(
            "Recording",
            string.Empty,
            ChannelId: channelId.Value.ToString("N"));
        var firstSessionId = new PeerCast.Nova.Core.Recording.RecordingSessionId(
            Guid.ParseExact("102132435465768798A9BABBDCDDEF00", "N"));
        var replacementSessionId = new PeerCast.Nova.Core.Recording.RecordingSessionId(
            Guid.ParseExact("FFEEDDCCBBAA99887766554433221100", "N"));
        var first = new GenesisGuiNextNativeTabRow(
            display,
            InteractiveChannel: null,
            ActiveRecording: new GenesisGuiNextActiveRecording(
                channelId,
                firstSessionId,
                PeerCast.Nova.Core.Recording.RecordingSessionOrigin.Manual,
                "Recording",
                "録画中",
                null,
                null));
        var replacement = first with
        {
            ActiveRecording = first.ActiveRecording! with
            {
                SessionId = replacementSessionId,
            },
        };
        var restoreCalls = 0;

        try
        {
            GenesisChannelListRenderedChannels.SetRows(listView, [first]);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 0);
            GenesisChannelListRenderedChannels.SetRows(
                listView,
                [replacement],
                (_, _) =>
                {
                    restoreCalls++;
                    return true;
                });

            Assert.Equal(0, restoreCalls);
            Assert.Equal(
                -1,
                GenesisChannelListRenderedChannels.FindAnchoredRow(listView, [replacement]));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }

    [Fact]
    public void RenderedSnapshotTracksNativeRowsUntilRemoved()
    {
        var listView = (nint)0x71440001;
        var rendered = new GenesisChannelItem(
            "Rendered",
            "pcp://example/rendered");
        var refreshed = rendered with
        {
            Description = "[game - refreshed]",
        };

        try
        {
            GenesisChannelListRenderedChannels.Set(listView, [rendered]);

            Assert.Same(
                rendered,
                GenesisChannelListRenderedChannels.Find(listView, 0));
            Assert.Null(GenesisChannelListRenderedChannels.Find(listView, 1));

            GenesisChannelListRenderedChannels.Set(listView, [refreshed]);

            Assert.Same(
                refreshed,
                GenesisChannelListRenderedChannels.Find(listView, 0));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }

        Assert.Null(GenesisChannelListRenderedChannels.Find(listView, 0));
    }
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextChannelCardTests
{
    [Fact]
    public void OnlyModernChannelPlayingAndLogDetailsUseTheCard()
    {
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextChannelCardLayout.Applies(false, tab));
            Assert.Equal(tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites or
                GenesisGuiNextTab.Playing or GenesisGuiNextTab.Logs,
                GenesisGuiNextChannelCardLayout.Applies(true, tab));
            Assert.Equal(tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites,
                GenesisGuiNextChannelCardLayout.StylesSidebarPlayback(tab));
        }
    }

    [Fact]
    public void ListFrameIsSharedOnlyByTheModernChannelRecordingAndLogPages()
    {
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextChannelListStyle.FrameApplies(false, tab));
            Assert.Equal(tab is GenesisGuiNextTab.Channels or GenesisGuiNextTab.New or GenesisGuiNextTab.Favorites or
                GenesisGuiNextTab.Playing or GenesisGuiNextTab.Recording or GenesisGuiNextTab.Logs,
                GenesisGuiNextChannelListStyle.FrameApplies(true, tab));
        }
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void LogTextAndRelayControlsFitTheCardAtMinimumAndNormalSizes(uint dpi)
    {
        int Scale(int value) => GenesisMainWindowLayout.Scale(value, dpi);
        foreach (var (width, height) in new[] { (1088, 474), (1120, 620) })
        {
            Assert.True(GenesisMainWindowLayout.TryResolve(Scale(width), Scale(height), dpi,
                out _, out _, out _, out var details));
            var layout = new GenesisGuiNextChannelCardLayout(details.Width);
            var regions = new[] { layout.LogTitle, layout.LogDescription, layout.LogScope,
                GenesisGuiNextChannelCardLayout.Record, layout.LogRelayStatus };
            Assert.Equal(layout.Title.Top, layout.LogTitle.Top);
            Assert.Equal(layout.Description.Top, layout.LogDescription.Top);
            Assert.Equal(layout.LogTitle.Right, layout.LogScope.Right);
            var previousBottom = Scale(6);
            foreach (var bounds in regions)
            {
                Assert.True(Scale(bounds.Top) >= previousBottom);
                Assert.True(Scale(bounds.Left) > Scale(6));
                Assert.True(Scale(bounds.Left) + Scale(bounds.Width) < Scale(details.Width - 6));
                Assert.True(Scale(bounds.Height) >= Scale(GenesisGuiNextDesign.BodyFontHeight));
                previousBottom = Scale(bounds.Top) + Scale(bounds.Height);
            }
            Assert.True(previousBottom < Scale(details.Height - 6));
        }
    }

    [Theory]
    [InlineData(1088, 474, 96u)]
    [InlineData(1120, 620, 96u)]
    [InlineData(1920, 1080, 96u)]
    [InlineData(1088, 474, 144u)]
    [InlineData(1088, 474, 192u)]
    public void CardContentsFitTheExistingPaneWithoutOverlapAtMinimumAndExpandedSizes(int width, int height, uint dpi)
    {
        Assert.True(GenesisMainWindowLayout.TryResolve(GenesisMainWindowLayout.Scale(width, dpi),
            GenesisMainWindowLayout.Scale(height, dpi), dpi, out _, out _, out var list, out var details));
        Assert.Equal(224, details.Height);
        Assert.True(list.Height >= GenesisGuiNextAppShellPlacement.MinimumListHeight);
        var layout = new GenesisGuiNextChannelCardLayout(details.Width);
        var texts = new[] { layout.Title, layout.Description, layout.Metadata, layout.Identity, layout.RecordingStatus, layout.RelayStatus };
        var buttons = new[] { GenesisGuiNextChannelCardLayout.Record, GenesisGuiNextChannelCardLayout.Stop,
            GenesisGuiNextChannelCardLayout.RelayStop };
        var all = texts.Concat(buttons).ToArray();
        foreach (var bounds in all)
        {
            Assert.InRange(bounds.Left, 7, details.Width - 7);
            Assert.InRange(bounds.Right, bounds.Left + 1, details.Width - 7);
            Assert.InRange(bounds.Top, 7, details.Height - 7);
            Assert.InRange(bounds.Bottom, bounds.Top + 1, details.Height - 7);
            Assert.True(GenesisMainWindowLayout.Scale(bounds.Height, dpi) > 0);
        }
        for (var i = 0; i < all.Length; i++)
            for (var j = i + 1; j < all.Length; j++)
                Assert.True(all[i].Right <= all[j].Left || all[j].Right <= all[i].Left ||
                    all[i].Bottom <= all[j].Top || all[j].Bottom <= all[i].Top, $"Overlap: {i}, {j}");
        Assert.Equal(GenesisGuiNextChannelCardLayout.Record.Height, GenesisGuiNextChannelCardLayout.RelayStop.Height);
        Assert.True(layout.Title.Right < layout.DividerLeft && layout.Metadata.Left > layout.DividerLeft);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void ChannelsPolishPreservesDenseRowsColumnsAndUnobstructedInformation(uint dpi)
    {
        foreach (var size in new[] { (1088, 474), (1120, 650), (1920, 1080) })
        {
            Assert.True(GenesisMainWindowLayout.TryResolve(GenesisMainWindowLayout.Scale(size.Item1, dpi), GenesisMainWindowLayout.Scale(size.Item2, dpi), dpi,
                out _, out _, out var list, out var details));
            var widths = GenesisGuiNextChannelListStyle.ColumnWidths(list.Width);
            Assert.Equal(GenesisChannelListPresentation.Columns.Count, widths.Length);
            Assert.True(widths.All(w => w >= 56));
            Assert.True(widths[0] >= 200 && widths[1] >= 260);
            if (list.Width >= 884) Assert.Equal(list.Width - 4, widths.Sum());
            Assert.InRange(GenesisGuiNextChannelListStyle.RowHeight, GenesisGuiNextDesign.RowImageHeight, 36);
            var card = new GenesisGuiNextChannelCardLayout(details.Width);
            var regions = new[] { card.Title, card.Description, card.Metadata, card.Identity, card.RecordingStatus, card.RelayStatus }
                .Concat(new[] { GenesisGuiNextChannelCardLayout.Record, GenesisGuiNextChannelCardLayout.Stop, GenesisGuiNextChannelCardLayout.RelayStop }).ToArray();
            Assert.Equal(22, card.Title.Left); // text uses the left edge; there is no thumbnail slot
            Assert.True(card.Metadata.Height >= 5 * GenesisGuiNextDesign.BodyFontHeight);
            foreach (var r in regions)
                Assert.True(r.Left >= 7 && r.Right <= details.Width - 7 && r.Top >= 7 && r.Bottom <= 218);
            for (var i = 0; i < regions.Length; i++)
                for (var j = i + 1; j < regions.Length; j++)
                    Assert.True(regions[i].Right <= regions[j].Left || regions[j].Right <= regions[i].Left ||
                        regions[i].Bottom <= regions[j].Top || regions[j].Bottom <= regions[i].Top);
        }
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextChannelListStyle.Applies(false, tab));
            Assert.Equal(tab == GenesisGuiNextTab.Channels, GenesisGuiNextChannelListStyle.Applies(true, tab));
        }
    }

    [Fact]
    public void ModernHidesIdleRelayButKeepsStopAndFailureEvidenceAcrossChannelAndRecordingTabs()
    {
        foreach (var tab in new[] { GenesisGuiNextTab.Channels, GenesisGuiNextTab.New, GenesisGuiNextTab.Favorites,
            GenesisGuiNextTab.Playing, GenesisGuiNextTab.Recording, GenesisGuiNextTab.Logs })
        {
            Assert.Equal(new(false, false, false), GenesisGuiNextRelayControlsPresentation.Resolve(true, tab, new(), false));
            Assert.Equal(new(true, true, true), GenesisGuiNextRelayControlsPresentation.Resolve(false, tab, new(), false));
            foreach (var state in Enum.GetValues<GenesisRelayActivationState>())
            {
                var owned = new GenesisRelayActivationSnapshot(State: state, CleanupConfirmed: false);
                Assert.Equal(new(false, true, true), GenesisGuiNextRelayControlsPresentation.Resolve(true, tab, owned, true));
            }
            Assert.Equal(new(false, false, true), GenesisGuiNextRelayControlsPresentation.Resolve(true, tab,
                new(State: GenesisRelayActivationState.Failed, Error: GenesisRelayActivationError.ShutdownFailed, CleanupConfirmed: false), false));
            Assert.True(GenesisGuiNextRelayControlsPresentation.Resolve(true, tab,
                new(Error: GenesisRelayActivationError.BindFailed), false).Status);
        }
        Assert.Equal(new(false, false, false), GenesisGuiNextRelayControlsPresentation.Resolve(false, GenesisGuiNextTab.Publishing, new(), true));
    }

    [Fact]
    public void DisabledActionsDoNotShowActivePlaybackRecordingOrHoverAccents()
    {
        var disabled = GenesisGuiNextDesign.CardButtonColors(false, false, true, false, false);
        foreach (var play in new[] { false, true })
            foreach (var record in new[] { false, true })
                foreach (var hot in new[] { false, true })
                    Assert.Equal(disabled, GenesisGuiNextDesign.CardButtonColors(play, record, true, hot, true));
        Assert.NotEqual(GenesisGuiNextDesign.Text, disabled.Text);
        Assert.NotEqual(GenesisGuiNextDesign.Recording, disabled.Border);
        Assert.NotEqual(GenesisGuiNextDesign.Selected, disabled.Background);
    }

    [Fact]
    public void EnabledActionsDistinguishPlaybackRecordingSecondaryHoverAndPress()
    {
        var play = GenesisGuiNextDesign.CardButtonColors(true, false, false, false, false);
        var record = GenesisGuiNextDesign.CardButtonColors(false, true, false, false, false);
        var copy = GenesisGuiNextDesign.CardButtonColors(false, false, false, false, false);
        Assert.Equal(GenesisGuiNextDesign.Selected, play.Background);
        Assert.Equal(GenesisGuiNextDesign.Recording, record.Border);
        Assert.NotEqual(record.Border, copy.Border);
        Assert.Equal(GenesisGuiNextDesign.Hover, GenesisGuiNextDesign.CardButtonColors(false, true, false, true, false).Background);
        Assert.Equal(GenesisGuiNextDesign.Selected, GenesisGuiNextDesign.CardButtonColors(false, true, false, true, true).Background);
    }
}

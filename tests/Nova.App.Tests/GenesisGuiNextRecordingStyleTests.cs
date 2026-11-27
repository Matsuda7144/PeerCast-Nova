namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRecordingStyleTests
{
    [Fact]
    public void RecordingStyleIsRestrictedToModernRecordingTab()
    {
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextRecordingLayout.Applies(false, tab));
            Assert.Equal(tab == GenesisGuiNextTab.Recording, GenesisGuiNextRecordingLayout.Applies(true, tab));
        }
    }

    [Theory]
    [InlineData(0, 96u)]
    [InlineData(1, 96u)]
    [InlineData(2, 96u)]
    [InlineData(3, 96u)]
    [InlineData(0, 144u)]
    [InlineData(1, 144u)]
    [InlineData(2, 144u)]
    [InlineData(3, 144u)]
    [InlineData(0, 192u)]
    [InlineData(1, 192u)]
    [InlineData(2, 192u)]
    [InlineData(3, 192u)]
    public void ModesFitMinimumAndExpandedPanelsWithoutOverlap(int modeValue, uint dpi)
    {
        var mode = (GenesisGuiNextRecordingPageMode)modeValue;
        foreach (var width in new[] { GenesisMainWindowLayout.MinimumContentWidth, 1724 })
        {
            var layout = new GenesisGuiNextRecordingLayout(width, mode);
            int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
            var toolbar = Enumerable.Range(0, 10).Select(GenesisGuiNextRecordingLayout.Toolbar).ToArray();
            for (var i = 0; i < toolbar.Length; i++)
            {
                Assert.InRange(toolbar[i].Left, 12, width - 16);
                Assert.True(S(toolbar[i].Right) <= S(width - 16));
                Assert.Equal(28, toolbar[i].Height);
                if (i > 0) Assert.True(S(toolbar[i - 1].Right) < S(toolbar[i].Left));
            }
            GenesisGuiNextAppShellPlacement.WindowRectangle[] blocks = mode switch
            {
                GenesisGuiNextRecordingPageMode.History => [layout.History, layout.Status],
                GenesisGuiNextRecordingPageMode.Rules => [layout.Title, layout.Description, layout.Metadata, layout.Identity],
                _ => [layout.Title, layout.Description, layout.Metadata, layout.Identity, layout.Status],
            };
            var bottom = toolbar[0].Bottom;
            foreach (var block in blocks)
            {
                Assert.True(S(bottom) < S(block.Top));
                Assert.True(S(block.Right) <= S(width - 16));
                Assert.True(S(block.Bottom) <= S(GenesisGuiNextAppShellPlacement.DetailsHeight - 12));
                bottom = block.Bottom;
            }
            Assert.True(S(bottom) < S(GenesisGuiNextRecordingLayout.RelayStart.Top));
            Assert.True(GenesisGuiNextRecordingLayout.RelayStart.Right < GenesisGuiNextRecordingLayout.RelayStop.Left);
            Assert.True(GenesisGuiNextRecordingLayout.RelayStop.Right < layout.RelayStatus.Left);
            Assert.True(layout.RelayStatus.Right <= width - 16);
            Assert.True(GenesisGuiNextRecordingLayout.Copy.Right < GenesisGuiNextRecordingLayout.RelayStart.Left);
            if (mode == GenesisGuiNextRecordingPageMode.ActiveRecordings)
            {
                Assert.True(S(layout.Status.Bottom) < S(GenesisGuiNextRecordingLayout.Record(false).Top));
                Assert.True(GenesisGuiNextRecordingLayout.Record(false).Right < GenesisGuiNextRecordingLayout.Stop.Left);
                Assert.True(GenesisGuiNextRecordingLayout.Stop.Right < GenesisGuiNextRecordingLayout.Copy.Left);
                Assert.True(GenesisGuiNextRecordingLayout.Record(true).Right < GenesisGuiNextRecordingLayout.Copy.Left);
                Assert.True(GenesisGuiNextRecordingLayout.Copy.Bottom <= GenesisGuiNextAppShellPlacement.DetailsHeight - 12);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModeSelectionAndActionStatesRemainDistinct(bool selected)
    {
        var normal = GenesisGuiNextDesign.RecordingButtonColors(selected, false, false, false, false);
        var focused = GenesisGuiNextDesign.RecordingButtonColors(selected, false, false, false, true);
        var pressed = GenesisGuiNextDesign.RecordingButtonColors(selected, false, false, true, false);
        Assert.Equal(GenesisGuiNextDesign.Accent, focused.Border);
        Assert.Equal(normal.Background, focused.Background);
        Assert.Equal(GenesisGuiNextDesign.Selected, pressed.Background);
        Assert.Equal(GenesisGuiNextDesign.DisabledText, GenesisGuiNextDesign.RecordingButtonColors(selected, true, true, true, true).Text);
        Assert.Equal(selected ? GenesisGuiNextDesign.Selected : GenesisGuiNextDesign.Input, normal.Background);
        if (!selected) Assert.NotEqual(normal.Background, GenesisGuiNextDesign.RecordingButtonColors(false, false, true, false, false).Background);
    }

    [Theory]
    [InlineData(false, false, false, GenesisGuiNextDesign.SecondaryText)]
    [InlineData(false, true, false, GenesisGuiNextDesign.Recording)]
    [InlineData(false, false, true, GenesisGuiNextDesign.Recording)]
    [InlineData(true, false, false, GenesisGuiNextDesign.Warning)]
    [InlineData(true, true, true, GenesisGuiNextDesign.Warning)]
    public void StatusUsesExistingFailureActivityAndTransitionEvidence(bool failed, bool active, bool transitioning, int expected)
    {
        Assert.Equal(expected, GenesisGuiNextDesign.RecordingStatusColor(failed, active, transitioning));
    }

    [Theory]
    [InlineData(0, 96u)]
    [InlineData(1, 96u)]
    [InlineData(2, 96u)]
    [InlineData(3, 96u)]
    [InlineData(0, 144u)]
    [InlineData(1, 144u)]
    [InlineData(2, 144u)]
    [InlineData(3, 144u)]
    [InlineData(0, 192u)]
    [InlineData(1, 192u)]
    [InlineData(2, 192u)]
    [InlineData(3, 192u)]
    public void ModesFitResizedDetailsPaneWithoutOverlap(int modeValue, uint dpi)
    {
        var mode = (GenesisGuiNextRecordingPageMode)modeValue;
        int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
        foreach (var width in new[] { GenesisMainWindowLayout.MinimumContentWidth, 1724 })
        {
            foreach (var height in new[] { 140, 180, 224, 340 })
            {
                var layout = new GenesisGuiNextRecordingLayout(width, mode, height);
                var toolbar = Enumerable.Range(0, 10).Select(layout.ToolbarButton).ToArray();
                for (var i = 0; i < toolbar.Length; i++)
                {
                    Assert.InRange(toolbar[i].Left, 12, width - 16);
                    Assert.True(S(toolbar[i].Right) <= S(width - 16));
                    Assert.True(S(toolbar[i].Top) >= S(6));
                    Assert.True(S(toolbar[i].Top) + S(toolbar[i].Height) < S(layout.TopDividerY));
                    if (i > 0) Assert.True(S(toolbar[i - 1].Left) + S(toolbar[i - 1].Width) < S(toolbar[i].Left));
                }

                GenesisGuiNextAppShellPlacement.WindowRectangle[] blocks = mode switch
                {
                    GenesisGuiNextRecordingPageMode.History => [layout.History, layout.Status],
                    GenesisGuiNextRecordingPageMode.Rules => [layout.Title, layout.Description, layout.Metadata, layout.Identity],
                    _ => [layout.Title, layout.Description, layout.Metadata, layout.Identity, layout.Status],
                };
                var bottom = S(layout.TopDividerY);
                foreach (var block in blocks)
                {
                    Assert.True(S(block.Top) >= bottom);
                    Assert.True(S(block.Right) <= S(width - 16));
                    Assert.True(S(block.Top) + S(block.Height) <= S(layout.BottomDividerY));
                    bottom = S(block.Top) + S(block.Height);
                }

                var actions = mode == GenesisGuiNextRecordingPageMode.ActiveRecordings
                    ? new[] { layout.RecordButton(false), layout.StopButton, layout.CopyButton, layout.RelayStartButton, layout.RelayStopButton, layout.RelayStatus }
                    : new[] { layout.RelayStartButton, layout.RelayStopButton, layout.RelayStatus };
                foreach (var action in actions)
                {
                    Assert.True(S(action.Top) >= S(layout.BottomDividerY));
                    Assert.True(S(action.Top) + S(action.Height) <= S(height - 6));
                    Assert.True(S(action.Right) <= S(width - 16));
                }
                Assert.True(S(layout.RecordButton(true).Left) + S(layout.RecordButton(true).Width) < S(layout.CopyButton.Left));
                Assert.True(S(layout.CopyButton.Left) + S(layout.CopyButton.Width) < S(layout.RelayStartButton.Left));
                Assert.True(S(layout.RelayStartButton.Left) + S(layout.RelayStartButton.Width) < S(layout.RelayStopButton.Left));
                Assert.True(S(layout.RelayStopButton.Left) + S(layout.RelayStopButton.Width) < S(layout.RelayStatus.Left));
            }
        }
    }
}

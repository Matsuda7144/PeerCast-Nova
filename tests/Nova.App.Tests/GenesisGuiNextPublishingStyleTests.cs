using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPublishingStyleTests
{
    [Fact]
    public void OnlyModernPublishingUsesThePanel()
    {
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextPublishingModernLayout.Applies(false, tab));
            Assert.Equal(tab == GenesisGuiNextTab.Publishing, GenesisGuiNextPublishingModernLayout.Applies(true, tab));
        }
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void GuideAndDistinctLifecycleActionsFitMinimumAndExpandedPanels(uint dpi)
    {
        foreach (var width in new[] { GenesisMainWindowLayout.MinimumContentWidth, 920, 1724 })
        {
            var layout = new GenesisGuiNextPublishingModernLayout(width);
            GenesisGuiNextPublishingGuideRectangle[] boxes = [layout.Title, layout.Start, layout.Finish, layout.Interrupt,
                layout.Settings, layout.State, GenesisGuiNextPublishingModernLayout.ServerLabel, layout.Server, layout.ServerCopy,
                GenesisGuiNextPublishingModernLayout.KeyLabel, layout.Key, layout.Reveal, layout.KeyCopy, layout.Instructions, layout.Feedback];
            int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
            foreach (var box in boxes)
            {
                Assert.True(box.Width > 0 && box.Height > 0);
                Assert.True(S(box.Left) >= S(16) && S(box.Right) <= S(width - 16));
                Assert.InRange(S(box.Top), S(6), S(GenesisGuiNextAppShellPlacement.DetailsHeight - 6));
                Assert.True(S(box.Bottom) <= S(GenesisGuiNextAppShellPlacement.DetailsHeight - 6));
            }
            for (var i = 0; i < boxes.Length; i++)
                for (var j = i + 1; j < boxes.Length; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    Assert.False(S(a.Left) < S(b.Right) && S(b.Left) < S(a.Right) &&
                        S(a.Top) < S(b.Bottom) && S(b.Top) < S(a.Bottom), $"Controls {i}/{j} overlap at {width}/{dpi}.");
                }
            Assert.True(layout.Server.Width > layout.Key.Width);
            Assert.True(layout.Start.Right < layout.Finish.Left && layout.Finish.Right < layout.Interrupt.Left);
            Assert.True(layout.Instructions.Height >= 64);
            Assert.True(layout.Feedback.Height >= 2 * GenesisGuiNextDesign.BodyFontHeight);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void StartFinishAndInterruptHaveDisabledHoverPressedAndFocusStates(bool start, bool interrupt)
    {
        var normal = GenesisGuiNextDesign.PublishingButtonColors(start, interrupt, false, false, false, false);
        var hover = GenesisGuiNextDesign.PublishingButtonColors(start, interrupt, false, true, false, false);
        var focus = GenesisGuiNextDesign.PublishingButtonColors(start, interrupt, false, false, false, true);
        var pressed = GenesisGuiNextDesign.PublishingButtonColors(start, interrupt, false, false, true, false);
        var disabled = GenesisGuiNextDesign.PublishingButtonColors(start, interrupt, true, true, true, true);
        Assert.Equal(interrupt ? GenesisGuiNextDesign.Warning : GenesisGuiNextDesign.Border, normal.Border);
        Assert.Equal(GenesisGuiNextDesign.Hover, hover.Background);
        Assert.Equal(GenesisGuiNextDesign.Accent, hover.Border);
        Assert.Equal(normal.Background, focus.Background);
        Assert.Equal(GenesisGuiNextDesign.Accent, focus.Border);
        Assert.Equal(GenesisGuiNextDesign.Selected, pressed.Background);
        Assert.Equal(GenesisGuiNextDesign.DisabledText, disabled.Text);
        Assert.Equal(GenesisGuiNextDesign.Separator, disabled.Border);
        Assert.NotEqual(GenesisGuiNextDesign.PublishingButtonColors(!start, interrupt, false, false, false, false).Background, normal.Background);
    }

    [Theory]
    [InlineData(NovaObsPublishingTone.Normal, GenesisGuiNextDesign.SecondaryText)]
    [InlineData(NovaObsPublishingTone.Active, GenesisGuiNextDesign.Success)]
    [InlineData(NovaObsPublishingTone.Warning, GenesisGuiNextDesign.Warning)]
    [InlineData(NovaObsPublishingTone.Special, GenesisGuiNextDesign.Special)]
    public void StatusColorUsesExistingPresentationTone(NovaObsPublishingTone tone, int expected) =>
        Assert.Equal(expected, GenesisGuiNextDesign.PublishingStatusColor(tone));

    [Fact]
    public void LongValuesUseSingleLineEllipsisAndFeedbackWraps()
    {
        Assert.NotEqual(0u, GenesisGuiNextPublishingModernLayout.SingleLineFlags & 0x0020);
        Assert.NotEqual(0u, GenesisGuiNextPublishingModernLayout.SingleLineFlags & 0x8000);
        Assert.Equal(0u, GenesisGuiNextPublishingModernLayout.FeedbackFlags & 0x0020);
        Assert.NotEqual(0u, GenesisGuiNextPublishingModernLayout.FeedbackFlags & 0x0010);
        Assert.NotEqual(0u, GenesisGuiNextPublishingModernLayout.FeedbackFlags & 0x8000);
    }
}

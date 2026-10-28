namespace PeerCast.Nova.App.Tests;

public sealed class PublishingGuideLayoutTests
{
    public static TheoryData<int, int> WidthsAndDpi
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var width in new[] { 320, 400, 600, 920, 1200 })
                foreach (var dpi in new[] { 96, 144, 192 })
                    data.Add(width, dpi);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(WidthsAndDpi))]
    public void VisiblePublishingRectanglesFitAndNeverIntersect(int width, int dpi)
    {
        var layout = GenesisGuiNextPublishingGuideLayout.ForWidth(width);
        var rectangles = Rectangles(layout);
        // The shell currently has no per-monitor DPI awareness. Windows scales
        // its logical client coordinates together; this verifies those bounds,
        // not physical glyph rendering or a new per-monitor layout contract.
        double Scale(int value) => value * dpi / 96d;
        foreach (var rectangle in rectangles)
        {
            Assert.True(rectangle.Width > 0 && rectangle.Height > 0);
            Assert.InRange(Scale(rectangle.Left), 0, Scale(width));
            Assert.InRange(Scale(rectangle.Right), 0, Scale(width));
            Assert.InRange(Scale(rectangle.Top), 0, Scale(GenesisGuiNextAppShellPlacement.DetailsHeight));
            Assert.InRange(Scale(rectangle.Bottom), 0, Scale(GenesisGuiNextAppShellPlacement.DetailsHeight));
        }
        for (var first = 0; first < rectangles.Length; first++)
            for (var second = first + 1; second < rectangles.Length; second++)
            {
                var a = rectangles[first];
                var b = rectangles[second];
                Assert.False(Scale(a.Left) < Scale(b.Right) && Scale(b.Left) < Scale(a.Right) &&
                    Scale(a.Top) < Scale(b.Bottom) && Scale(b.Top) < Scale(a.Bottom),
                    $"Rectangles {first} and {second} overlap at width {width}, DPI {dpi}.");
            }
    }

    [Fact]
    public void WidthChangesRecomputeValuesAndLifecycleButtonsWithoutRetainingOldPositions()
    {
        var narrow = GenesisGuiNextPublishingGuideLayout.ForWidth(320);
        var wide = GenesisGuiNextPublishingGuideLayout.ForWidth(920);
        Assert.True(wide.Server.Width > narrow.Server.Width);
        Assert.True(wide.Key.Width > narrow.Key.Width);
        Assert.True(wide.Start.Left > narrow.Start.Left);
        Assert.Equal(Rectangles(narrow), Rectangles(GenesisGuiNextPublishingGuideLayout.ForWidth(320)));
        Assert.Equal(78, narrow.Start.Width);
        Assert.Equal(78, narrow.Finish.Width);
        Assert.Equal(78, narrow.Interrupt.Width);
    }

    [Fact]
    public void PublishingExcludesRelayAndReturningToEveryExistingTabRestoresIt()
    {
        foreach (var tab in Enum.GetValues<GenesisGuiNextTab>())
        {
            Assert.False(GenesisGuiNextPublishingGuideLayout.ShowRelayControls(GenesisGuiNextTab.Publishing));
            Assert.Equal(tab != GenesisGuiNextTab.Publishing,
                GenesisGuiNextPublishingGuideLayout.ShowRelayControls(tab));
        }
        // Relay previously occupies the guide and feedback rows: hiding it is
        // essential, even when all guide rectangles are otherwise disjoint.
        var layout = GenesisGuiNextPublishingGuideLayout.ForWidth(920);
        Assert.True(layout.Instructions.Top < GenesisGuiNextAppShellBridge.RelayControlsTop);
        Assert.True(layout.Instructions.Bottom > GenesisGuiNextAppShellBridge.RelayControlsTop);
    }

    private static GenesisGuiNextPublishingGuideRectangle[] Rectangles(GenesisGuiNextPublishingGuideLayout layout) =>
        [layout.Title, layout.Start, layout.Finish, layout.Interrupt, layout.State, layout.Settings,
            GenesisGuiNextPublishingGuideLayout.ServerLabel, layout.Server, layout.ServerCopy, GenesisGuiNextPublishingGuideLayout.KeyLabel,
            layout.Key, layout.Reveal, layout.KeyCopy, layout.Instructions, layout.Feedback];
}

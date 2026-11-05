namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextSettingsLayoutTests
{
    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void GeneralControlsFitMinimumInitialAndExpandedShellsWithoutOverlap(uint dpi)
    {
        foreach (var size in new[] { (GenesisMainWindowLayout.MinimumClientWidth, GenesisMainWindowLayout.MinimumClientHeight), (1120, 620), (1920, 1080) })
        {
            int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
            Assert.True(GenesisMainWindowLayout.TryResolve(S(size.Item1), S(size.Item2), dpi, out _, out _, out var list, out var details));
            var layout = new GenesisGuiNextSettingsLayout(list.Width, details.Bottom - list.Top);
            GenesisGuiNextAppShellPlacement.WindowRectangle[] boxes = [layout.Title, GenesisGuiNextSettingsLayout.General,
                layout.Legacy, layout.CardTitle, layout.Runtime, layout.Configured, layout.Notice,
                GenesisGuiNextSettingsLayout.Classic, GenesisGuiNextSettingsLayout.Modern, layout.Status, layout.Back, layout.Cancel, layout.Save];
            foreach (var b in boxes)
            {
                Assert.True(b.Width > 0 && b.Height > 0);
                Assert.InRange(S(b.Left), S(16), S(layout.Width - 16));
                Assert.True(S(b.Right) <= S(layout.Width - 16));
                Assert.InRange(S(b.Top), 0, S(layout.Height));
                Assert.True(S(b.Bottom) <= S(layout.Height));
            }
            for (var i = 0; i < boxes.Length; i++)
                for (var j = i + 1; j < boxes.Length; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    Assert.False(S(a.Left) < S(b.Right) && S(b.Left) < S(a.Right) && S(a.Top) < S(b.Bottom) && S(b.Top) < S(a.Bottom));
                }
            Assert.True(layout.Card.Top < layout.CardTitle.Top && layout.Status.Bottom < layout.Card.Bottom);
            Assert.True(layout.Card.Bottom < layout.Save.Top);
            Assert.True(layout.Notice.Height >= 40);
        }
    }
}

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
                GenesisGuiNextSettingsLayout.PlaybackTab, GenesisGuiNextSettingsLayout.RecordingTab, GenesisGuiNextSettingsLayout.NotificationsTab,
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

    [Theory]
    [InlineData(1, 96u)]
    [InlineData(2, 96u)]
    [InlineData(3, 96u)]
    [InlineData(1, 144u)]
    [InlineData(2, 144u)]
    [InlineData(3, 144u)]
    [InlineData(1, 192u)]
    [InlineData(2, 192u)]
    [InlineData(3, 192u)]
    public void AdditionalCategoriesFitWithoutScrollingOrControlOverlap(int category, uint dpi)
    {
        foreach (var size in new[] { (GenesisMainWindowLayout.MinimumClientWidth, GenesisMainWindowLayout.MinimumClientHeight), (1120, 620), (1920, 1080) })
        {
            int S(int value) => GenesisMainWindowLayout.Scale(value, dpi);
            Assert.True(GenesisMainWindowLayout.TryResolve(S(size.Item1), S(size.Item2), dpi, out _, out _, out var list, out var details));
            var layout = new GenesisGuiNextSettingsLayout(list.Width, details.Bottom - list.Top);
            var boxes = new List<GenesisGuiNextAppShellPlacement.WindowRectangle> { layout.Title, GenesisGuiNextSettingsLayout.General,
                GenesisGuiNextSettingsLayout.PlaybackTab, GenesisGuiNextSettingsLayout.RecordingTab, GenesisGuiNextSettingsLayout.NotificationsTab,
                layout.Legacy, layout.CardTitle, layout.PathNotice, layout.Back };
            if (category == (int)GenesisIntegratedSettingsCategory.Notifications)
            {
                boxes.AddRange(Enumerable.Range(0, 3).Select(layout.NotificationCheck));
                boxes.Add(layout.NotificationStatus); boxes.Add(layout.SoundTest);
                Assert.True(layout.NotificationStatus.Bottom < layout.Card.Bottom);
            }
            else
            {
                boxes.AddRange([layout.PathRuntime, layout.PathSaved, layout.PathInput(category == (int)GenesisIntegratedSettingsCategory.Playback),
                    layout.PathStatus, layout.Reset, layout.Save, layout.Cancel]);
                if (category == (int)GenesisIntegratedSettingsCategory.Playback) boxes.Add(layout.Browse);
                Assert.True(layout.PathStatus.Bottom < layout.Card.Bottom);
            }
            foreach (var b in boxes)
            {
                Assert.True(b.Width > 0 && b.Height > 0);
                Assert.True(S(b.Left) >= S(16) && S(b.Right) <= S(layout.Width - 16));
                Assert.True(S(b.Top) >= 0 && S(b.Bottom) <= S(layout.Height));
            }
            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    Assert.False(S(a.Left) < S(b.Right) && S(b.Left) < S(a.Right) && S(a.Top) < S(b.Bottom) && S(b.Top) < S(a.Bottom));
                }
        }
    }
}

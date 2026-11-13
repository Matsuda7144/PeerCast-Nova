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
                GenesisGuiNextSettingsLayout.ConnectionTab, GenesisGuiNextSettingsLayout.YellowPagesTab, layout.Legacy, layout.CardTitle, layout.Runtime, layout.Configured, layout.Notice,
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
                GenesisGuiNextSettingsLayout.ConnectionTab, GenesisGuiNextSettingsLayout.YellowPagesTab, layout.Legacy, layout.CardTitle, layout.PathNotice, layout.Back };
            if (category == (int)GenesisIntegratedSettingsCategory.Notifications)
            {
                boxes.AddRange(Enumerable.Range(0, 3).Select(layout.NotificationCheck));
                boxes.Add(layout.NotificationStatus); boxes.Add(layout.SoundTest);
                Assert.True(layout.NotificationStatus.Bottom < layout.Card.Bottom);
            }
            else
            {
                boxes.AddRange([layout.PathRuntime, layout.PathSaved, layout.PathInput(true),
                    layout.PathStatus, layout.Reset, layout.Save, layout.Cancel]);
                boxes.Add(layout.Browse); // Both playback executable and recording directory use Browse.
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

    [Theory]
    [InlineData(false, 96u)]
    [InlineData(true, 96u)]
    [InlineData(false, 144u)]
    [InlineData(true, 144u)]
    [InlineData(false, 192u)]
    [InlineData(true, 192u)]
    public void NetworkAndYpFieldsFitMinimumInitialAndExpandedShells(bool yp, uint dpi)
    {
        foreach (var size in new[] { (GenesisMainWindowLayout.MinimumClientWidth, GenesisMainWindowLayout.MinimumClientHeight), (1120, 620), (1920, 1080) })
        {
            int S(int n) => GenesisMainWindowLayout.Scale(n, dpi);
            Assert.True(GenesisMainWindowLayout.TryResolve(S(size.Item1), S(size.Item2), dpi, out _, out _, out var list, out var details));
            var layout = new GenesisGuiNextSettingsLayout(list.Width, details.Bottom - list.Top);
            var n = new GenesisGuiNextNetworkSettingsLayout(layout.Width);
            var boxes = new List<GenesisGuiNextAppShellPlacement.WindowRectangle> { layout.Title, GenesisGuiNextSettingsLayout.General,
                GenesisGuiNextSettingsLayout.PlaybackTab, GenesisGuiNextSettingsLayout.RecordingTab, GenesisGuiNextSettingsLayout.NotificationsTab,
                GenesisGuiNextSettingsLayout.ConnectionTab, GenesisGuiNextSettingsLayout.YellowPagesTab, layout.Legacy, layout.Back, layout.Save, layout.Cancel };
            var fields = new List<GenesisGuiNextAppShellPlacement.WindowRectangle>();
            if (yp)
            {
                fields.AddRange([layout.CardTitle, n.YpStatus, GenesisGuiNextNetworkSettingsLayout.YpList, GenesisGuiNextNetworkSettingsLayout.Enabled]);
                for (var j = 0; j < 5; j++) fields.AddRange([n.YpInput(j), GenesisGuiNextNetworkSettingsLayout.YpLabel(j)]);
                boxes.AddRange([GenesisGuiNextNetworkSettingsLayout.New(layout.Height), GenesisGuiNextNetworkSettingsLayout.Remove(layout.Height), GenesisGuiNextNetworkSettingsLayout.Toggle(layout.Height)]);
            }
            else
            {
                fields.AddRange([n.ConnectionTitle, n.Ipv6, n.Wan, n.Notice, n.Runtime, n.Saved, n.Status, n.AutoMapping]);
                var natStatus = n.NatStatus(layout);
                if (size.Item2 == GenesisMainWindowLayout.MinimumClientHeight)
                {
                    Assert.True(natStatus.Top > layout.Card.Bottom);
                    Assert.True(natStatus.Left > layout.Back.Right && natStatus.Right < layout.Cancel.Left);
                    boxes.Add(natStatus);
                }
                else fields.Add(natStatus);
                for (var j = 0; j < 6; j++) fields.AddRange([n.ConnectionInput(j), n.ConnectionLabel(j)]);
            }
            foreach (var b in fields) Assert.True(b.Top >= layout.Card.Top && b.Bottom < layout.Card.Bottom);
            boxes.AddRange(fields);
            foreach (var b in boxes)
            {
                Assert.True(b.Width > 0 && b.Height > 0);
                Assert.True(S(b.Left) >= S(16) && S(b.Right) <= S(layout.Width - 16));
                Assert.True(S(b.Top) >= 0 && S(b.Bottom) <= S(layout.Height));
            }
            for (var a = 0; a < boxes.Count; a++)
                for (var b = a + 1; b < boxes.Count; b++)
                    Assert.False(boxes[a].Left < boxes[b].Right && boxes[b].Left < boxes[a].Right && boxes[a].Top < boxes[b].Bottom && boxes[b].Top < boxes[a].Bottom);
        }
    }
}

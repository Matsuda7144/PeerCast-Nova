namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextWindowChromeTests
{
    [Fact]
    public void ModernUsesTheExistingPaletteAndDisposeRestoresEveryOriginalAttribute()
    {
        var values = new Dictionary<int, int> { [20] = 0, [35] = -1, [36] = -1 };
        using (var chrome = new GenesisGuiNextWindowChrome(() => false, a => values[a], (a, v) => { values[a] = v; return true; }))
        {
            Assert.Equal(1, values[20]);
            Assert.Equal(GenesisGuiNextDesign.Panel, values[35]);
            Assert.Equal(GenesisGuiNextDesign.Text, values[36]);
        }
        Assert.Equal(0, values[20]); Assert.Equal(-1, values[35]); Assert.Equal(-1, values[36]);
    }

    [Fact]
    public void AccessibilityChangesRestoreTheSystemAttributesAndCanReturnToModern()
    {
        bool? highContrast = true;
        var values = new Dictionary<int, int> { [20] = 0, [35] = -1, [36] = -1 };
        var writes = 0;
        using var chrome = new GenesisGuiNextWindowChrome(() => highContrast, a => values[a], (a, v) => { writes++; values[a] = v; return true; });
        Assert.Equal(0, writes);
        highContrast = false; chrome.Refresh(); Assert.Equal(1, values[20]);
        highContrast = true; chrome.Refresh(); Assert.Equal(0, values[20]); Assert.Equal(-1, values[35]);
        highContrast = false; chrome.Refresh(); Assert.Equal(GenesisGuiNextDesign.Panel, values[35]);
        highContrast = null; chrome.Refresh(); Assert.Equal(0, values[20]); Assert.Equal(-1, values[36]);
    }

    [Fact]
    public void UnreadableDarkModeLeavesTheWindowUntouched()
    {
        var writes = 0;
        using var chrome = new GenesisGuiNextWindowChrome(() => false, a => a == 20 ? null : -1, (_, _) => { writes++; return true; });
        chrome.Refresh(); Assert.Equal(0, writes);
    }

    [Fact]
    public void UnsetCaptionColorsUseDocumentedSystemDefaultsForRestoration()
    {
        var values = new Dictionary<int, int> { [20] = 0 };
        using (var chrome = new GenesisGuiNextWindowChrome(() => false, a => a == 20 ? 0 : null,
            (a, v) => { values[a] = v; return true; }))
            Assert.Equal(GenesisGuiNextDesign.Panel, values[35]);
        Assert.Equal(0, values[20]); Assert.Equal(-1, values[35]); Assert.Equal(-1, values[36]);
    }

    [Fact]
    public void FailedOptionalColorFallsBackWithoutKeepingAPartialPalette()
    {
        var values = new Dictionary<int, int> { [20] = 0, [35] = -1, [36] = -1 };
        using var chrome = new GenesisGuiNextWindowChrome(() => false, a => values[a], (a, v) =>
        {
            if (a == 36 && v == GenesisGuiNextDesign.Text) return false;
            values[a] = v; return true;
        });
        Assert.Equal(0, values[20]); Assert.Equal(-1, values[35]); Assert.Equal(-1, values[36]);
    }

    [Fact]
    public void ClassicAndMissingWindowsNeverCreateChrome()
    {
        Assert.Null(GenesisGuiNextWindowChrome.TryCreate((nint)123, false));
        Assert.Null(GenesisGuiNextWindowChrome.TryCreate(0, true));
    }
}

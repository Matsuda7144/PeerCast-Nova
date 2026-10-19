namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRecordingStatusDrawTests
{
    [Theory]
    [InlineData(4u, true, false)]
    [InlineData(5u, false, true)]
    [InlineData(2u, false, false)]
    public void OwnerDrawTypesMatchWin32Contract(uint type, bool button, bool staticControl)
    {
        Assert.Equal(button, GenesisGuiNextAppShellBridge.IsOwnerDrawButton(type));
        Assert.Equal(staticControl, GenesisGuiNextAppShellBridge.IsOwnerDrawStatic(type));
    }

    [Theory]
    [InlineData(5u, 1117u, true)]
    [InlineData(2u, 1117u, false)]
    [InlineData(4u, 1117u, false)]
    [InlineData(5u, 1116u, false)]
    [InlineData(5u, 0u, false)]
    public void OnlyRecordingStatusStaticRoutesToStatusDraw(uint type, uint id, bool expected)
    {
        Assert.Equal(expected, GenesisGuiNextAppShellBridge.IsRecordingStatusDraw(type, id));
    }

    [Theory]
    [InlineData(0, true, 132, 14)]
    [InlineData(1, true, 114, 32)]
    [InlineData(2, false, 132, 14)]
    public void SharedStatusVisibilityAndBoundsRemainModeSpecific(
        int modeValue, bool visible, int top, int height)
    {
        var mode = (GenesisGuiNextRecordingPageMode)modeValue;
        Assert.Equal(visible, GenesisGuiNextAppShellBridge.IsRecordingStatusVisible(
            mode == GenesisGuiNextRecordingPageMode.Rules));
        var layout = GenesisGuiNextAppShellBridge.RecordingStatusLayout(
            recordingPage: true, libraryPage: mode == GenesisGuiNextRecordingPageMode.Library);
        Assert.Equal((top, height), layout);
        Assert.InRange(layout.Top, 0, GenesisGuiNextAppShellPlacement.DetailsHeight);
        Assert.InRange(layout.Top + layout.Height, 1, GenesisGuiNextAppShellPlacement.DetailsHeight);
        Assert.True(GenesisGuiNextAppShellBridge.IsRecordingStatusDraw(5, 1117));
    }

    [Fact]
    public void NonRecordingStatusBoundsRemainUnchanged()
    {
        Assert.Equal((122, 18), GenesisGuiNextAppShellBridge.RecordingStatusLayout(false, false));
        Assert.True(GenesisGuiNextAppShellBridge.IsRecordingStatusVisible(false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DrawingKeepsLibraryAndActiveStatusSourcesSeparate(bool library)
    {
        const string libraryText = "録画ファイルを 17 件表示しています";
        const string activeText = "自動録画を抑止中";
        Assert.Equal(library ? libraryText : activeText,
            GenesisGuiNextAppShellBridge.RecordingStatusText(library, libraryText, activeText));
    }
}

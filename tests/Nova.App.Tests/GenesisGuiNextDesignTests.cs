namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextDesignTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionRemainsDistinctEvenWhenPointerLeavesOrEnters(bool hot)
    {
        Assert.Equal(GenesisGuiNextDesign.Selected, GenesisGuiNextDesign.RowBackground(true, hot));
        Assert.Equal(GenesisGuiNextDesign.Selected, GenesisGuiNextDesign.NavigationBackground(true, hot));
        Assert.NotEqual(GenesisGuiNextDesign.Selected, GenesisGuiNextDesign.RowBackground(false, hot));
        Assert.NotEqual(GenesisGuiNextDesign.Selected, GenesisGuiNextDesign.NavigationBackground(false, hot));
    }

    [Theory]
    [InlineData(GenesisGuiNextDesign.Text, GenesisGuiNextDesign.List)]
    [InlineData(GenesisGuiNextDesign.Text, GenesisGuiNextDesign.Selected)]
    [InlineData(GenesisGuiNextDesign.Text, GenesisGuiNextDesign.Hover)]
    [InlineData(GenesisGuiNextDesign.SecondaryText, GenesisGuiNextDesign.Header)]
    [InlineData(GenesisGuiNextDesign.SecondaryText, GenesisGuiNextDesign.Panel)]
    public void MainTextAndSecondaryLabelsRemainReadable(int foreground, int background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);
        Assert.True((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05) >= 4.5);
    }

    private static double Luminance(int color)
    {
        static double Component(int value)
        {
            var fraction = value / 255.0;
            return fraction <= 0.04045 ? fraction / 12.92 : Math.Pow((fraction + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Component(color & 255) + 0.7152 * Component(color >> 8 & 255) + 0.0722 * Component(color >> 16 & 255);
    }
}

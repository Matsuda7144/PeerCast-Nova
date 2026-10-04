namespace PeerCast.Nova.App.Tests;

public sealed class NatPortMappingSettingTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("off", false)]
    [InlineData("OFF", false)]
    [InlineData("on", true)]
    [InlineData(" ON ", true)]
    public void Setting_ExplicitOnOffAndDefaultOff(string? value, bool expected)
    {
        Assert.Equal(expected, SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(value).Enabled);
    }

    [Fact]
    public void Setting_RejectsInvalidValue()
    {
        Assert.Throws<ArgumentException>(() => SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting("yes"));
    }
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisConnectionSettingsTextTests
{
    [Fact]
    public void SummaryKeepsZeroUnlimitedAndAllMaximumValues()
    {
        var value = new GenesisGuiNextConnectionPreferences
        {
            WanRelayEnabled = true,
            ListenPort = 65535,
            TotalUpstreamIpv4Kbps = 0,
            TotalUpstreamIpv6Kbps = null,
            MaxRelaysTotal = int.MaxValue,
            MaxRelaysPerBroadcastChannel = 0,
            MaxRelaysPerViewingChannel = null,
        };
        var text = GenesisConnectionSettingsText.Summary(value);
        Assert.Equal(3, text.Split("\r\n").Length);
        Assert.Contains("WAN ON / Port 65535 / IPv6 OFF", text);
        Assert.Contains("IPv4 0 / IPv6 無制限", text);
        Assert.Contains("合計 2147483647 / 配信毎 0 / 視聴毎 無制限", text);
        var maximum = GenesisConnectionSettingsText.Summary(value with
        {
            TotalUpstreamIpv4Kbps = int.MaxValue,
            TotalUpstreamIpv6Kbps = int.MaxValue,
            MaxRelaysPerBroadcastChannel = int.MaxValue,
            MaxRelaysPerViewingChannel = int.MaxValue,
        });
        Assert.Equal(5, maximum.Split("2147483647").Length - 1);
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(300, 180)]
    [InlineData(300, 600)]
    public void ScrollingCanRevealEveryFieldAndFinalStatus(int scroll, int page)
    {
        const int content = 460;
        Assert.Equal(Math.Max(0, content - page), GenesisConnectionBodyLayout.Clamp(int.MaxValue, page, content));
        foreach (var top in new[] { 4, 110, 220, 320, 430 })
        {
            var revealed = GenesisConnectionBodyLayout.Reveal(scroll, page, top, top + 26, content);
            Assert.InRange(top - revealed, 0, page - 26);
        }
    }
}

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisSystemTrayPolicyTests
{
    [Fact]
    public void CloseExitsByDefaultAndMinimizeHidesToTrayUntilExplicitExit()
    {
        var policy = new GenesisSystemTrayPolicy();

        Assert.False(policy.CloseToTrayEnabled);
        Assert.False(policy.ShouldHideOnClose());
        Assert.True(policy.MinimizeToTrayEnabled);
        Assert.False(policy.IsExitRequested);
        Assert.True(policy.ShouldHideOnMinimize());

        Assert.True(policy.TryConsumeFirstHideNotice());
        Assert.False(policy.TryConsumeFirstHideNotice());
        Assert.True(policy.HasShownFirstHideNotice);

        policy.AllowExit();
        Assert.True(policy.IsExitRequested);
        Assert.False(policy.ShouldHideOnClose());
        Assert.False(policy.ShouldHideOnMinimize());
    }

    [Fact]
    public void CloseToTrayOptInAndToggleLabelsWorkAsExpected()
    {
        var closeToTray = new GenesisSystemTrayPolicy { CloseToTrayEnabled = true, MinimizeToTrayEnabled = false };
        Assert.True(closeToTray.ShouldHideOnClose());
        Assert.False(closeToTray.ShouldHideOnMinimize());

        Assert.Equal("トレイに格納", GenesisSystemTrayPolicy.ResolveToggleVisibilityLabel(isWindowVisible: true));
        Assert.Equal("PeerCast Nova を表示", GenesisSystemTrayPolicy.ResolveToggleVisibilityLabel(isWindowVisible: false));
    }

    [Fact]
    public void MinimizeMessageHelpersMatchSystemCommandAndMinimizedSizeOnly()
    {
        Assert.True(GenesisSystemTrayPolicy.IsMinimizeCommand(0x0112, 0xF020));
        Assert.True(GenesisSystemTrayPolicy.IsMinimizeCommand(0x0112, 0xF020 | 0x0001));
        Assert.False(GenesisSystemTrayPolicy.IsMinimizeCommand(0x0112, 0xF030));
        Assert.False(GenesisSystemTrayPolicy.IsMinimizeCommand(0x0112, 0xF060));
        Assert.False(GenesisSystemTrayPolicy.IsMinimizeCommand(0x0005, 0xF020));

        Assert.True(GenesisSystemTrayPolicy.IsMinimizedSize(0x0005, 1));
        Assert.False(GenesisSystemTrayPolicy.IsMinimizedSize(0x0005, 0));
        Assert.False(GenesisSystemTrayPolicy.IsMinimizedSize(0x0005, 2));
        Assert.False(GenesisSystemTrayPolicy.IsMinimizedSize(0x0112, 1));
    }
}

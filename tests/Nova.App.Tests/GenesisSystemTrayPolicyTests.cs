namespace PeerCast.Nova.App.Tests;

public sealed class GenesisSystemTrayPolicyTests
{
    [Fact]
    public void CloseHidesToTrayUntilExplicitExitAndShowsNoticeOnce()
    {
        var policy = new GenesisSystemTrayPolicy();

        Assert.True(policy.CloseToTrayEnabled);
        Assert.False(policy.IsExitRequested);
        Assert.True(policy.ShouldHideOnClose());

        Assert.True(policy.TryConsumeFirstHideNotice());
        Assert.False(policy.TryConsumeFirstHideNotice());
        Assert.True(policy.HasShownFirstHideNotice);

        policy.AllowExit();
        Assert.True(policy.IsExitRequested);
        Assert.False(policy.ShouldHideOnClose());
    }

    [Fact]
    public void DisablingCloseToTrayAndToggleLabelsWorkAsExpected()
    {
        var policy = new GenesisSystemTrayPolicy { CloseToTrayEnabled = false };
        Assert.False(policy.ShouldHideOnClose());

        Assert.Equal("トレイに格納", GenesisSystemTrayPolicy.ResolveToggleVisibilityLabel(isWindowVisible: true));
        Assert.Equal("PeerCast Nova を表示", GenesisSystemTrayPolicy.ResolveToggleVisibilityLabel(isWindowVisible: false));
    }
}

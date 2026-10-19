using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisManualRecordingCommandLanesTests
{
    [Fact]
    public void SuppressesDuplicateCommandsPerChannelButAllowsOtherChannels()
    {
        var lanes = new GenesisManualRecordingCommandLanes();
        var first = new ChannelId(Guid.NewGuid());
        var second = new ChannelId(Guid.NewGuid());

        Assert.True(lanes.TryEnter(first));
        Assert.False(lanes.TryEnter(first));
        Assert.True(lanes.TryEnter(second));
        Assert.True(lanes.IsActive(first));
        Assert.True(lanes.IsActive(second));

        lanes.Exit(first);
        Assert.False(lanes.IsActive(first));
        Assert.True(lanes.IsActive(second));
        lanes.Exit(second);
    }
}

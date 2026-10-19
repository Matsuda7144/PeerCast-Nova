namespace PeerCast.Nova.App.Tests;

public sealed class GenesisInteractionCommandLanesTests
{
    [Fact]
    public void RefreshCommandDoesNotBlockPlaybackLane()
    {
        var lanes = new GenesisInteractionCommandLanes();
        var refresh = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lanes.TrackRefresh(refresh.Task);

        Assert.False(lanes.CanStartRefresh);
        Assert.True(lanes.CanStartPlayback);

        refresh.SetResult();

        Assert.True(lanes.CanStartRefresh);
        Assert.True(lanes.CanStartPlayback);
    }

    [Fact]
    public void PlaybackCommandDoesNotBlockRefreshLane()
    {
        var lanes = new GenesisInteractionCommandLanes();
        var playback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lanes.TrackPlayback(playback.Task);

        Assert.False(lanes.CanStartPlayback);
        Assert.True(lanes.CanStartRefresh);

        playback.SetResult();

        Assert.True(lanes.CanStartPlayback);
        Assert.True(lanes.CanStartRefresh);
    }

    [Fact]
    public void StopCanRunBesidePendingPlaybackAndSuppressesDuplicateStop()
    {
        var lanes = new GenesisInteractionCommandLanes();
        var playback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lanes.TrackPlayback(playback.Task);

        Assert.False(lanes.CanStartPlayback);
        Assert.True(lanes.CanStartStop);

        lanes.TrackStop(stop.Task);

        Assert.False(lanes.CanStartPlayback);
        Assert.False(lanes.CanStartStop);

        stop.SetResult();

        Assert.False(lanes.CanStartPlayback);
        Assert.True(lanes.CanStartStop);

        playback.SetResult();

        Assert.True(lanes.CanStartPlayback);
        Assert.True(lanes.CanStartStop);
    }
}

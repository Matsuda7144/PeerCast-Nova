using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class SystemPlaybackControlledWindowsPhysicalRunOperationsTests
{
    [Theory]
    [InlineData(
        PlaybackControlledWindowsOutputValidationKind.Video,
        PlaybackSchedulerInitialOutputKind.Video)]
    [InlineData(
        PlaybackControlledWindowsOutputValidationKind.Audio,
        PlaybackSchedulerInitialOutputKind.Audio)]
    public void CreateLifecycleOptions_MapsControlledKindAndPreservesBoundedDropOldest(
        PlaybackControlledWindowsOutputValidationKind kind,
        PlaybackSchedulerInitialOutputKind expectedInitialOutputKind)
    {
        var options = SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
            TimeSpan.FromMilliseconds(100),
            kind);

        Assert.Equal(
            PlaybackSchedulerOptions.DefaultCapacity,
            options.Generation.Scheduler.Capacity);
        Assert.Equal(
            PlaybackSchedulerOptions.DefaultMaximumOwnedBytes,
            options.Generation.Scheduler.MaximumOwnedBytes);
        Assert.Equal(
            PlaybackSchedulerFullPolicy.DropOldest,
            options.Generation.Scheduler.FullPolicy);
        Assert.Equal(expectedInitialOutputKind, options.Generation.Scheduler.InitialOutputKind);
    }

    [Fact]
    public void ProductionSchedulerDefaultsRemainVideoAndDropIncoming()
    {
        var productionDefault = new PlaybackSchedulerOptions();
        Assert.Equal(
            PlaybackSchedulerInitialOutputKind.Video,
            productionDefault.InitialOutputKind);
        Assert.Equal(PlaybackSchedulerFullPolicy.DropIncoming, productionDefault.FullPolicy);
    }
}

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsTextureCopyProbeCommandTests
{
    [Fact]
    public void MissingHoldUsesThreeSecondDefault()
    {
        Assert.True(PlaybackControlledWindowsTextureCopyProbeCommand.TryParse(
            [PlaybackControlledWindowsTextureCopyProbeCommand.Argument],
            out var hold));
        Assert.Equal(TimeSpan.FromSeconds(3), hold);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3000", 3000)]
    [InlineData("30000", 30000)]
    public void BoundedHoldIsAccepted(string text, int expectedMilliseconds)
    {
        Assert.True(PlaybackControlledWindowsTextureCopyProbeCommand.TryParse(
            [
                PlaybackControlledWindowsTextureCopyProbeCommand.Argument,
                "--observation-hold-ms",
                text,
            ],
            out var hold));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), hold);
    }

    [Fact]
    public void MissingHoldValueFailsClosed()
    {
        Assert.False(PlaybackControlledWindowsTextureCopyProbeCommand.TryParse(
            [
                PlaybackControlledWindowsTextureCopyProbeCommand.Argument,
                "--observation-hold-ms",
            ],
            out _));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("30001")]
    [InlineData("not-a-number")]
    public void InvalidHoldFailsClosed(string text)
    {
        Assert.False(PlaybackControlledWindowsTextureCopyProbeCommand.TryParse(
            [
                PlaybackControlledWindowsTextureCopyProbeCommand.Argument,
                "--observation-hold-ms",
                text,
            ],
            out _));
    }
}

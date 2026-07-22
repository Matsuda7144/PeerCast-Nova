namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsKnownPatternProbeCommandTests
{
    [Fact]
    public void Missing_hold_option_uses_explicit_three_second_default()
    {
        Assert.True(PlaybackControlledWindowsKnownPatternProbeCommand.TryParse(
            [PlaybackControlledWindowsKnownPatternProbeCommand.Argument],
            out var options));

        Assert.Equal(TimeSpan.FromSeconds(3), options.ObservationHoldDuration);
    }

    [Fact]
    public void Hold_option_without_value_fails_closed()
    {
        Assert.False(PlaybackControlledWindowsKnownPatternProbeCommand.TryParse(
            [
                PlaybackControlledWindowsKnownPatternProbeCommand.Argument,
                "--observation-hold-ms",
            ],
            out _));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3000", 3000)]
    [InlineData("30000", 30000)]
    public void Bounded_hold_is_accepted(string text, int expectedMilliseconds)
    {
        Assert.True(PlaybackControlledWindowsKnownPatternProbeCommand.TryParse(
            [
                PlaybackControlledWindowsKnownPatternProbeCommand.Argument,
                "--observation-hold-ms",
                text,
            ],
            out var options));

        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            options.ObservationHoldDuration);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("30001")]
    [InlineData("not-a-number")]
    public void Invalid_hold_fails_closed(string text)
    {
        Assert.False(PlaybackControlledWindowsKnownPatternProbeCommand.TryParse(
            [
                PlaybackControlledWindowsKnownPatternProbeCommand.Argument,
                "--observation-hold-ms",
                text,
            ],
            out _));
    }
}

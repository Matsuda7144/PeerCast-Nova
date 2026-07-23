namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsKnownAudioToneProbeCommandTests
{
    [Fact]
    public void Missing_hold_option_uses_explicit_one_second_default()
    {
        Assert.True(PlaybackControlledWindowsKnownAudioToneProbeCommand.TryParse(
            [PlaybackControlledWindowsKnownAudioToneProbeCommand.Argument],
            out var options));

        Assert.Equal(TimeSpan.FromSeconds(1), options.ObservationHoldDuration);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1000", 1000)]
    [InlineData("30000", 30000)]
    public void Bounded_hold_is_accepted(string text, int expectedMilliseconds)
    {
        Assert.True(PlaybackControlledWindowsKnownAudioToneProbeCommand.TryParse(
            [
                PlaybackControlledWindowsKnownAudioToneProbeCommand.Argument,
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
        Assert.False(PlaybackControlledWindowsKnownAudioToneProbeCommand.TryParse(
            [
                PlaybackControlledWindowsKnownAudioToneProbeCommand.Argument,
                "--observation-hold-ms",
                text,
            ],
            out _));
    }

    [Fact]
    public void Runner_dispatches_probe_before_default_physical_run()
    {
        var source = File.ReadAllText(GetRunnerProgramPath());
        var probe = source.IndexOf(
            "PlaybackControlledWindowsKnownAudioToneProbeCommand.Argument",
            StringComparison.Ordinal);
        var physical = source.IndexOf(
            "PlaybackControlledWindowsPhysicalRunCommand.RunAsync",
            StringComparison.Ordinal);

        Assert.True(probe >= 0);
        Assert.True(physical > probe);
    }

    private static string GetRunnerProgramPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Nova.ControlledWindowsRunner",
                "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The controlled runner program was not found.");
    }
}

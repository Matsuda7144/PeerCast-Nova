namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsPhysicalRunCommandTests
{
    [Fact]
    public async Task Missing_required_arguments_fail_before_composition_start()
    {
        using var output = new StringWriter();

        var result = await PlaybackControlledWindowsPhysicalRunCommand.RunAsync(
            [PlaybackControlledWindowsPhysicalRunCommand.Argument],
            output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration,
            result.Result);
        Assert.Contains("Result: InvalidConfiguration", output.ToString());
    }

    [Theory]
    [InlineData("combined", "128")]
    [InlineData("video", "0")]
    [InlineData("audio", "4097")]
    public async Task Invalid_kind_or_decode_limit_fail_closed(
        string kind,
        string maximumDecodeOperations)
    {
        using var output = new StringWriter();
        string[] arguments =
        [
            PlaybackControlledWindowsPhysicalRunCommand.Argument,
            "--runtime-directory", "C:\\controlled-runtime",
            "--controlled-fixture", "C:\\controlled.flv",
            "--fixture-sha256", new string('A', 64),
            "--output-kind", kind,
            "--max-decode-operations", maximumDecodeOperations,
        ];

        var result = await PlaybackControlledWindowsPhysicalRunCommand.RunAsync(
            arguments,
            output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration,
            result.Result);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("30001")]
    [InlineData("not-a-number")]
    public async Task Invalid_observation_hold_fails_before_composition_start(string hold)
    {
        using var output = new StringWriter();
        string[] arguments =
        [
            PlaybackControlledWindowsPhysicalRunCommand.Argument,
            "--runtime-directory", "C:\\controlled-runtime",
            "--controlled-fixture", "C:\\controlled.flv",
            "--fixture-sha256", new string('A', 64),
            "--output-kind", "video",
            "--observation-hold-ms", hold,
        ];

        var result = await PlaybackControlledWindowsPhysicalRunCommand.RunAsync(
            arguments,
            output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration,
            result.Result);
    }
}

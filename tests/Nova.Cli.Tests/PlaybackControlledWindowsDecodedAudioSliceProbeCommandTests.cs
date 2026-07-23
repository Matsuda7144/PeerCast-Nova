namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsDecodedAudioSliceProbeCommandTests
{
    private static readonly string[] Required =
    [
        PlaybackControlledWindowsDecodedAudioSliceProbeCommand.Argument,
        "--runtime-directory",
        "runtime",
        "--controlled-fixture",
        "fixture",
    ];

    [Fact]
    public void Required_inputs_use_explicit_bounded_defaults()
    {
        Assert.True(
            PlaybackControlledWindowsDecodedAudioSliceProbeCommand.TryParse(
                Required,
                out var options));

        Assert.Equal(128, options.MaximumDecodeOperations);
        Assert.Equal(65_536, options.FixturePacketBytes);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ObservationHoldDuration);
    }

    [Theory]
    [InlineData("--max-decode-operations", "0")]
    [InlineData("--max-decode-operations", "4097")]
    [InlineData("--fixture-packet-bytes", "0")]
    [InlineData("--fixture-packet-bytes", "66940")]
    [InlineData("--observation-hold-ms", "-1")]
    [InlineData("--observation-hold-ms", "30001")]
    [InlineData("--observation-hold-ms", "invalid")]
    public void Invalid_bound_fails_closed(string name, string value)
    {
        Assert.False(
            PlaybackControlledWindowsDecodedAudioSliceProbeCommand.TryParse(
                [.. Required, name, value],
                out _));
    }

    [Fact]
    public void Missing_runtime_or_fixture_fails_closed()
    {
        Assert.False(
            PlaybackControlledWindowsDecodedAudioSliceProbeCommand.TryParse(
                [PlaybackControlledWindowsDecodedAudioSliceProbeCommand.Argument],
                out _));
    }

    [Fact]
    public void Runner_dispatches_slice_probe_before_default_physical_run()
    {
        var source = File.ReadAllText(GetRunnerProgramPath());
        var probe = source.IndexOf(
            "PlaybackControlledWindowsDecodedAudioSliceProbeCommand.Argument",
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

namespace PeerCast.Nova.App.Tests;

public sealed class NovaApplicationEntrypointTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpReturnsWithoutStartingApplication(string argument)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var launches = 0;

        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            [argument],
            output,
            error,
            (_, _) =>
            {
                launches++;
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, launches);
        Assert.StartsWith("Usage: Nova.App [--help | --about]", output.ToString());
        Assert.Contains("--pcrplayer-executable <path>", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--libmpv-library <path>", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--play-fmp4 <loopback-url>", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task AboutProjectsExactEmbeddedMaterialWithoutStartingApplication()
    {
        var expected = Assert.IsType<NovaAboutLegalModel>(NovaAboutLegalMaterial.Load().Model);
        var output = new StringWriter();
        var error = new StringWriter();
        var launches = 0;

        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            ["--about"],
            output,
            error,
            (_, _) =>
            {
                launches++;
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, launches);
        Assert.Empty(error.ToString());
        Assert.Contains(expected.ProductName, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected.ProjectLicense, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected.Notice, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected.Provenance, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected.RuntimeReplacement, output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--help", "--about")]
    [InlineData("--pcrplayer-executable")]
    [InlineData("--pcrplayer-executable", "first.exe", "--pcrplayer-executable", "second.exe")]
    [InlineData("--libmpv-library")]
    [InlineData("--libmpv-library", "libmpv-2.dll")]
    [InlineData("--libmpv-library", "libmpv-2.dll", "--pcrplayer-executable", "player.exe")]
    public async Task InvalidArgumentsFailBeforeStartingApplication(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var launches = 0;

        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            args,
            output,
            error,
            (_, _) =>
            {
                launches++;
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, launches);
        Assert.Empty(output.ToString());
        Assert.Equal(
            $"Usage: Nova.App [--help | --about] [--pcrplayer-executable <path> | --libmpv-library <path>]{Environment.NewLine}",
            error.ToString());
    }

    [Fact]
    public async Task PlayerOptionPassesTheExecutablePathToGuiStartup()
    {
        const string path = @"C:\Player Tools\PCRBrowser.exe";
        string? received = null;
        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            ["--pcrplayer-executable", path],
            TextWriter.Null,
            TextWriter.Null,
            (playback, _) =>
            {
                received = playback.PlayerExecutablePath;
                Assert.Null(playback.LibMpvLibraryPath);
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(path, received);
    }

    [Fact]
    public async Task LibMpvOptionSelectsEmbeddedPlayback()
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), "Player Tools", "libmpv-2.dll");
        NovaPlaybackLaunchOptions? options = null;
        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            ["--libmpv-library", libraryPath],
            TextWriter.Null,
            TextWriter.Null,
            (playback, _) =>
            {
                options = playback;
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(new NovaPlaybackLaunchOptions(null, libraryPath), options);
    }

    [Theory]
    [InlineData("http://127.0.0.1:45913/stream/531dc8dfc7fb42928ac2c0a626517a87")]
    [InlineData("https://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87")]
    [InlineData("http://example.test:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87")]
    [InlineData("http://127.0.0.1:0/fmp4/531dc8dfc7fb42928ac2c0a626517a87")]
    public async Task Fmp4OptionRejectsAnInvalidEndpointBeforeStartup(string url)
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), "libmpv-2.dll");
        var error = new StringWriter();
        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            ["--play-fmp4", url, "--libmpv-library", libraryPath],
            TextWriter.Null,
            error,
            (_, _) => throw new InvalidOperationException("Neither playback nor the regular app may start."),
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Contains("--play-fmp4 <loopback-url>", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--play-fmp4")]
    [InlineData("--play-fmp4", "http://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87")]
    [InlineData("--play-fmp4", "http://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87", "--libmpv-library", "libmpv-2.dll")]
    [InlineData("--play-fmp4", "http://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87", "--pcrplayer-executable", "player.exe")]
    public async Task Fmp4OptionRequiresAnExplicitLibMpvLibrary(params string[] args)
    {
        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            args,
            TextWriter.Null,
            TextWriter.Null,
            (_, _) => throw new InvalidOperationException("Neither playback nor the regular app may start."),
            CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData(NovaWindowsStartupResult.Completed, 0)]
    [InlineData(NovaWindowsStartupResult.Cancelled, 0)]
    [InlineData(NovaWindowsStartupResult.PlatformUnsupported, 2)]
    [InlineData(NovaWindowsStartupResult.StartupRejected, 1)]
    [InlineData(NovaWindowsStartupResult.Faulted, 1)]
    public async Task NoArgumentsRunsApplicationAndMapsExitCode(
        NovaWindowsStartupResult result,
        int expectedExitCode)
    {
        var launches = 0;

        var exitCode = await NovaApplicationEntrypoint.RunAsync(
            [],
            TextWriter.Null,
            TextWriter.Null,
            (playback, _) =>
            {
                Assert.Equal(new NovaPlaybackLaunchOptions(null, null), playback);
                launches++;
                return ValueTask.FromResult(result);
            },
            CancellationToken.None);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(1, launches);
    }
}

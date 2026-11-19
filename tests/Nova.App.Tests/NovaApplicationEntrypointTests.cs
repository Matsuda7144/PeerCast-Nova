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
            $"Usage: Nova.App [--help | --about] [--pcrplayer-executable <path>]{Environment.NewLine}",
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
            (playerPath, _) =>
            {
                received = playerPath;
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(path, received);
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
            (playerPath, _) =>
            {
                Assert.Null(playerPath);
                launches++;
                return ValueTask.FromResult(result);
            },
            CancellationToken.None);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(1, launches);
    }
}

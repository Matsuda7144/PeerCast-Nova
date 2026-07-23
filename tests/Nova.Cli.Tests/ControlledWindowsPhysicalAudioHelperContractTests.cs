namespace PeerCast.Nova.Cli.Tests;

public sealed class ControlledWindowsPhysicalAudioHelperContractTests
{
    [Fact]
    public void HelperUsesOneManagedCaptureCommandWithoutLoadingNet10Assemblies()
    {
        var source = File.ReadAllText(GetHelperPath());

        Assert.DoesNotContain("Assembly]::LoadFrom", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentList", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PlaybackControlledWindowsAudioProcessFailureDiagnostics",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("pwsh -", source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            1,
            Count(source, "--capture-controlled-windows-audio-child"));
    }

    [Fact]
    public void AllExternalPathAndVideoGatesPrecedeTheOnlyCaptureCommand()
    {
        var source = File.ReadAllText(GetHelperPath());
        var capture = source.IndexOf(
            "--capture-controlled-windows-audio-child",
            StringComparison.Ordinal);

        Assert.True(capture > source.IndexOf(
            "the requested failure record already exists",
            StringComparison.Ordinal));
        Assert.True(capture > source.IndexOf(
            "failure, video, and audio record paths must be different",
            StringComparison.Ordinal));
        Assert.True(capture > source.IndexOf(
            "the failure record must remain repository-external",
            StringComparison.Ordinal));
        Assert.True(capture > source.IndexOf(
            "the video record gate was not accepted",
            StringComparison.Ordinal));
    }

    [Fact]
    public void RunbookKeepsWindowsPowerShellBypassHostWithoutPwshRequirement()
    {
        var source = File.ReadAllText(GetRunbookPath());

        Assert.Contains(
            "powershell.exe -NoProfile -ExecutionPolicy Bypass",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("pwsh -", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DedicatedRunnerManagedCatchEmitsOnlyTheSanitizedMarker()
    {
        var source = File.ReadAllText(GetRunnerProgramPath());
        Assert.Contains("catch (Exception)", source);
        Assert.Contains(
            "\"PCN-CONTROLLED-RUNNER-FAILURE: ManagedUnhandledException\"",
            source);
        Assert.DoesNotContain("exception.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.StackTrace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InnerException", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedRunnerDispatchesCaptureBeforePhysicalRunCommand()
    {
        var source = File.ReadAllText(GetRunnerProgramPath());
        var capture = source.IndexOf(
            "PlaybackControlledWindowsAudioChildCaptureCommand.Argument",
            StringComparison.Ordinal);
        var physical = source.IndexOf(
            "PlaybackControlledWindowsPhysicalRunCommand.RunAsync",
            StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(physical > capture);
    }

    private static string GetHelperPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Run-ControlledWindowsPhysicalAudio.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The controlled audio helper was not found.");
    }

    private static string GetRunnerProgramPath()
    {
        var helper = new FileInfo(GetHelperPath());
        var root = helper.Directory?.Parent ??
            throw new DirectoryNotFoundException("The repository root was not found.");
        return Path.Combine(root.FullName, "src", "Nova.ControlledWindowsRunner", "Program.cs");
    }

    private static string GetRunbookPath()
    {
        var helper = new FileInfo(GetHelperPath());
        var root = helper.Directory?.Parent ??
            throw new DirectoryNotFoundException("The repository root was not found.");
        return Path.Combine(
            root.FullName,
            "docs",
            "runbooks",
            "0.0.10d-controlled-windows-audio-validation.md");
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}

namespace PeerCast.Nova.Cli.Tests;

public sealed class ControlledWindowsPhysicalAudioHelperContractTests
{
    [Fact]
    public void HelperUsesOneRedirectedVisibleChildAndPromotesOnlyCanonicalOutput()
    {
        var source = File.ReadAllText(GetHelperPath());

        Assert.Contains("[System.Diagnostics.ProcessStartInfo]::new()", source);
        Assert.Contains("$startInfo.UseShellExecute = $false", source);
        Assert.Contains("$startInfo.RedirectStandardOutput = $true", source);
        Assert.Contains("$startInfo.RedirectStandardError = $true", source);
        Assert.Contains("$startInfo.CreateNoWindow = $false", source);
        Assert.Contains(
            "$physicalProcess.StandardOutput.BaseStream.CopyToAsync($standardOutputBytes)",
            source);
        Assert.Contains(
            "$physicalProcess.StandardError.BaseStream.CopyToAsync($standardErrorBytes)",
            source);
        Assert.Equal(1, Count(source, "$physicalProcess.Start()"));
        Assert.Contains("--diagnose-audio-native-stages", source);
        Assert.DoesNotMatch(
            @"(?s)& dotnet \$runnerAssembly\s+`\s*--controlled-windows-physical-run",
            source);

        var canonical = source.IndexOf("$canonicalRecord =", StringComparison.Ordinal);
        var audioPromotion = source.IndexOf(
            "Move-Item -LiteralPath $temporaryAudioRecord",
            StringComparison.Ordinal);
        var failurePromotion = source.IndexOf(
            "Move-Item -LiteralPath $temporaryFailureRecord",
            StringComparison.Ordinal);
        Assert.True(canonical >= 0);
        Assert.True(audioPromotion > canonical);
        Assert.True(failurePromotion > canonical);
        var failureStop = source.IndexOf(
            "a sanitized external failure record was retained",
            StringComparison.Ordinal);
        Assert.True(failureStop > failurePromotion);
        Assert.True(audioPromotion > failureStop);
    }

    [Fact]
    public void AllExternalPathAndVideoGatesPrecedeTheOnlyPhysicalChildStart()
    {
        var source = File.ReadAllText(GetHelperPath());
        var childStart = source.IndexOf("$physicalProcess.Start()", StringComparison.Ordinal);

        Assert.True(childStart > source.IndexOf(
            "the requested failure record already exists",
            StringComparison.Ordinal));
        Assert.True(childStart > source.IndexOf(
            "failure, video, and audio record paths must be different",
            StringComparison.Ordinal));
        Assert.True(childStart > source.IndexOf(
            "the failure record must remain repository-external",
            StringComparison.Ordinal));
        Assert.True(childStart > source.IndexOf(
            "the video record gate was not accepted",
            StringComparison.Ordinal));
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

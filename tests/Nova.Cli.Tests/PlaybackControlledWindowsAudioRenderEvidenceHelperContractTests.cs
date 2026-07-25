namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceHelperContractTests
{
    private static readonly string[] RequiredFragments =
    [
        "[string]$ExpectedCommit",
        "[string]$RuntimeDirectory",
        "[string]$FixturePath",
        "[string]$VideoRecordPath",
        "[string]$RenderEvidenceRecordPath",
        "[string]$FailureRecordPath",
        "Invoke-SanitizedNativeCommand",
        "Get-ValidationFailure",
        "PeerCastNovaControlledRenderEvidenceValidation",
        "ValidationFailure",
        "@('branch', '--show-current')",
        "@('rev-parse', 'HEAD')",
        "@('status', '--porcelain', '--untracked-files=all')",
        "--verify-controlled-windows-run-record",
        "--capture-controlled-windows-audio-render-evidence",
        "--render-evidence-record-path",
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE 1",
        "Delivery-Attempts: 4",
        "Delivered: 4",
        "Retained-WouldBlock: 0",
        "Physical-Runs-Executed-By-This-Command: True",
        "Audio-Acceptance-Claimed: False",
        "External-Binary-Or-Payload-Published: False",
        "Failure-Phase: ",
        "Failure-Category: ",
        "Raw-Exception-Published: False",
        "Raw-Native-Stderr-Published: False",
        "Sanitized helper failure record retained: ",
        "Operator observation required: Audio audible: Yes / No",
        "Do not rerun this command.",
    ];

    [Fact]
    public void Helper_retains_clean_main_video_gate_and_single_run_contract()
    {
        var script = File.ReadAllText(FindScript());

        foreach (var fragment in RequiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(
            "--capture-controlled-windows-delivery-diagnostic",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
            script,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            Count(
                script,
                "--capture-controlled-windows-audio-render-evidence"));
    }

    [Fact]
    public void Helper_sanitizes_native_stderr_and_retains_only_bounded_failure_identity()
    {
        var script = File.ReadAllText(FindScript());

        Assert.Contains(
            "1> $standardOutputPath 2> $standardErrorPath",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$ErrorActionPreference = 'Continue'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$ErrorActionPreference = $nativeErrorActionPreference",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$current.Data.Contains($validationMarker)",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$current = $current.InnerException",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$validationPrefix + 'unexpected helper failure.'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Physical-Run-Completion-Established: False",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("& git ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("& dotnet ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$failureMessage.StartsWith(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ScriptStackTrace", script, StringComparison.Ordinal);
        Assert.DoesNotContain("InvocationInfo", script, StringComparison.Ordinal);
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "The controlled audio render evidence helper was not found.");
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }
}

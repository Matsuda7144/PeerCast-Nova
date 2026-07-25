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
        "[string]$TerminalRenderRecordPath",
        "[string]$FailureRecordPath",
        "Invoke-SanitizedNativeCommand",
        "Get-ValidationFailure",
        "PeerCastNovaControlledRenderEvidenceValidation",
        "ValidationFailure",
        "Get-ValidatedTerminalRecordLines",
        "@('branch', '--show-current')",
        "@('rev-parse', 'HEAD')",
        "@('status', '--porcelain', '--untracked-files=all')",
        "--verify-controlled-windows-run-record",
        "--capture-controlled-windows-audio-render-evidence",
        "--render-evidence-record-path",
        "--terminal-render-record-path",
        "--failure-record-path",
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE 1",
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-TERMINAL-RENDER 1",
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-TERMINAL-RENDER",
        "Delivery-Attempts: 4",
        "Delivered: 4",
        "Retained-WouldBlock: 0",
        "Physical-Runs-Executed-By-This-Command: True",
        "PhysicalRunStarted",
        "PhysicalRunCompleted",
        "CompletedRenderEvidenceEstablished",
        "AudioAcceptanceClaimed",
        "ExternalBinaryOrPayloadPublished",
        "Canonical-Terminal-Render-Record-Produced: False",
        "Failure-Phase: ",
        "Failure-Category: ",
        "Raw-Exception-Published: False",
        "Raw-Native-Stderr-Published: False",
        "Terminal render record: Produced",
        "Completed render evidence: Not established",
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
        Assert.Equal(1, Count(script, "--terminal-render-record-path"));
    }

    [Fact]
    public void Helper_requires_one_exclusive_completed_terminal_or_failure_outcome()
    {
        var script = File.ReadAllText(FindScript());

        Assert.Contains("$captureExitCode -eq 0", script, StringComparison.Ordinal);
        Assert.Contains("$captureExitCode -eq 1", script, StringComparison.Ordinal);
        Assert.Contains("$captureExitCode -eq 2", script, StringComparison.Ordinal);
        Assert.Contains("$captureOutcome -ceq 'Completed'", script, StringComparison.Ordinal);
        Assert.Contains("$recordPaths = @(", script, StringComparison.Ordinal);
        Assert.Contains("must be pairwise distinct", script, StringComparison.Ordinal);
        Assert.Contains("$terminalLines.Count -ne 3", script, StringComparison.Ordinal);
        Assert.Contains("$recordLines.Count -eq 26", script, StringComparison.Ordinal);
        Assert.Contains("$finalExitCode = 0", script, StringComparison.Ordinal);
        Assert.Contains("$finalExitCode = 1", script, StringComparison.Ordinal);
        Assert.Contains("$finalExitCode = 2", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Completed render evidence: Established'\n        $finalExitCode = 1",
            script,
            StringComparison.Ordinal);
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
        Assert.Contains(
            "-not [System.IO.File]::Exists($terminalRecordFull)",
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

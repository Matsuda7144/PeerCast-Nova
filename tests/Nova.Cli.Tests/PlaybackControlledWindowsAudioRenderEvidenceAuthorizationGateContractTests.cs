namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceAuthorizationGateContractTests
{
    [Fact]
    public void Gate_requires_exact_statement_and_strict_readiness_validation_before_helper()
    {
        var script = File.ReadAllText(FindScript());

        string[] requiredFragments =
        [
            "[string]$ReadinessRecordPath",
            "[string]$AuthorizationStatement",
            "I explicitly authorize exactly one controlled physical audio render run.",
            "$AuthorizationStatement -cne $requiredAuthorizationStatement",
            "the exact explicit one-run authorization statement is required",
            "--verify-controlled-windows-audio-render-readiness-record",
            "Result: ReadinessReportValidated",
            "Readiness-Report-Validated: True",
            "Ready-For-Separate-Authorization: True",
            "Physical-Run-Authorization-Granted: False",
            "Physical-Runs-Executed-By-This-Command: False",
            "Audio-Acceptance-Claimed: False",
            "$readinessDigestBefore",
            "$readinessDigestAfter",
            "the retained readiness record changed during validation",
        ];

        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }

        Assert.Equal(
            1,
            Count(script, "--verify-controlled-windows-audio-render-readiness-record"));
        Assert.Equal(
            1,
            Count(script, "Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1"));

        var readinessIndex = script.IndexOf(
            "--verify-controlled-windows-audio-render-readiness-record",
            StringComparison.Ordinal);
        var helperIndex = script.IndexOf(
            "Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1",
            StringComparison.Ordinal);
        Assert.True(readinessIndex >= 0);
        Assert.True(helperIndex > readinessIndex);
    }

    [Fact]
    public void Gate_rechecks_clean_main_and_fresh_external_distinct_paths()
    {
        var script = File.ReadAllText(FindScript());

        string[] requiredFragments =
        [
            "@('branch', '--show-current')",
            "@('rev-parse', 'HEAD')",
            "@('status', '--porcelain', '--untracked-files=all')",
            "the retained readiness record is unavailable",
            "every future physical outcome path must still be fresh",
            "every future physical outcome parent directory must exist",
            "runtime, inputs, readiness, and outcome paths must be pairwise distinct",
            "runtime, inputs, readiness, and outcome paths must remain repository-external",
            "the readiness record must remain outside the runtime directory",
            "a future physical outcome path was occupied before helper invocation",
            "$postHead -ine $ExpectedCommit",
            "$helperInvoked = $true",
        ];

        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }

        Assert.Equal(
            2,
            Count(script, "@('rev-parse', 'HEAD')"));
        Assert.Equal(
            2,
            Count(script, "@('status', '--porcelain', '--untracked-files=all')"));
    }

    [Fact]
    public void Gate_invokes_only_existing_helper_and_tracks_post_invocation_failure_truthfully()
    {
        var script = File.ReadAllText(FindScript());

        string[] forbiddenFragments =
        [
            "--capture-controlled-windows-audio-render-evidence",
            "--controlled-windows-audio-render-evidence",
            "--controlled-windows-physical-run",
            "--capture-controlled-windows-delivery-diagnostic",
            "--controlled-windows-known-audio-tone-probe",
            "--controlled-windows-decoded-audio-slice-probe",
            "Save-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1",
            "Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1",
        ];

        foreach (var fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(fragment, script, StringComparison.Ordinal);
        }

        string[] requiredFragments =
        [
            "$maximumNativeOutputBytes = 512 * 1024",
            "$maximumNativeErrorBytes = 64 * 1024",
            "@(0, 1, 2) -cnotcontains $helperResult.ExitCode",
            "$helperResult.OutputLines | ForEach-Object",
            "$helperResult.ErrorLines | ForEach-Object",
            "Physical helper invoked: Yes",
            "Physical run completion established: No",
            "Physical helper invoked: No",
            "Physical runs executed by this command: No",
            "Do not rerun this command.",
        ];

        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "The readiness-required physical audio entrypoint was not found.");
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

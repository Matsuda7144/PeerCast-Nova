namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceReadinessHelperContractTests
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
        "PeerCastNovaControlledRenderReadinessValidation",
        "@('branch', '--show-current')",
        "@('rev-parse', 'HEAD')",
        "@('status', '--porcelain', '--untracked-files=all')",
        "every future output record path must be fresh",
        "every future output record parent directory must exist",
        "future output record paths must be pairwise distinct",
        "must remain repository-external",
        "Get-FileHash -LiteralPath $fixtureFull -Algorithm SHA256",
        "@('restore', (Join-Path $repoRoot 'PeerCast-Nova.sln'))",
        "@('build', $runnerProject, '--configuration', 'Release', '--no-restore')",
        "--verify-controlled-windows-run-record",
        "Result: VideoAccepted",
        "Record-Accepted: True",
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS 1",
        "Result: ReadyForSeparateAuthorization",
        "Expected-Runtime-Build: ",
        "Runtime-Directory-Present: True",
        "Completed-Record-Path-Fresh: True",
        "Terminal-Record-Path-Fresh: True",
        "Failure-Record-Path-Fresh: True",
        "Record-Paths-Repository-External: True",
        "Record-Paths-Pairwise-Distinct: True",
        "Readiness-Only: True",
        "Physical-Run-Authorization-Granted: False",
        "Physical-Runs-Executed-By-This-Command: False",
        "Audio-Acceptance-Claimed: False",
        "External-Binary-Or-Payload-Published: False",
        "Readiness does not authorize physical execution.",
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS",
    ];

    [Fact]
    public void Readiness_script_preserves_clean_main_input_and_path_gates()
    {
        var script = File.ReadAllText(FindScript());

        foreach (var fragment in RequiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }

        Assert.Contains(
            "1> $standardOutputPath 2> $standardErrorPath",
            script,
            StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Continue'", script, StringComparison.Ordinal);
        Assert.Contains(
            "$ErrorActionPreference = $nativeErrorActionPreference",
            script,
            StringComparison.Ordinal);
        Assert.Contains("$head -ine $ExpectedCommit", script, StringComparison.Ordinal);
        Assert.Contains(
            "$fixtureInfo.Length -ne $expectedFixtureBytes",
            script,
            StringComparison.Ordinal);
        Assert.Equal(2, Count(script, "@('status', '--porcelain', '--untracked-files=all')"));
        Assert.Equal(1, Count(script, "--verify-controlled-windows-run-record"));
    }

    [Fact]
    public void Readiness_script_cannot_start_or_retain_a_physical_audio_run()
    {
        var script = File.ReadAllText(FindScript());

        string[] forbiddenFragments =
        [
            "--capture-controlled-windows-audio-render-evidence",
            "--controlled-windows-audio-render-evidence",
            "--controlled-windows-physical-run",
            "--capture-controlled-windows-delivery-diagnostic",
            "Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1",
            "Write-SanitizedHelperFailureRecord",
            "[System.IO.File]::WriteAllText",
            "[System.IO.File]::Move",
            "New-Item -ItemType Directory",
        ];

        foreach (var fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(fragment, script, StringComparison.Ordinal);
        }

        Assert.Equal(0, Count(script, "--terminal-render-record-path"));
        Assert.Equal(0, Count(script, "--render-evidence-record-path"));
        Assert.Equal(0, Count(script, "--failure-record-path"));
        Assert.Equal(2, Count(script, "Physical-Runs-Executed-By-This-Command: False"));
        Assert.Equal(1, Count(script, "Physical-Run-Authorization-Granted: False"));
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "The controlled audio render readiness script was not found.");
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

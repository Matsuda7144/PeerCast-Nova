using System.Text.RegularExpressions;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderReadinessCaptureHelperContractTests
{
    [Fact]
    public void Capture_wrapper_requires_fresh_external_distinct_atomic_record_promotion()
    {
        var script = File.ReadAllText(FindScript());

        string[] requiredFragments =
        [
            "[string]$ReadinessRecordPath",
            "the readiness record path must be fresh",
            "the readiness record parent directory must exist",
            "the readiness record path must remain repository-external",
            "the readiness record path must remain outside the runtime directory",
            "must be distinct from every input and future output path",
            "New-Object System.Text.UTF8Encoding($false, $true)",
            "[System.IO.FileMode]::CreateNew",
            "[System.IO.FileShare]::None",
            "$recordStream.Flush($true)",
            ".peercast-nova-readiness-",
            "[System.IO.File]::Move($temporaryRecordPath, $readinessRecordFull)",
            "the readiness record path was occupied before promotion",
            "$recordPromoted = $true",
            "$retainedText = if ($recordPromoted) { 'Yes' } else { 'No' }",
            "Readiness record retained: ",
        ];

        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("[System.IO.File]::WriteAllText", script, StringComparison.Ordinal);
        Assert.DoesNotContain("New-Item -ItemType Directory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Force $readinessRecordFull", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_wrapper_invokes_only_readiness_and_managed_record_preflight()
    {
        var script = File.ReadAllText(FindScript());

        Assert.Equal(
            1,
            Count(
                script,
                "Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1"));
        Assert.Equal(
            1,
            Count(
                script,
                "--verify-controlled-windows-audio-render-readiness-record"));
        Assert.Contains("$readinessExitCode -ne 0", script, StringComparison.Ordinal);
        Assert.Contains("$readinessErrorBytes -ne 0", script, StringComparison.Ordinal);
        Assert.Contains("$readinessLines.Count -ne 27", script, StringComparison.Ordinal);
        Assert.Contains("-not $preflight.WithinBounds", script, StringComparison.Ordinal);
        Assert.Contains("Result: ReadinessReportValidated", script, StringComparison.Ordinal);
        Assert.Contains("Readiness-Report-Validated: True", script, StringComparison.Ordinal);

        string[] forbiddenFragments =
        [
            "Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1",
            "--capture-controlled-windows-audio-render-evidence",
            "--controlled-windows-audio-render-evidence",
            "--controlled-windows-physical-run",
            "--capture-controlled-windows-delivery-diagnostic",
            "--controlled-windows-known-audio-tone-probe",
            "--controlled-windows-decoded-audio-slice-probe",
        ];

        foreach (var fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(fragment, script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Capture_wrapper_publishes_only_bounded_non_authorizing_status()
    {
        var script = File.ReadAllText(FindScript());

        string[] requiredFragments =
        [
            "$maximumReadinessOutputBytes = 128 * 1024",
            "$maximumReadinessErrorBytes = 32 * 1024",
            "$maximumNativeOutputBytes = 32 * 1024",
            "$maximumNativeErrorBytes = 32 * 1024",
            "Result: ReadinessRecordCaptured",
            "Readiness-Script-Executed-By-This-Command: True",
            "Readiness-Record-Validated: True",
            "Ready-For-Separate-Authorization: True",
            "Physical-Run-Authorization-Granted: False",
            "Physical-Runs-Executed-By-This-Command: False",
            "Audio-Acceptance-Claimed: False",
            "External-Binary-Or-Payload-Published: False",
            "Readiness capture does not authorize physical execution.",
        ];

        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }

        Assert.Contains(
            "Get-Content -LiteralPath $ErrorPath",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$errorLines.Count -ne 3",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$errorLines[1] -cne $readinessFailurePhysicalClaim",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$errorLines[2] -cne $readinessFailureAuthorizationClaim",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$errorLines[0] -ceq ($readinessFailurePrefix + $reason)",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "'the readiness script reported: ' + $readinessFailureReason",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConvertTo-WindowsProcessArgument",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Start-Process",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "-RedirectStandardOutput $readinessStandardOutputPath",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "-RedirectStandardError $readinessStandardErrorPath",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$readinessExitCode = $readinessProcess.ExitCode",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "& 'powershell.exe' @readinessArguments",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "WriteLine($errorLines",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Write-Output $errorLines",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $readinessRecordFull", script);
        Assert.DoesNotContain("Write-Output $canonicalReadinessText", script);
    }

    [Fact]
    public void Capture_wrapper_allowlists_every_fixed_inner_readiness_gate_reason()
    {
        var wrapper = File.ReadAllText(FindScript());
        var readiness = File.ReadAllText(FindReadinessScript());
        var reasons = Regex.Matches(
                readiness,
                "Stop-ReadinessValidation '([^']+)'",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(19, reasons.Length);
        foreach (var reason in reasons)
        {
            Assert.Contains($"'{reason}'", wrapper, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "'unexpected readiness failure.'",
            wrapper,
            StringComparison.Ordinal);
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Save-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The readiness capture wrapper was not found.");
    }

    private static string FindReadinessScript() =>
        Path.Combine(
            Path.GetDirectoryName(FindScript())!,
            "Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1");

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

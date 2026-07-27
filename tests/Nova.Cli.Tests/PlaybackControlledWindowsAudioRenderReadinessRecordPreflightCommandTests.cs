using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommandTests
{
    private const string Commit = "cdebb629d8297def0dabd266026548afa5b6eec5";
    private const string OtherCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Canonical_readiness_record_round_trips_with_exact_schema()
    {
        var text = PlaybackControlledWindowsAudioRenderReadinessRecordCodec.Serialize(Record());

        Assert.True(
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.TryParse(
                text,
                out var parsed));
        Assert.Equal(Record(), parsed);
        Assert.Equal(27, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.StartsWith(
            "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS 1\n",
            text,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS\n",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_readiness_record_accepts_windows_line_endings()
    {
        var text = PlaybackControlledWindowsAudioRenderReadinessRecordCodec
            .Serialize(Record())
            .Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.True(
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.TryParse(
                text,
                out var parsed));
        Assert.Equal(Record(), parsed);
    }

    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public void Codec_rejects_noncanonical_or_weakened_records(string text)
    {
        Assert.False(
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.TryParse(
                text,
                out _));
    }

    public static IEnumerable<object[]> InvalidRecords()
    {
        var canonical = PlaybackControlledWindowsAudioRenderReadinessRecordCodec
            .Serialize(Record());
        yield return ["\uFEFF" + canonical];
        yield return [canonical + "\n"];
        yield return [canonical.TrimEnd('\n')];
        yield return [canonical.Replace(
            "Readiness-Only: True",
            "Readiness-Only: False",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Physical-Run-Authorization-Granted: False",
            "Physical-Run-Authorization-Granted: True",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Physical-Runs-Executed-By-This-Command: False",
            "Physical-Runs-Executed-By-This-Command: True",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Audio-Acceptance-Claimed: False",
            "Audio-Acceptance-Claimed: True",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Expected-Runtime-Build: " + PlaybackFfmpegBackendCandidate.BuildId,
            "Expected-Runtime-Build: other",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            $"Validated-Commit: {Commit}",
            $"Validated-Commit: {Commit.ToUpperInvariant()}",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Audio-Buffer-Milliseconds: 100",
            "Audio-Buffer-Milliseconds: 101",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Completed-Record-Path-Fresh: True\nTerminal-Record-Path-Fresh: True",
            "Terminal-Record-Path-Fresh: True\nCompleted-Record-Path-Fresh: True",
            StringComparison.Ordinal)];
        yield return [canonical.Replace(
            "Runner-Build: Accepted",
            "Runner-Build: Rejected",
            StringComparison.Ordinal)];
    }

    [Fact]
    public async Task Valid_readiness_record_is_revalidated_without_physical_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec.Serialize(Record()));
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand
                    .RunAsync(Arguments(path), output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                    .ReadinessReportValidated,
                result.Result);
            Assert.True(result.Validated);
            var report = output.ToString();
            string[] expectedLines =
            [
                "Milestone: " +
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec.CurrentMilestone,
                "Result: ReadinessReportValidated",
                $"Validated-Commit: {Commit}",
                $"Expected-Runtime-Build: {PlaybackFfmpegBackendCandidate.BuildId}",
                "Fixture-SHA256: " +
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec.ApprovedFixtureSha256,
                "Fixture-Bytes: " +
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec.ApprovedFixtureBytes,
                "Maximum-Decode-Operations: 128",
                "Maximum-Delivery-Operations: 128",
                "Fixture-Packet-Bytes: 65536",
                "Observation-Hold-Milliseconds: 1000",
                "Audio-Buffer-Milliseconds: 100",
                "Readiness-Report-Validated: True",
                "Ready-For-Separate-Authorization: True",
                "Physical-Run-Authorization-Granted: False",
                "Physical-Runs-Executed-By-This-Command: False",
                "Audio-Acceptance-Claimed: False",
                "External-Binary-Or-Payload-Published: False",
            ];
            Assert.Equal(17, expectedLines.Length);
            Assert.Equal(17, report.Count(character => character == '\n'));
            Assert.Equal(
                string.Join(Environment.NewLine, expectedLines) + Environment.NewLine,
                report);
            Assert.Contains("Result: ReadinessReportValidated", report, StringComparison.Ordinal);
            Assert.Contains("Readiness-Report-Validated: True", report, StringComparison.Ordinal);
            Assert.Contains(
                "Ready-For-Separate-Authorization: True",
                report,
                StringComparison.Ordinal);
            Assert.Contains(
                "Physical-Run-Authorization-Granted: False",
                report,
                StringComparison.Ordinal);
            Assert.Contains(
                "Physical-Runs-Executed-By-This-Command: False",
                report,
                StringComparison.Ordinal);
            Assert.Contains("Audio-Acceptance-Claimed: False", report, StringComparison.Ordinal);
            Assert.DoesNotContain(path, report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Valid_record_with_different_review_identity_is_rejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec.Serialize(Record()));
            using var output = new StringWriter();
            var arguments = Arguments(path);
            arguments[4] = OtherCommit;

            var result =
                await PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand
                    .RunAsync(arguments, output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                    .IdentityMismatch,
                result.Result);
            Assert.False(result.Validated);
            var report = output.ToString();
            Assert.Contains("Result: IdentityMismatch", report, StringComparison.Ordinal);
            Assert.Contains("Readiness-Report-Validated: False", report, StringComparison.Ordinal);
            Assert.Contains(
                "Ready-For-Separate-Authorization: False",
                report,
                StringComparison.Ordinal);
            Assert.Contains(
                "Physical-Run-Authorization-Granted: False",
                report,
                StringComparison.Ordinal);
            Assert.DoesNotContain(path, report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Malformed_record_is_rejected_without_path_or_payload_disclosure()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "private malformed readiness payload\n");
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand
                    .RunAsync(Arguments(path), output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult.InvalidRecord,
                result.Result);
            Assert.False(result.Validated);
            var report = output.ToString();
            Assert.Contains("Result: InvalidRecord", report, StringComparison.Ordinal);
            Assert.DoesNotContain(path, report, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private malformed", report, StringComparison.Ordinal);
            Assert.Contains(
                "Physical-Runs-Executed-By-This-Command: False",
                report,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Duplicate_or_missing_arguments_are_invalid_configuration()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var duplicateOutput = new StringWriter();
            var duplicate = Arguments(path).Concat(["--record", path]).ToArray();
            var duplicateResult =
                await PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand
                    .RunAsync(duplicate, duplicateOutput);
            Assert.Equal(2, duplicateResult.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                    .InvalidConfiguration,
                duplicateResult.Result);

            using var missingOutput = new StringWriter();
            var missing = Arguments(path)[..^2];
            var missingResult =
                await PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand
                    .RunAsync(missing, missingOutput);
            Assert.Equal(2, missingResult.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                    .InvalidConfiguration,
                missingResult.Result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PowerShell_readiness_schema_stays_aligned_and_write_free()
    {
        var script = File.ReadAllText(FindReadinessScript());
        string[] requiredLabels =
        [
            "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS 1",
            "Milestone: 0.0.10d Phase 7",
            "Result: ReadyForSeparateAuthorization",
            "Validated-Commit: ",
            "Expected-Runtime-Build: ",
            "Runtime-Directory-Present: True",
            "Fixture-SHA256: ",
            "Fixture-Bytes: ",
            "Video-Record-Gate: Accepted",
            "Maximum-Decode-Operations: ",
            "Maximum-Delivery-Operations: ",
            "Fixture-Packet-Bytes: ",
            "Observation-Hold-Milliseconds: ",
            "Audio-Buffer-Milliseconds: ",
            "Completed-Record-Path-Fresh: True",
            "Terminal-Record-Path-Fresh: True",
            "Failure-Record-Path-Fresh: True",
            "Record-Paths-Repository-External: True",
            "Record-Paths-Pairwise-Distinct: True",
            "Runner-Build: Accepted",
            "Readiness-Only: True",
            "Physical-Run-Authorization-Granted: False",
            "Physical-Runs-Executed-By-This-Command: False",
            "Audio-Acceptance-Claimed: False",
            "External-Binary-Or-Payload-Published: False",
            "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS",
        ];

        foreach (var label in requiredLabels)
        {
            Assert.Contains(label, script, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("[System.IO.File]::WriteAllText", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[System.IO.File]::Move", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand.Argument,
            script,
            StringComparison.Ordinal);
    }

    private static PlaybackControlledWindowsAudioRenderReadinessRecord Record() =>
        new(
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.CurrentMilestone,
            Commit,
            PlaybackFfmpegBackendCandidate.BuildId,
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.ApprovedFixtureSha256,
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec.ApprovedFixtureBytes,
            MaximumDecodeOperations: 128,
            MaximumDeliveryOperations: 128,
            FixturePacketBytes: 65_536,
            ObservationHoldMilliseconds: 1000,
            PlaybackControlledWindowsAudioRenderReadinessRecordCodec
                .ApprovedAudioBufferMilliseconds);

    private static string[] Arguments(string path) =>
    [
        PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand.Argument,
        "--record",
        path,
        "--expected-commit",
        Commit,
        "--fixture-sha256",
        PlaybackControlledWindowsAudioRenderReadinessRecordCodec.ApprovedFixtureSha256,
        "--max-decode-operations",
        "128",
        "--max-delivery-operations",
        "128",
        "--fixture-packet-bytes",
        "65536",
        "--observation-hold-ms",
        "1000",
        "--audio-buffer-ms",
        "100",
    ];

    private static string FindReadinessScript()
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

        throw new FileNotFoundException("The readiness script was not found.");
    }
}

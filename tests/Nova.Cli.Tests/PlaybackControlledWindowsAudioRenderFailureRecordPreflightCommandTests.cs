namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommandTests
{
    [Fact]
    public void Child_failure_codec_matches_existing_capture_serializer()
    {
        const int exitCode = unchecked((int)0xC0000005);
        var record = ChildRecord(exitCode, stdoutBytes: 17, stderrBytes: 29);

        var serialized =
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(record);

        Assert.Equal(
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition
                .SerializeFailureRecord(exitCode, 17, 29),
            serialized);
        Assert.True(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
                serialized,
                out var parsed));
        Assert.Equal(record, parsed);
    }

    [Fact]
    public void Child_failure_codec_accepts_missing_child_result_only_canonically()
    {
        var record = new PlaybackControlledWindowsAudioRenderChildFailureRecord(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentMilestone,
            ChildExitCodeAvailable: false,
            ChildExitCode: 0,
            StdoutByteCount: 0,
            StderrByteCount: 0,
            ChildProcessResultCaptured: false);

        var serialized =
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(record);

        Assert.True(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
                serialized,
                out var parsed));
        Assert.Equal(record, parsed);
    }

    [Theory]
    [InlineData("Child-Process-Result-Captured: True", "Child-Process-Result-Captured: False")]
    [InlineData("Child-Exit-Code-Hex: C0000005", "Child-Exit-Code-Hex: C0000004")]
    [InlineData("Raw-Stderr-Published: False", "Raw-Stderr-Published: True")]
    public void Child_failure_codec_rejects_contradictions(
        string original,
        string replacement)
    {
        var serialized =
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(
                ChildRecord(unchecked((int)0xC0000005), 17, 29));
        var tampered = serialized.Replace(original, replacement, StringComparison.Ordinal);

        Assert.False(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
                tampered,
                out _));
    }

    [Fact]
    public void Helper_failure_codec_round_trips_reviewed_identity()
    {
        var record = HelperRecord(
            PlaybackControlledWindowsAudioRenderHelperFailurePhase.RunnerBuild,
            PlaybackControlledWindowsAudioRenderHelperFailureCategory.ValidationFailure);

        var serialized =
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.Serialize(record);

        Assert.True(
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.TryParse(
                serialized,
                out var parsed));
        Assert.Equal(record, parsed);
    }

    [Theory]
    [InlineData("Failure-Phase: RunnerBuild", "Failure-Phase: PrivatePhase")]
    [InlineData("Failure-Category: ValidationFailure", "Failure-Category: PrivateCategory")]
    [InlineData("Raw-Exception-Published: False", "Raw-Exception-Published: True")]
    public void Helper_failure_codec_rejects_unknown_or_private_identity(
        string original,
        string replacement)
    {
        var serialized =
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.Serialize(
                HelperRecord(
                    PlaybackControlledWindowsAudioRenderHelperFailurePhase.RunnerBuild,
                    PlaybackControlledWindowsAudioRenderHelperFailureCategory.ValidationFailure));
        var tampered = serialized.Replace(original, replacement, StringComparison.Ordinal);

        Assert.False(
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.TryParse(
                tampered,
                out _));
    }

    [Fact]
    public void Helper_failure_codec_matches_script_schema_and_reviewed_values()
    {
        var script = File.ReadAllText(FindHelperScript());
        var canonical =
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.Serialize(
                HelperRecord(
                    PlaybackControlledWindowsAudioRenderHelperFailurePhase.RunnerBuild,
                    PlaybackControlledWindowsAudioRenderHelperFailureCategory.ValidationFailure));

        foreach (var line in canonical.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Failure-Phase: ", StringComparison.Ordinal) ||
                line.StartsWith("Failure-Category: ", StringComparison.Ordinal))
            {
                continue;
            }
            Assert.Contains(line, script, StringComparison.Ordinal);
        }

        foreach (var phase in Enum.GetValues<
                     PlaybackControlledWindowsAudioRenderHelperFailurePhase>())
        {
            if (phase != PlaybackControlledWindowsAudioRenderHelperFailurePhase.None)
            {
                Assert.Contains(phase.ToString(), script, StringComparison.Ordinal);
            }
        }
        foreach (var category in Enum.GetValues<
                     PlaybackControlledWindowsAudioRenderHelperFailureCategory>())
        {
            if (category != PlaybackControlledWindowsAudioRenderHelperFailureCategory.None)
            {
                Assert.Contains(category.ToString(), script, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Valid_child_failure_is_revalidated_without_raw_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(
                    ChildRecord(unchecked((int)0xC0000005), 17, 29)));
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.RunAsync(
                    Arguments(path, "child"),
                    output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult
                    .FailureRecordValidated,
                result.Result);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderFailureRecordSource.ChildCapture,
                result.DetectedSource);
            Assert.True(result.Validated);
            var report = output.ToString();
            Assert.Contains("Detected-Failure-Source: ChildCapture", report);
            Assert.Contains("Child-Exit-Code-Hex: C0000005", report);
            Assert.Contains("Stdout-Bytes: 17", report);
            Assert.Contains("Stderr-Bytes: 29", report);
            Assert.Contains("Physical-Run-Completion-Established: False", report);
            Assert.Contains("Physical-Runs-Executed-By-This-Command: False", report);
            Assert.DoesNotContain(path, report, StringComparison.Ordinal);
            Assert.DoesNotContain("private stdout", report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Valid_helper_failure_is_revalidated_without_exception_text()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.Serialize(
                    HelperRecord(
                        PlaybackControlledWindowsAudioRenderHelperFailurePhase.VideoRecordGate,
                        PlaybackControlledWindowsAudioRenderHelperFailureCategory.IoFailure)));
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.RunAsync(
                    Arguments(path, "helper"),
                    output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderFailureRecordSource.Helper,
                result.DetectedSource);
            Assert.True(result.Validated);
            var report = output.ToString();
            Assert.Contains("Helper-Failure-Phase: VideoRecordGate", report);
            Assert.Contains("Helper-Failure-Category: IoFailure", report);
            Assert.Contains("Audio-Acceptance-Claimed: False", report);
            Assert.DoesNotContain(path, report, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", report, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("child", PlaybackControlledWindowsAudioRenderFailureRecordSource.Helper)]
    [InlineData("helper", PlaybackControlledWindowsAudioRenderFailureRecordSource.ChildCapture)]
    public async Task Expected_source_mismatch_fails_closed(
        string expectedSource,
        PlaybackControlledWindowsAudioRenderFailureRecordSource actualSource)
    {
        var path = Path.GetTempFileName();
        try
        {
            var content = actualSource ==
                PlaybackControlledWindowsAudioRenderFailureRecordSource.Helper
                ? PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.Serialize(
                    HelperRecord(
                        PlaybackControlledWindowsAudioRenderHelperFailurePhase.CaptureIntegration,
                        PlaybackControlledWindowsAudioRenderHelperFailureCategory.InvalidOperation))
                : PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(
                    ChildRecord(1, 2, 3));
            await File.WriteAllTextAsync(path, content);
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.RunAsync(
                    Arguments(path, expectedSource),
                    output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult.SourceMismatch,
                result.Result);
            Assert.Equal(actualSource, result.DetectedSource);
            Assert.False(result.Validated);
            Assert.Contains("Failure-Record-Validated: False", output.ToString());
            Assert.DoesNotContain(path, output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Malformed_record_fails_closed_without_publishing_content()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "private malformed record C:\\secret");
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.RunAsync(
                    Arguments(path, "child"),
                    output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult.InvalidRecord,
                result.Result);
            Assert.False(result.Validated);
            var report = output.ToString();
            Assert.DoesNotContain(path, report, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", report, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("malformed", report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Missing_duplicate_or_unknown_arguments_are_invalid_configuration(int kind)
    {
        var path = Path.GetTempFileName();
        try
        {
            var arguments = kind switch
            {
                0 =>
                [
                    PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.Argument,
                    "--record", path,
                ],
                1 => Arguments(path, "child").Concat(
                    ["--expected-source", "child"]).ToArray(),
                _ => Arguments(path, "child").Concat(["--unknown"]).ToArray(),
            };
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.RunAsync(
                    arguments,
                    output);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult
                    .InvalidConfiguration,
                result.Result);
            Assert.False(result.Validated);
            Assert.Equal("Result: InvalidConfiguration" + Environment.NewLine, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] Arguments(string path, string expectedSource) =>
    [
        PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.Argument,
        "--record", path,
        "--expected-source", expectedSource,
    ];

    private static PlaybackControlledWindowsAudioRenderChildFailureRecord ChildRecord(
        int exitCode,
        long stdoutBytes,
        long stderrBytes) =>
        new(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentMilestone,
            ChildExitCodeAvailable: true,
            exitCode,
            stdoutBytes,
            stderrBytes,
            ChildProcessResultCaptured: true);

    private static PlaybackControlledWindowsAudioRenderHelperFailureRecord HelperRecord(
        PlaybackControlledWindowsAudioRenderHelperFailurePhase phase,
        PlaybackControlledWindowsAudioRenderHelperFailureCategory category) =>
        new(
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.CurrentMilestone,
            phase,
            category);

    private static string FindHelperScript()
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
}

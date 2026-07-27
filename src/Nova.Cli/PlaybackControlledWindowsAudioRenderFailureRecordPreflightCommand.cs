using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsAudioRenderFailureRecordSource
{
    None,
    ChildCapture,
    Helper,
}

public enum PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult
{
    None,
    FailureRecordValidated,
    InvalidConfiguration,
    InvalidRecord,
    SourceMismatch,
}

public enum PlaybackControlledWindowsAudioRenderHelperFailurePhase
{
    None,
    SolutionRestore,
    RunnerBuild,
    RunnerAssemblyValidation,
    VideoRecordGate,
    RenderEvidenceCapture,
    CaptureIntegration,
    EvidenceEnvelopeValidation,
    TerminalEnvelopeValidation,
    PostRunRepositoryCheck,
    Completed,
}

public enum PlaybackControlledWindowsAudioRenderHelperFailureCategory
{
    None,
    ValidationFailure,
    CommandUnavailable,
    AccessDenied,
    IoFailure,
    InvalidArgument,
    InvalidOperation,
    PowerShellRuntime,
    UnexpectedException,
}

public sealed record PlaybackControlledWindowsAudioRenderChildFailureRecord(
    int SchemaVersion,
    string Milestone,
    bool ChildExitCodeAvailable,
    int ChildExitCode,
    long StdoutByteCount,
    long StderrByteCount,
    string LastNativeAudioStage,
    bool ChildProcessResultCaptured)
{
    public string ControlledRunResult { get; init; } = "None";
}

public sealed record PlaybackControlledWindowsAudioRenderHelperFailureRecord(
    int SchemaVersion,
    string Milestone,
    PlaybackControlledWindowsAudioRenderHelperFailurePhase FailurePhase,
    PlaybackControlledWindowsAudioRenderHelperFailureCategory FailureCategory);

public static class PlaybackControlledWindowsAudioRenderChildFailureRecordCodec
{
    public const int CurrentSchemaVersion = 4;
    public const string CurrentMilestone = "0.0.10d Phase 6";
    public const int MaximumRecordCharacterCount = 32 * 1024;

    private const int LegacySchemaVersion = 2;
    private const int PreviousSchemaVersion = 3;
    private const string LegacyHeader =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 2";
    private const string PreviousHeader =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 3";
    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 4";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE";

    public static string Serialize(
        PlaybackControlledWindowsAudioRenderChildFailureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsValid(record))
        {
            throw new ArgumentException(
                "The controlled audio child failure record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(896);
        Append(
            builder,
            record.SchemaVersion switch
            {
                LegacySchemaVersion => LegacyHeader,
                PreviousSchemaVersion => PreviousHeader,
                _ => Header,
            });
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: RenderEvidenceRecordNotProduced");
        Append(builder, $"Child-Exit-Code-Available: {record.ChildExitCodeAvailable}");
        Append(
            builder,
            "Child-Exit-Code-Signed: " +
            record.ChildExitCode.ToString(CultureInfo.InvariantCulture));
        Append(builder, $"Child-Exit-Code-Hex: {unchecked((uint)record.ChildExitCode):X8}");
        Append(
            builder,
            $"Stdout-Bytes: {record.StdoutByteCount.ToString(CultureInfo.InvariantCulture)}");
        Append(
            builder,
            $"Stderr-Bytes: {record.StderrByteCount.ToString(CultureInfo.InvariantCulture)}");
        if (record.SchemaVersion >= PreviousSchemaVersion)
        {
            Append(builder, $"Last-Native-Audio-Stage: {record.LastNativeAudioStage}");
        }
        if (record.SchemaVersion == CurrentSchemaVersion)
        {
            Append(builder, $"Controlled-Run-Result: {record.ControlledRunResult}");
        }
        Append(builder, "Raw-Stdout-Published: False");
        Append(builder, "Raw-Stderr-Published: False");
        Append(builder, "Canonical-Render-Evidence-Record-Produced: False");
        Append(builder, "Canonical-Terminal-Render-Record-Produced: False");
        Append(builder, $"Child-Process-Result-Captured: {record.ChildProcessResultCaptured}");
        Append(builder, "Physical-Run-Completion-Established: False");
        Append(builder, "Audio-Acceptance-Claimed: False");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(builder, Footer);
        if (builder.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio child failure record exceeds its size limit.");
        }

        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioRenderChildFailureRecord? record)
    {
        record = null;
        return TryParseVersion(
                text,
                CurrentSchemaVersion,
                Header,
                expectedLineCount: 20,
                out record) ||
            TryParseVersion(
                text,
                PreviousSchemaVersion,
                PreviousHeader,
                expectedLineCount: 19,
                out record) ||
            TryParseVersion(
                text,
                LegacySchemaVersion,
                LegacyHeader,
                expectedLineCount: 18,
                out record);
    }

    private static bool TryParseVersion(
        string? text,
        int schemaVersion,
        string header,
        int expectedLineCount,
        out PlaybackControlledWindowsAudioRenderChildFailureRecord? record)
    {
        record = null;
        if (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryGetLines(
                text,
                MaximumRecordCharacterCount,
                expectedLineCount,
                out var normalized,
                out var lines) ||
            !string.Equals(lines[0], header, StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[1],
                "Milestone: ",
                out var milestone) ||
            !string.Equals(lines[2], "Kind: Audio", StringComparison.Ordinal) ||
            !string.Equals(
                lines[3],
                "Result: RenderEvidenceRecordNotProduced",
                StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadBoolean(
                lines[4],
                "Child-Exit-Code-Available: ",
                out var exitAvailable) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadInt32(
                lines[5],
                "Child-Exit-Code-Signed: ",
                out var exitCode) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadHexUInt32(
                lines[6],
                "Child-Exit-Code-Hex: ",
                out var exitCodeHex) ||
            exitCodeHex != unchecked((uint)exitCode) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadInt64(
                lines[7],
                "Stdout-Bytes: ",
                out var stdoutBytes) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadInt64(
                lines[8],
                "Stderr-Bytes: ",
                out var stderrBytes))
        {
            return false;
        }

        var hasNativeStage = schemaVersion >= PreviousSchemaVersion;
        var hasControlledRunResult = schemaVersion == CurrentSchemaVersion;
        var offset = hasNativeStage ? 1 : 0;
        var lastNativeAudioStage = "None";
        if (hasNativeStage &&
            (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[9],
                "Last-Native-Audio-Stage: ",
                out lastNativeAudioStage) ||
             !PlaybackControlledWindowsAudioProcessFailureDiagnostics
                 .IsNativeAudioStageOrNone(lastNativeAudioStage)))
        {
            return false;
        }

        var controlledRunResult = "None";
        if (hasControlledRunResult &&
            (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[10],
                "Controlled-Run-Result: ",
                out controlledRunResult) ||
             !PlaybackControlledWindowsAudioProcessFailureDiagnostics
                 .IsControlledRunResultOrNone(controlledRunResult)))
        {
            return false;
        }
        if (hasControlledRunResult)
        {
            offset++;
        }

        if (!string.Equals(
                lines[9 + offset],
                "Raw-Stdout-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[10 + offset],
                "Raw-Stderr-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[11 + offset],
                "Canonical-Render-Evidence-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[12 + offset],
                "Canonical-Terminal-Render-Record-Produced: False",
                StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadBoolean(
                lines[13 + offset],
                "Child-Process-Result-Captured: ",
                out var childResultCaptured) ||
            !string.Equals(
                lines[14 + offset],
                "Physical-Run-Completion-Established: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[15 + offset],
                "Audio-Acceptance-Claimed: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[16 + offset],
                "External-Binary-Or-Payload-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[17 + offset], Footer, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsAudioRenderChildFailureRecord(
            schemaVersion,
            milestone,
            exitAvailable,
            exitCode,
            stdoutBytes,
            stderrBytes,
            lastNativeAudioStage,
            childResultCaptured)
        {
            ControlledRunResult = controlledRunResult,
        };
        if (!IsValid(candidate) ||
            !string.Equals(Serialize(candidate), normalized + "\n", StringComparison.Ordinal))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsValid(
        PlaybackControlledWindowsAudioRenderChildFailureRecord record) =>
        record.SchemaVersion is LegacySchemaVersion or PreviousSchemaVersion or
            CurrentSchemaVersion &&
        string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
        record.StdoutByteCount >= 0 &&
        record.StderrByteCount >= 0 &&
        PlaybackControlledWindowsAudioProcessFailureDiagnostics
            .IsNativeAudioStageOrNone(record.LastNativeAudioStage) &&
        PlaybackControlledWindowsAudioProcessFailureDiagnostics
            .IsControlledRunResultOrNone(record.ControlledRunResult) &&
        (record.SchemaVersion != LegacySchemaVersion ||
            string.Equals(record.LastNativeAudioStage, "None", StringComparison.Ordinal)) &&
        (record.SchemaVersion == CurrentSchemaVersion ||
            string.Equals(record.ControlledRunResult, "None", StringComparison.Ordinal)) &&
        record.ChildExitCodeAvailable == record.ChildProcessResultCaptured &&
        (record.ChildExitCodeAvailable || record.ChildExitCode == 0);

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

public static class PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec
{
    public const int CurrentSchemaVersion = 2;
    public const string CurrentMilestone = "0.0.10d Phase 6";
    public const int MaximumRecordCharacterCount = 32 * 1024;

    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-HELPER-FAILURE 2";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-HELPER-FAILURE";

    public static string Serialize(
        PlaybackControlledWindowsAudioRenderHelperFailureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsValid(record))
        {
            throw new ArgumentException(
                "The controlled audio helper failure record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(768);
        Append(builder, Header);
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: HelperFailure");
        Append(builder, $"Failure-Phase: {record.FailurePhase}");
        Append(builder, $"Failure-Category: {record.FailureCategory}");
        Append(builder, "Canonical-Render-Evidence-Record-Produced: False");
        Append(builder, "Canonical-Terminal-Render-Record-Produced: False");
        Append(builder, "Sanitized-Child-Failure-Record-Produced: False");
        Append(builder, "Physical-Run-Completion-Established: False");
        Append(builder, "Raw-Exception-Published: False");
        Append(builder, "Raw-Native-Stderr-Published: False");
        Append(builder, "Audio-Acceptance-Claimed: False");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(builder, Footer);
        if (builder.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio helper failure record exceeds its size limit.");
        }

        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioRenderHelperFailureRecord? record)
    {
        record = null;
        if (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryGetLines(
                text,
                MaximumRecordCharacterCount,
                expectedLineCount: 15,
                out var normalized,
                out var lines) ||
            !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[1],
                "Milestone: ",
                out var milestone) ||
            !string.Equals(lines[2], "Kind: Audio", StringComparison.Ordinal) ||
            !string.Equals(lines[3], "Result: HelperFailure", StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadEnum(
                lines[4],
                "Failure-Phase: ",
                out PlaybackControlledWindowsAudioRenderHelperFailurePhase failurePhase) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadEnum(
                lines[5],
                "Failure-Category: ",
                out PlaybackControlledWindowsAudioRenderHelperFailureCategory failureCategory) ||
            !string.Equals(
                lines[6],
                "Canonical-Render-Evidence-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[7],
                "Canonical-Terminal-Render-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[8],
                "Sanitized-Child-Failure-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[9],
                "Physical-Run-Completion-Established: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[10], "Raw-Exception-Published: False", StringComparison.Ordinal) ||
            !string.Equals(
                lines[11],
                "Raw-Native-Stderr-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[12], "Audio-Acceptance-Claimed: False", StringComparison.Ordinal) ||
            !string.Equals(
                lines[13],
                "External-Binary-Or-Payload-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[14], Footer, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsAudioRenderHelperFailureRecord(
            CurrentSchemaVersion,
            milestone,
            failurePhase,
            failureCategory);
        if (!IsValid(candidate) ||
            !string.Equals(Serialize(candidate), normalized + "\n", StringComparison.Ordinal))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsValid(
        PlaybackControlledWindowsAudioRenderHelperFailureRecord record) =>
        record.SchemaVersion == CurrentSchemaVersion &&
        string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
        Enum.IsDefined(record.FailurePhase) &&
        record.FailurePhase != PlaybackControlledWindowsAudioRenderHelperFailurePhase.None &&
        Enum.IsDefined(record.FailureCategory) &&
        record.FailureCategory != PlaybackControlledWindowsAudioRenderHelperFailureCategory.None;

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

public readonly record struct
    PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommandResult(
        int ExitCode,
        PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult Result,
        PlaybackControlledWindowsAudioRenderFailureRecordSource DetectedSource,
        bool Validated);

/// <summary>
/// Revalidates one retained child-capture or helper-level controlled audio failure
/// record without starting a runner, runtime, fixture replay, helper, or physical output.
/// </summary>
public static class PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand
{
    public const string Argument =
        "--verify-controlled-windows-audio-render-failure-record";

    private const int ValidatedExitCode = 0;
    private const int ValidationFailureExitCode = 1;
    private const int InvalidConfigurationExitCode = 2;

    public static async ValueTask<
        PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommandResult> RunAsync(
        string[] arguments,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(
                InvalidConfigurationExitCode,
                PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult
                    .InvalidConfiguration,
                PlaybackControlledWindowsAudioRenderFailureRecordSource.None,
                Validated: false);
        }

        var text = await PlaybackControlledWindowsPhysicalRunRecordFile.ReadBoundedAsync(
                options.RecordPath,
                cancellationToken)
            .ConfigureAwait(false);
        var childValid = PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
            text,
            out var childRecord);
        var helperValid = PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec.TryParse(
            text,
            out var helperRecord);
        var detectedSource = childValid
            ? PlaybackControlledWindowsAudioRenderFailureRecordSource.ChildCapture
            : helperValid
                ? PlaybackControlledWindowsAudioRenderFailureRecordSource.Helper
                : PlaybackControlledWindowsAudioRenderFailureRecordSource.None;

        if (detectedSource == PlaybackControlledWindowsAudioRenderFailureRecordSource.None)
        {
            return await WriteReportAsync(
                    output,
                    options.ExpectedSource,
                    detectedSource,
                    PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult.InvalidRecord,
                    childRecord: null,
                    helperRecord: null,
                    validated: false)
                .ConfigureAwait(false);
        }

        if (detectedSource != options.ExpectedSource)
        {
            return await WriteReportAsync(
                    output,
                    options.ExpectedSource,
                    detectedSource,
                    PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult.SourceMismatch,
                    childRecord: null,
                    helperRecord: null,
                    validated: false)
                .ConfigureAwait(false);
        }

        return await WriteReportAsync(
                output,
                options.ExpectedSource,
                detectedSource,
                PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult
                    .FailureRecordValidated,
                childRecord,
                helperRecord,
                validated: true)
            .ConfigureAwait(false);
    }

    private static async ValueTask<
        PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommandResult>
        WriteReportAsync(
            TextWriter output,
            PlaybackControlledWindowsAudioRenderFailureRecordSource expectedSource,
            PlaybackControlledWindowsAudioRenderFailureRecordSource detectedSource,
            PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult result,
            PlaybackControlledWindowsAudioRenderChildFailureRecord? childRecord,
            PlaybackControlledWindowsAudioRenderHelperFailureRecord? helperRecord,
            bool validated)
    {
        var child = validated ? childRecord : null;
        var helper = validated ? helperRecord : null;
        await output.WriteLineAsync("Milestone: 0.0.10d Phase 6").ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync($"Expected-Failure-Source: {expectedSource}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Detected-Failure-Source: {detectedSource}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Failure-Record-Validated: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Child-Exit-Code-Available: {child?.ChildExitCodeAvailable ?? false}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Child-Exit-Code-Signed: " +
                (child?.ChildExitCode ?? 0).ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Child-Exit-Code-Hex: {unchecked((uint)(child?.ChildExitCode ?? 0)):X8}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Stdout-Bytes: " +
                (child?.StdoutByteCount ?? 0).ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Stderr-Bytes: " +
                (child?.StderrByteCount ?? 0).ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Last-Native-Audio-Stage: {child?.LastNativeAudioStage ?? "None"}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Controlled-Run-Result: {child?.ControlledRunResult ?? "None"}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Child-Process-Result-Captured: {child?.ChildProcessResultCaptured ?? false}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Helper-Failure-Phase: {helper?.FailurePhase ?? PlaybackControlledWindowsAudioRenderHelperFailurePhase.None}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Helper-Failure-Category: {helper?.FailureCategory ?? PlaybackControlledWindowsAudioRenderHelperFailureCategory.None}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Canonical-Render-Evidence-Record-Produced: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Canonical-Terminal-Render-Record-Produced: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Run-Completion-Established: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Audio-Acceptance-Claimed: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("External-Binary-Or-Payload-Published: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Runs-Executed-By-This-Command: False")
            .ConfigureAwait(false);

        return new(
            validated ? ValidatedExitCode : ValidationFailureExitCode,
            result,
            detectedSource,
            validated);
    }

    private static bool TryParse(string[] arguments, out Options options)
    {
        options = default;
        if (arguments.Length != 5 ||
            Count(arguments, Argument) != 1 ||
            !TryGetOne(arguments, "--record", out var recordPath) ||
            !TryGetOne(arguments, "--expected-source", out var expectedSourceText) ||
            !IsValidPath(recordPath) ||
            !TryParseSource(expectedSourceText, out var expectedSource))
        {
            return false;
        }

        options = new(recordPath, expectedSource);
        return true;
    }

    private static bool TryParseSource(
        string value,
        out PlaybackControlledWindowsAudioRenderFailureRecordSource source)
    {
        if (string.Equals(value, "child", StringComparison.OrdinalIgnoreCase))
        {
            source = PlaybackControlledWindowsAudioRenderFailureRecordSource.ChildCapture;
            return true;
        }
        if (string.Equals(value, "helper", StringComparison.OrdinalIgnoreCase))
        {
            source = PlaybackControlledWindowsAudioRenderFailureRecordSource.Helper;
            return true;
        }

        source = PlaybackControlledWindowsAudioRenderFailureRecordSource.None;
        return false;
    }

    private static bool TryGetOne(
        string[] arguments,
        string name,
        out string value)
    {
        value = string.Empty;
        var matches = new List<string>();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(arguments[index + 1]);
            }
        }

        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0]))
        {
            return false;
        }

        value = matches[0];
        return true;
    }

    private static int Count(string[] arguments, string value) =>
        arguments.Count(argument => string.Equals(
            argument,
            value,
            StringComparison.OrdinalIgnoreCase));

    private static bool IsValidPath(string value) =>
        value.Length <= 32_767 && !value.Contains('\0');

    private readonly record struct Options(
        string RecordPath,
        PlaybackControlledWindowsAudioRenderFailureRecordSource ExpectedSource);
}

internal static class PlaybackControlledWindowsAudioRenderFailureRecordText
{
    internal static bool TryGetLines(
        string? text,
        int maximumCharacters,
        int expectedLineCount,
        out string normalized,
        out string[] lines)
    {
        normalized = string.Empty;
        lines = [];
        if (string.IsNullOrEmpty(text) || text.Length > maximumCharacters)
        {
            return false;
        }

        normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }
        lines = normalized.Split('\n');
        return lines.Length == expectedLineCount;
    }

    internal static bool TryReadExact(
        string line,
        string prefix,
        out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[prefix.Length..];
        return value.Length > 0;
    }

    internal static bool TryReadBoolean(
        string line,
        string prefix,
        out bool value)
    {
        value = false;
        if (!TryReadExact(line, prefix, out var text))
        {
            return false;
        }
        if (string.Equals(text, "True", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }
        return string.Equals(text, "False", StringComparison.Ordinal);
    }

    internal static bool TryReadInt32(
        string line,
        string prefix,
        out int value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            int.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
    }

    internal static bool TryReadInt64(
        string line,
        string prefix,
        out long value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadHexUInt32(
        string line,
        string prefix,
        out uint value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            text.Length == 8 &&
            uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadEnum<TEnum>(
        string line,
        string prefix,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        return TryReadExact(line, prefix, out var text) &&
            Enum.TryParse(text, ignoreCase: false, out value) &&
            Enum.IsDefined(value);
    }
}

using System.Globalization;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public sealed record PlaybackControlledWindowsAudioRenderReadinessRecord(
    int SchemaVersion,
    string Milestone,
    string ValidatedCommit,
    string ExpectedRuntimeBuild,
    string FixtureSha256,
    long FixtureBytes,
    int MaximumDecodeOperations,
    int MaximumDeliveryOperations,
    int FixturePacketBytes,
    int ObservationHoldMilliseconds,
    int AudioBufferMilliseconds);

public static class PlaybackControlledWindowsAudioRenderReadinessRecordCodec
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMilestone = "0.0.10d Phase 7";
    public const int MaximumRecordCharacterCount = 32 * 1024;
    public const string ApprovedFixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";
    public const long ApprovedFixtureBytes = 66_939;
    public const int ApprovedAudioBufferMilliseconds = 100;

    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS 1";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS";

    public static string Serialize(
        PlaybackControlledWindowsAudioRenderReadinessRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsValid(record))
        {
            throw new ArgumentException(
                "The controlled audio render readiness record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(1536);
        Append(builder, Header);
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: ReadyForSeparateAuthorization");
        Append(builder, $"Validated-Commit: {record.ValidatedCommit}");
        Append(builder, $"Expected-Runtime-Build: {record.ExpectedRuntimeBuild}");
        Append(builder, "Runtime-Directory-Present: True");
        Append(builder, $"Fixture-SHA256: {record.FixtureSha256}");
        Append(
            builder,
            $"Fixture-Bytes: {record.FixtureBytes.ToString(CultureInfo.InvariantCulture)}");
        Append(builder, "Video-Record-Gate: Accepted");
        Append(
            builder,
            "Maximum-Decode-Operations: " +
            record.MaximumDecodeOperations.ToString(CultureInfo.InvariantCulture));
        Append(
            builder,
            "Maximum-Delivery-Operations: " +
            record.MaximumDeliveryOperations.ToString(CultureInfo.InvariantCulture));
        Append(
            builder,
            "Fixture-Packet-Bytes: " +
            record.FixturePacketBytes.ToString(CultureInfo.InvariantCulture));
        Append(
            builder,
            "Observation-Hold-Milliseconds: " +
            record.ObservationHoldMilliseconds.ToString(CultureInfo.InvariantCulture));
        Append(
            builder,
            "Audio-Buffer-Milliseconds: " +
            record.AudioBufferMilliseconds.ToString(CultureInfo.InvariantCulture));
        Append(builder, "Completed-Record-Path-Fresh: True");
        Append(builder, "Terminal-Record-Path-Fresh: True");
        Append(builder, "Failure-Record-Path-Fresh: True");
        Append(builder, "Record-Paths-Repository-External: True");
        Append(builder, "Record-Paths-Pairwise-Distinct: True");
        Append(builder, "Runner-Build: Accepted");
        Append(builder, "Readiness-Only: True");
        Append(builder, "Physical-Run-Authorization-Granted: False");
        Append(builder, "Physical-Runs-Executed-By-This-Command: False");
        Append(builder, "Audio-Acceptance-Claimed: False");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(builder, Footer);

        if (builder.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio render readiness record exceeds its size limit.");
        }

        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioRenderReadinessRecord? record)
    {
        record = null;
        if (!TryGetLines(text, out var normalized, out var lines) ||
            lines.Length != 27 ||
            !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !TryReadExact(lines[1], "Milestone: ", out var milestone) ||
            !string.Equals(lines[2], "Kind: Audio", StringComparison.Ordinal) ||
            !string.Equals(
                lines[3],
                "Result: ReadyForSeparateAuthorization",
                StringComparison.Ordinal) ||
            !TryReadExact(lines[4], "Validated-Commit: ", out var validatedCommit) ||
            !TryReadExact(
                lines[5],
                "Expected-Runtime-Build: ",
                out var expectedRuntimeBuild) ||
            !string.Equals(
                lines[6],
                "Runtime-Directory-Present: True",
                StringComparison.Ordinal) ||
            !TryReadExact(lines[7], "Fixture-SHA256: ", out var fixtureSha256) ||
            !TryReadInt64(lines[8], "Fixture-Bytes: ", out var fixtureBytes) ||
            !string.Equals(
                lines[9],
                "Video-Record-Gate: Accepted",
                StringComparison.Ordinal) ||
            !TryReadInt32(
                lines[10],
                "Maximum-Decode-Operations: ",
                out var maximumDecodeOperations) ||
            !TryReadInt32(
                lines[11],
                "Maximum-Delivery-Operations: ",
                out var maximumDeliveryOperations) ||
            !TryReadInt32(
                lines[12],
                "Fixture-Packet-Bytes: ",
                out var fixturePacketBytes) ||
            !TryReadInt32(
                lines[13],
                "Observation-Hold-Milliseconds: ",
                out var observationHoldMilliseconds) ||
            !TryReadInt32(
                lines[14],
                "Audio-Buffer-Milliseconds: ",
                out var audioBufferMilliseconds) ||
            !string.Equals(
                lines[15],
                "Completed-Record-Path-Fresh: True",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[16],
                "Terminal-Record-Path-Fresh: True",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[17],
                "Failure-Record-Path-Fresh: True",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[18],
                "Record-Paths-Repository-External: True",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[19],
                "Record-Paths-Pairwise-Distinct: True",
                StringComparison.Ordinal) ||
            !string.Equals(lines[20], "Runner-Build: Accepted", StringComparison.Ordinal) ||
            !string.Equals(lines[21], "Readiness-Only: True", StringComparison.Ordinal) ||
            !string.Equals(
                lines[22],
                "Physical-Run-Authorization-Granted: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[23],
                "Physical-Runs-Executed-By-This-Command: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[24],
                "Audio-Acceptance-Claimed: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[25],
                "External-Binary-Or-Payload-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[26], Footer, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsAudioRenderReadinessRecord(
            CurrentSchemaVersion,
            milestone,
            validatedCommit,
            expectedRuntimeBuild,
            fixtureSha256,
            fixtureBytes,
            maximumDecodeOperations,
            maximumDeliveryOperations,
            fixturePacketBytes,
            observationHoldMilliseconds,
            audioBufferMilliseconds);
        if (!IsValid(candidate) ||
            !string.Equals(Serialize(candidate), normalized, StringComparison.Ordinal))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsValid(
        PlaybackControlledWindowsAudioRenderReadinessRecord record) =>
        record.SchemaVersion == CurrentSchemaVersion &&
        string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
        IsLowerHex(record.ValidatedCommit, 40) &&
        string.Equals(
            record.ExpectedRuntimeBuild,
            PlaybackFfmpegBackendCandidate.BuildId,
            StringComparison.Ordinal) &&
        string.Equals(
            record.FixtureSha256,
            ApprovedFixtureSha256,
            StringComparison.Ordinal) &&
        record.FixtureBytes == ApprovedFixtureBytes &&
        record.MaximumDecodeOperations is >= 4 and <= 4096 &&
        record.MaximumDeliveryOperations is >= 4 and <= 4096 &&
        record.FixturePacketBytes is >= 1 and <= 1_048_576 &&
        record.ObservationHoldMilliseconds is >= 0 and <= 30_000 &&
        record.AudioBufferMilliseconds == ApprovedAudioBufferMilliseconds;

    private static bool TryGetLines(
        string? text,
        out string normalized,
        out string[] lines)
    {
        normalized = string.Empty;
        lines = [];
        if (string.IsNullOrEmpty(text) ||
            text.Length > MaximumRecordCharacterCount ||
            text[0] == '\uFEFF' ||
            text.Contains('\0'))
        {
            return false;
        }

        normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Contains('\r') ||
            !normalized.EndsWith('\n') ||
            normalized.Length <= 1)
        {
            return false;
        }

        lines = normalized[..^1].Split('\n');
        return lines.All(line => line.Length > 0);
    }

    private static bool TryReadExact(
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

    private static bool TryReadInt32(
        string line,
        string prefix,
        out int value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt64(
        string line,
        string prefix,
        out long value)
    {
        value = 0;
        return TryReadExact(line, prefix, out var text) &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

public enum PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
{
    None,
    ReadinessReportValidated,
    InvalidConfiguration,
    InvalidRecord,
    IdentityMismatch,
}

public readonly record struct
    PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommandResult(
        int ExitCode,
        PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult Result,
        bool Validated);

/// <summary>
/// Revalidates one retained non-physical readiness report without starting a
/// runner, loading a runtime, replaying a fixture, or producing physical output.
/// </summary>
public static class PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommand
{
    public const string Argument =
        "--verify-controlled-windows-audio-render-readiness-record";

    private const int InvalidConfigurationExitCode = 2;
    private const int ValidationFailureExitCode = 1;
    private const int ValidatedExitCode = 0;

    public static async ValueTask<
        PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommandResult> RunAsync(
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
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                    .InvalidConfiguration,
                Validated: false);
        }

        var text = await PlaybackControlledWindowsPhysicalRunRecordFile.ReadBoundedAsync(
                options.RecordPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!PlaybackControlledWindowsAudioRenderReadinessRecordCodec.TryParse(
                text,
                out var record) ||
            record is null)
        {
            return await WriteReportAsync(
                    output,
                    options,
                    PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                        .InvalidRecord,
                    validated: false)
                .ConfigureAwait(false);
        }

        if (!string.Equals(
                record.ValidatedCommit,
                options.ExpectedCommit,
                StringComparison.Ordinal) ||
            !string.Equals(
                record.FixtureSha256,
                options.FixtureSha256,
                StringComparison.Ordinal) ||
            record.MaximumDecodeOperations != options.MaximumDecodeOperations ||
            record.MaximumDeliveryOperations != options.MaximumDeliveryOperations ||
            record.FixturePacketBytes != options.FixturePacketBytes ||
            record.ObservationHoldMilliseconds != options.ObservationHoldMilliseconds ||
            record.AudioBufferMilliseconds != options.AudioBufferMilliseconds)
        {
            return await WriteReportAsync(
                    output,
                    options,
                    PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                        .IdentityMismatch,
                    validated: false)
                .ConfigureAwait(false);
        }

        return await WriteReportAsync(
                output,
                options,
                PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult
                    .ReadinessReportValidated,
                validated: true)
            .ConfigureAwait(false);
    }

    private static async ValueTask<
        PlaybackControlledWindowsAudioRenderReadinessRecordPreflightCommandResult>
        WriteReportAsync(
            TextWriter output,
            Options options,
            PlaybackControlledWindowsAudioRenderReadinessRecordPreflightResult result,
            bool validated)
    {
        await output.WriteLineAsync(
                $"Milestone: {PlaybackControlledWindowsAudioRenderReadinessRecordCodec.CurrentMilestone}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync($"Validated-Commit: {options.ExpectedCommit}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Expected-Runtime-Build: {PlaybackFfmpegBackendCandidate.BuildId}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Fixture-SHA256: {options.FixtureSha256}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Fixture-Bytes: " +
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec.ApprovedFixtureBytes
                    .ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Maximum-Decode-Operations: " +
                options.MaximumDecodeOperations.ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Maximum-Delivery-Operations: " +
                options.MaximumDeliveryOperations.ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Fixture-Packet-Bytes: " +
                options.FixturePacketBytes.ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Observation-Hold-Milliseconds: " +
                options.ObservationHoldMilliseconds.ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Audio-Buffer-Milliseconds: " +
                options.AudioBufferMilliseconds.ToString(CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Readiness-Report-Validated: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Ready-For-Separate-Authorization: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Run-Authorization-Granted: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Runs-Executed-By-This-Command: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Audio-Acceptance-Claimed: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("External-Binary-Or-Payload-Published: False")
            .ConfigureAwait(false);

        return new(
            validated ? ValidatedExitCode : ValidationFailureExitCode,
            result,
            validated);
    }

    private static bool TryParse(string[] arguments, out Options options)
    {
        options = default;
        if (arguments.Length != 17 ||
            Count(arguments, Argument) != 1 ||
            !TryGetOne(arguments, "--record", out var recordPath) ||
            !TryGetOne(arguments, "--expected-commit", out var expectedCommitText) ||
            !TryGetOne(arguments, "--fixture-sha256", out var fixtureSha256Text) ||
            !TryGetOne(
                arguments,
                "--max-decode-operations",
                out var maximumDecodeText) ||
            !TryGetOne(
                arguments,
                "--max-delivery-operations",
                out var maximumDeliveryText) ||
            !TryGetOne(arguments, "--fixture-packet-bytes", out var fixturePacketText) ||
            !TryGetOne(
                arguments,
                "--observation-hold-ms",
                out var observationHoldText) ||
            !TryGetOne(arguments, "--audio-buffer-ms", out var audioBufferText) ||
            !IsValidPath(recordPath) ||
            !TryNormalizeCommit(expectedCommitText, out var expectedCommit) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                fixtureSha256Text,
                out var fixtureSha256) ||
            !TryParseBounded(maximumDecodeText, 4, 4096, out var maximumDecode) ||
            !TryParseBounded(maximumDeliveryText, 4, 4096, out var maximumDelivery) ||
            !TryParseBounded(fixturePacketText, 1, 1_048_576, out var fixturePacketBytes) ||
            !TryParseBounded(observationHoldText, 0, 30_000, out var observationHold) ||
            !TryParseBounded(
                audioBufferText,
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec
                    .ApprovedAudioBufferMilliseconds,
                PlaybackControlledWindowsAudioRenderReadinessRecordCodec
                    .ApprovedAudioBufferMilliseconds,
                out var audioBuffer))
        {
            return false;
        }

        options = new(
            recordPath,
            expectedCommit,
            fixtureSha256,
            maximumDecode,
            maximumDelivery,
            fixturePacketBytes,
            observationHold,
            audioBuffer);
        return true;
    }

    private static bool TryGetOne(
        string[] arguments,
        string name,
        out string value)
    {
        value = string.Empty;
        var matchCount = 0;
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchCount++;
            value = arguments[index + 1];
        }

        return matchCount == 1 && !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryNormalizeCommit(string value, out string normalized)
    {
        normalized = value.ToLowerInvariant();
        return normalized.Length == 40 &&
            normalized.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool TryParseBounded(
        string text,
        int minimum,
        int maximum,
        out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
        value >= minimum &&
        value <= maximum;

    private static int Count(string[] arguments, string value) =>
        arguments.Count(argument => string.Equals(
            argument,
            value,
            StringComparison.OrdinalIgnoreCase));

    private static bool IsValidPath(string value) =>
        value.Length <= 32_767 && !value.Contains('\0');

    private readonly record struct Options(
        string RecordPath,
        string ExpectedCommit,
        string FixtureSha256,
        int MaximumDecodeOperations,
        int MaximumDeliveryOperations,
        int FixturePacketBytes,
        int ObservationHoldMilliseconds,
        int AudioBufferMilliseconds);
}

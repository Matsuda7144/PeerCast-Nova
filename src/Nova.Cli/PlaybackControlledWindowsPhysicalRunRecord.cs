using System.Globalization;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public sealed record PlaybackControlledWindowsPhysicalRunRecord(
    int SchemaVersion,
    string Milestone,
    PlaybackControlledWindowsOutputValidationKind Kind,
    string RuntimeBuildId,
    string FixtureSha256,
    PlaybackControlledWindowsPhysicalRunResult Result,
    PlaybackControlledWindowsOutputEvidenceResult EvidenceResult,
    long FixtureByteCount,
    int FixturePacketCount,
    int DecodeOperationCount,
    int DeliveryAttemptCount,
    int DeliveredCount,
    int RetainedWouldBlockCount,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    PlaybackApprovedExternalRuntimeWindowsLifecycleState LifecycleState,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved,
    bool ExternalBinaryOrPayloadPublished);

/// <summary>
/// Canonical bounded text codec for one sanitized independent physical run.
/// The record contains no local path, device identity, native handle, packet,
/// decoded payload, screenshot, or operator identity.
/// </summary>
public static class PlaybackControlledWindowsPhysicalRunRecordCodec
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMilestone = "0.0.10d Phase 5";
    public const int MaximumRecordCharacterCount = 16 * 1024;
    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD 1";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD";

    public static PlaybackControlledWindowsPhysicalRunRecord Create(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        string expectedFixtureSha256)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryNormalizeSha256(expectedFixtureSha256, out var normalizedDigest))
        {
            throw new ArgumentException(
                "The expected fixture digest must be a 64-character SHA-256 value.",
                nameof(expectedFixtureSha256));
        }

        return new(
            CurrentSchemaVersion,
            CurrentMilestone,
            snapshot.Kind,
            PlaybackFfmpegBackendCandidate.BuildId,
            normalizedDigest,
            snapshot.Result,
            snapshot.EvidenceResult,
            snapshot.FixtureByteCount,
            snapshot.FixturePacketCount,
            snapshot.DecodeOperationCount,
            snapshot.DeliveryAttemptCount,
            snapshot.DeliveredCount,
            snapshot.RetainedWouldBlockCount,
            snapshot.RunnerState,
            snapshot.LifecycleState,
            snapshot.BorrowedWindowPreserved,
            snapshot.BorrowedDispatcherPreserved,
            ExternalBinaryOrPayloadPublished: false);
    }

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsPhysicalRunRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(Serialize(record).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }

    public static string Serialize(PlaybackControlledWindowsPhysicalRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsSyntacticallyValid(record, out var normalizedDigest))
        {
            throw new ArgumentException(
                "The controlled Windows run record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(capacity: 1024);
        Append(builder, Header);
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, $"Kind: {record.Kind}");
        Append(builder, $"Runtime-Build: {record.RuntimeBuildId}");
        Append(builder, $"Fixture-SHA256: {normalizedDigest}");
        Append(builder, $"Result: {record.Result}");
        Append(builder, $"Evidence: {record.EvidenceResult}");
        Append(builder, Format("Fixture-Bytes", record.FixtureByteCount));
        Append(builder, Format("Fixture-Packets", record.FixturePacketCount));
        Append(builder, Format("Decode-Operations", record.DecodeOperationCount));
        Append(builder, Format("Delivery-Attempts", record.DeliveryAttemptCount));
        Append(builder, Format("Delivered", record.DeliveredCount));
        Append(builder, Format("Retained-WouldBlock", record.RetainedWouldBlockCount));
        Append(builder, $"Runner-State: {record.RunnerState}");
        Append(builder, $"Lifecycle-State: {record.LifecycleState}");
        Append(builder, $"Borrowed-Window-Preserved: {record.BorrowedWindowPreserved}");
        Append(
            builder,
            $"Borrowed-Dispatcher-Preserved: {record.BorrowedDispatcherPreserved}");
        Append(
            builder,
            $"External-Binary-Or-Payload-Published: " +
            record.ExternalBinaryOrPayloadPublished);
        Append(builder, Footer);
        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsPhysicalRunRecord? record)
    {
        record = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumRecordCharacterCount)
        {
            return false;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        var lines = normalized.Split('\n');
        if (lines.Length != 19 ||
            !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !string.Equals(lines[18], Footer, StringComparison.Ordinal) ||
            !TryRead(lines[1], "Milestone: ", out var milestone) ||
            !TryRead(lines[2], "Kind: ", out var kindText) ||
            !TryRead(lines[3], "Runtime-Build: ", out var runtimeBuildId) ||
            !TryRead(lines[4], "Fixture-SHA256: ", out var fixtureSha256) ||
            !TryRead(lines[5], "Result: ", out var resultText) ||
            !TryRead(lines[6], "Evidence: ", out var evidenceText) ||
            !TryRead(lines[7], "Fixture-Bytes: ", out var fixtureBytesText) ||
            !TryRead(lines[8], "Fixture-Packets: ", out var fixturePacketsText) ||
            !TryRead(lines[9], "Decode-Operations: ", out var decodeOperationsText) ||
            !TryRead(lines[10], "Delivery-Attempts: ", out var deliveryAttemptsText) ||
            !TryRead(lines[11], "Delivered: ", out var deliveredText) ||
            !TryRead(
                lines[12],
                "Retained-WouldBlock: ",
                out var retainedWouldBlockText) ||
            !TryRead(lines[13], "Runner-State: ", out var runnerStateText) ||
            !TryRead(lines[14], "Lifecycle-State: ", out var lifecycleStateText) ||
            !TryRead(
                lines[15],
                "Borrowed-Window-Preserved: ",
                out var windowText) ||
            !TryRead(
                lines[16],
                "Borrowed-Dispatcher-Preserved: ",
                out var dispatcherText) ||
            !TryRead(
                lines[17],
                "External-Binary-Or-Payload-Published: ",
                out var publishedText) ||
            !TryParseEnum(kindText, out PlaybackControlledWindowsOutputValidationKind kind) ||
            !TryParseEnum(resultText, out PlaybackControlledWindowsPhysicalRunResult result) ||
            !TryParseEnum(
                evidenceText,
                out PlaybackControlledWindowsOutputEvidenceResult evidence) ||
            !TryParseEnum(
                runnerStateText,
                out PlaybackWindowsPhysicalRunnerState runnerState) ||
            !TryParseEnum(
                lifecycleStateText,
                out PlaybackApprovedExternalRuntimeWindowsLifecycleState lifecycleState) ||
            !TryParseInt64(fixtureBytesText, out var fixtureBytes) ||
            !TryParseInt32(fixturePacketsText, out var fixturePackets) ||
            !TryParseInt32(decodeOperationsText, out var decodeOperations) ||
            !TryParseInt32(deliveryAttemptsText, out var deliveryAttempts) ||
            !TryParseInt32(deliveredText, out var delivered) ||
            !TryParseInt32(retainedWouldBlockText, out var retainedWouldBlock) ||
            !bool.TryParse(windowText, out var windowPreserved) ||
            !bool.TryParse(dispatcherText, out var dispatcherPreserved) ||
            !bool.TryParse(publishedText, out var published) ||
            !TryNormalizeSha256(fixtureSha256, out var normalizedDigest))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsPhysicalRunRecord(
            CurrentSchemaVersion,
            milestone,
            kind,
            runtimeBuildId,
            normalizedDigest,
            result,
            evidence,
            fixtureBytes,
            fixturePackets,
            decodeOperations,
            deliveryAttempts,
            delivered,
            retainedWouldBlock,
            runnerState,
            lifecycleState,
            windowPreserved,
            dispatcherPreserved,
            published);
        if (!IsSyntacticallyValid(candidate, out _))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    internal static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        normalized = value.ToUpperInvariant();
        return true;
    }

    private static bool IsSyntacticallyValid(
        PlaybackControlledWindowsPhysicalRunRecord record,
        out string normalizedDigest)
    {
        normalizedDigest = string.Empty;
        return record.SchemaVersion == CurrentSchemaVersion &&
            string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
            record.Kind is PlaybackControlledWindowsOutputValidationKind.Video or
                PlaybackControlledWindowsOutputValidationKind.Audio &&
            !string.IsNullOrWhiteSpace(record.RuntimeBuildId) &&
            record.RuntimeBuildId.Length <= 256 &&
            !record.RuntimeBuildId.Contains('\0') &&
            TryNormalizeSha256(record.FixtureSha256, out normalizedDigest) &&
            Enum.IsDefined(record.Result) &&
            Enum.IsDefined(record.EvidenceResult) &&
            Enum.IsDefined(record.RunnerState) &&
            Enum.IsDefined(record.LifecycleState) &&
            record.FixtureByteCount >= 0 &&
            record.FixturePacketCount >= 0 &&
            record.DecodeOperationCount >= 0 &&
            record.DeliveryAttemptCount >= 0 &&
            record.DeliveredCount >= 0 &&
            record.RetainedWouldBlockCount >= 0;
    }

    private static bool TryRead(string line, string prefix, out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[prefix.Length..];
        return value.Length > 0;
    }

    private static bool TryParseEnum<T>(string value, out T result)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out result) && Enum.IsDefined(result);

    private static bool TryParseInt32(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static bool TryParseInt64(string value, out long result) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static string Format(string name, long value) =>
        $"{name}: {value.ToString(CultureInfo.InvariantCulture)}";

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

using System.Globalization;
using System.Text;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public sealed record PlaybackControlledWindowsDeliveryDiagnosticRecord(
    int SchemaVersion,
    string Milestone,
    PlaybackControlledWindowsOutputValidationKind Kind,
    string RuntimeBuildId,
    string FixtureSha256,
    PlaybackControlledWindowsPhysicalRunResult RunResult,
    PlaybackControlledWindowsOutputEvidenceResult EvidenceResult,
    long FixtureByteCount,
    int FixturePacketCount,
    int DecodeOperationCount,
    int DeliveryAttemptCount,
    int DeliveredCount,
    int RetainedWouldBlockCount,
    IReadOnlyList<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliveryResults,
    bool DeliveryResultOverflowed,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    PlaybackApprovedExternalRuntimeWindowsLifecycleState LifecycleState,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved,
    bool ExternalBinaryOrPayloadPublished,
    bool PhysicalRunsExecutedByThisCommand,
    bool AudioAcceptanceClaimed);

public static class PlaybackControlledWindowsDeliveryDiagnosticRecordCodec
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMilestone = "0.0.10d Phase 5";
    public const int MaximumDeliveryResultCount = 4;
    public const int MaximumRecordCharacterCount = 16 * 1024;
    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-DELIVERY-DIAGNOSTIC 1";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-DELIVERY-DIAGNOSTIC";

    public static PlaybackControlledWindowsDeliveryDiagnosticRecord Create(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        string expectedFixtureSha256,
        IReadOnlyList<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> deliveryResults,
        bool deliveryResultOverflowed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(deliveryResults);
        if (!PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                expectedFixtureSha256,
                out var normalizedDigest))
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
            deliveryResults.ToArray(),
            deliveryResultOverflowed,
            snapshot.RunnerState,
            snapshot.LifecycleState,
            snapshot.BorrowedWindowPreserved,
            snapshot.BorrowedDispatcherPreserved,
            ExternalBinaryOrPayloadPublished: false,
            PhysicalRunsExecutedByThisCommand: true,
            AudioAcceptanceClaimed: false);
    }

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsDeliveryDiagnosticRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(Serialize(record).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }

    public static string Serialize(PlaybackControlledWindowsDeliveryDiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsValid(record, out var normalizedDigest))
        {
            throw new ArgumentException(
                "The controlled Windows delivery diagnostic record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(1536);
        Append(builder, Header);
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, $"Kind: {record.Kind}");
        Append(builder, $"Runtime-Build: {record.RuntimeBuildId}");
        Append(builder, $"Fixture-SHA256: {normalizedDigest}");
        Append(builder, $"Run-Result: {record.RunResult}");
        Append(builder, $"Evidence: {record.EvidenceResult}");
        Append(builder, Format("Fixture-Bytes", record.FixtureByteCount));
        Append(builder, Format("Fixture-Packets", record.FixturePacketCount));
        Append(builder, Format("Decode-Operations", record.DecodeOperationCount));
        Append(builder, Format("Delivery-Attempts", record.DeliveryAttemptCount));
        Append(builder, Format("Delivered", record.DeliveredCount));
        Append(builder, Format("Retained-WouldBlock", record.RetainedWouldBlockCount));
        Append(builder, Format("Delivery-Result-Count", record.DeliveryResults.Count));
        for (var index = 0; index < MaximumDeliveryResultCount; index++)
        {
            var result = index < record.DeliveryResults.Count
                ? record.DeliveryResults[index]
                : PlaybackApprovedExternalRuntimeWindowsLifecycleResult.None;
            Append(builder, $"Delivery-Result-{index + 1}: {result}");
        }
        Append(builder, $"Delivery-Result-Overflowed: {record.DeliveryResultOverflowed}");
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
        Append(
            builder,
            $"Physical-Runs-Executed-By-This-Command: " +
            record.PhysicalRunsExecutedByThisCommand);
        Append(builder, $"Audio-Acceptance-Claimed: {record.AudioAcceptanceClaimed}");
        Append(builder, Footer);
        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsDeliveryDiagnosticRecord? record)
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
        if (lines.Length != 27 ||
            !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !string.Equals(lines[26], Footer, StringComparison.Ordinal) ||
            !TryRead(lines[1], "Milestone: ", out var milestone) ||
            !TryRead(lines[2], "Kind: ", out var kindText) ||
            !TryRead(lines[3], "Runtime-Build: ", out var runtimeBuildId) ||
            !TryRead(lines[4], "Fixture-SHA256: ", out var fixtureSha256) ||
            !TryRead(lines[5], "Run-Result: ", out var runResultText) ||
            !TryRead(lines[6], "Evidence: ", out var evidenceText) ||
            !TryRead(lines[7], "Fixture-Bytes: ", out var fixtureBytesText) ||
            !TryRead(lines[8], "Fixture-Packets: ", out var fixturePacketsText) ||
            !TryRead(lines[9], "Decode-Operations: ", out var decodeOperationsText) ||
            !TryRead(lines[10], "Delivery-Attempts: ", out var deliveryAttemptsText) ||
            !TryRead(lines[11], "Delivered: ", out var deliveredText) ||
            !TryRead(lines[12], "Retained-WouldBlock: ", out var retainedWouldBlockText) ||
            !TryRead(lines[13], "Delivery-Result-Count: ", out var resultCountText) ||
            !TryRead(lines[14], "Delivery-Result-1: ", out var result1Text) ||
            !TryRead(lines[15], "Delivery-Result-2: ", out var result2Text) ||
            !TryRead(lines[16], "Delivery-Result-3: ", out var result3Text) ||
            !TryRead(lines[17], "Delivery-Result-4: ", out var result4Text) ||
            !TryRead(lines[18], "Delivery-Result-Overflowed: ", out var overflowText) ||
            !TryRead(lines[19], "Runner-State: ", out var runnerStateText) ||
            !TryRead(lines[20], "Lifecycle-State: ", out var lifecycleStateText) ||
            !TryRead(lines[21], "Borrowed-Window-Preserved: ", out var windowText) ||
            !TryRead(
                lines[22],
                "Borrowed-Dispatcher-Preserved: ",
                out var dispatcherText) ||
            !TryRead(
                lines[23],
                "External-Binary-Or-Payload-Published: ",
                out var publishedText) ||
            !TryRead(
                lines[24],
                "Physical-Runs-Executed-By-This-Command: ",
                out var executedText) ||
            !TryRead(lines[25], "Audio-Acceptance-Claimed: ", out var acceptanceText) ||
            !TryParseEnum(kindText, out PlaybackControlledWindowsOutputValidationKind kind) ||
            !TryParseEnum(runResultText, out PlaybackControlledWindowsPhysicalRunResult runResult) ||
            !TryParseEnum(
                evidenceText,
                out PlaybackControlledWindowsOutputEvidenceResult evidence) ||
            !TryParseEnum(
                runnerStateText,
                out PlaybackWindowsPhysicalRunnerState runnerState) ||
            !TryParseEnum(
                lifecycleStateText,
                out PlaybackApprovedExternalRuntimeWindowsLifecycleState lifecycleState) ||
            !TryParseEnum(
                result1Text,
                out PlaybackApprovedExternalRuntimeWindowsLifecycleResult result1) ||
            !TryParseEnum(
                result2Text,
                out PlaybackApprovedExternalRuntimeWindowsLifecycleResult result2) ||
            !TryParseEnum(
                result3Text,
                out PlaybackApprovedExternalRuntimeWindowsLifecycleResult result3) ||
            !TryParseEnum(
                result4Text,
                out PlaybackApprovedExternalRuntimeWindowsLifecycleResult result4) ||
            !TryParseInt64(fixtureBytesText, out var fixtureBytes) ||
            !TryParseInt32(fixturePacketsText, out var fixturePackets) ||
            !TryParseInt32(decodeOperationsText, out var decodeOperations) ||
            !TryParseInt32(deliveryAttemptsText, out var deliveryAttempts) ||
            !TryParseInt32(deliveredText, out var delivered) ||
            !TryParseInt32(retainedWouldBlockText, out var retainedWouldBlock) ||
            !TryParseInt32(resultCountText, out var resultCount) ||
            !bool.TryParse(overflowText, out var overflowed) ||
            !bool.TryParse(windowText, out var windowPreserved) ||
            !bool.TryParse(dispatcherText, out var dispatcherPreserved) ||
            !bool.TryParse(publishedText, out var published) ||
            !bool.TryParse(executedText, out var executed) ||
            !bool.TryParse(acceptanceText, out var acceptanceClaimed) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                fixtureSha256,
                out var normalizedDigest) ||
            resultCount is < 0 or > MaximumDeliveryResultCount)
        {
            return false;
        }

        var allResults = new[] { result1, result2, result3, result4 };
        var deliveryResults = allResults.Take(resultCount).ToArray();
        if (allResults.Skip(resultCount).Any(
                value => value != PlaybackApprovedExternalRuntimeWindowsLifecycleResult.None))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsDeliveryDiagnosticRecord(
            CurrentSchemaVersion,
            milestone,
            kind,
            runtimeBuildId,
            normalizedDigest,
            runResult,
            evidence,
            fixtureBytes,
            fixturePackets,
            decodeOperations,
            deliveryAttempts,
            delivered,
            retainedWouldBlock,
            deliveryResults,
            overflowed,
            runnerState,
            lifecycleState,
            windowPreserved,
            dispatcherPreserved,
            published,
            executed,
            acceptanceClaimed);
        if (!IsValid(candidate, out _))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsValid(
        PlaybackControlledWindowsDeliveryDiagnosticRecord record,
        out string normalizedDigest)
    {
        normalizedDigest = string.Empty;
        return record.SchemaVersion == CurrentSchemaVersion &&
            string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
            record.Kind == PlaybackControlledWindowsOutputValidationKind.Audio &&
            !string.IsNullOrWhiteSpace(record.RuntimeBuildId) &&
            record.RuntimeBuildId.Length <= 256 &&
            !record.RuntimeBuildId.Contains('\0') &&
            PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                record.FixtureSha256,
                out normalizedDigest) &&
            Enum.IsDefined(record.RunResult) &&
            Enum.IsDefined(record.EvidenceResult) &&
            record.FixtureByteCount >= 0 &&
            record.FixturePacketCount >= 0 &&
            record.DecodeOperationCount >= 0 &&
            record.DeliveryAttemptCount >= 0 &&
            record.DeliveredCount >= 0 &&
            record.RetainedWouldBlockCount >= 0 &&
            record.DeliveryResults is not null &&
            record.DeliveryResults.Count <= MaximumDeliveryResultCount &&
            record.DeliveryResults.All(Enum.IsDefined) &&
            record.DeliveryAttemptCount == record.DeliveryResults.Count &&
            Enum.IsDefined(record.RunnerState) &&
            Enum.IsDefined(record.LifecycleState) &&
            !record.ExternalBinaryOrPayloadPublished &&
            record.PhysicalRunsExecutedByThisCommand &&
            !record.AudioAcceptanceClaimed;
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

internal sealed class PlaybackControlledWindowsDeliveryDiagnosticOperations(
    IPlaybackControlledWindowsPhysicalRunOperations inner) :
    IPlaybackControlledWindowsPhysicalRunOperations
{
    private readonly object _sync = new();
    private readonly List<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> _deliveryResults = [];
    private bool _deliveryResultOverflowed;

    internal IReadOnlyList<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliveryResults
    {
        get
        {
            lock (_sync)
            {
                return _deliveryResults.ToArray();
            }
        }
    }

    internal bool DeliveryResultOverflowed
    {
        get
        {
            lock (_sync)
            {
                return _deliveryResultOverflowed;
            }
        }
    }

    public PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot => inner.Snapshot;

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
        CancellationToken token) => inner.StartRunnerAsync(token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        InitializeLifecycleAsync(CancellationToken token) =>
        inner.InitializeLifecycleAsync(token);

    public ValueTask<PcpControlledFlvFixtureReplayReport> ReplayFixtureAsync(
        CancellationToken token) => inner.ReplayFixtureAsync(token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> CompleteInputAsync(
        CancellationToken token) => inner.CompleteInputAsync(token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind kind,
        CancellationToken token) => inner.ReceiveDecodedAsync(kind, token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind kind,
        CancellationToken token) => inner.DrainDecodedAsync(kind, token);

    public async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
        CancellationToken token)
    {
        var result = await inner.DeliverOneAsync(token).ConfigureAwait(false);
        lock (_sync)
        {
            if (_deliveryResults.Count <
                PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.MaximumDeliveryResultCount)
            {
                _deliveryResults.Add(result);
            }
            else
            {
                _deliveryResultOverflowed = true;
            }
        }
        return result;
    }

    public ValueTask StopLifecycleAsync() => inner.StopLifecycleAsync();

    public ValueTask<PlaybackControlledWindowsPhysicalRunPreservation> VerifyBorrowedOwnersAsync(
        CancellationToken token) => inner.VerifyBorrowedOwnersAsync(token);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync() =>
        inner.StopRunnerAsync();

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

public readonly record struct PlaybackControlledWindowsDeliveryDiagnosticCommandResult(
    int ExitCode,
    PlaybackControlledWindowsPhysicalRunResult RunResult);

public static class PlaybackControlledWindowsDeliveryDiagnosticCommand
{
    public const string Argument = "--controlled-windows-delivery-diagnostic";
    public const int DiagnosticRecordProducedExitCode = 0;
    public const int DiagnosticRecordProducedWithRunFailureExitCode = 1;
    public const int InvalidConfigurationExitCode = 2;

    public static ValueTask<PlaybackControlledWindowsDeliveryDiagnosticCommandResult> RunAsync(
        string[] arguments,
        TextWriter output,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            arguments,
            output,
            static options => new SystemPlaybackControlledWindowsPhysicalRunOperations(options),
            cancellationToken);

    internal static async ValueTask<PlaybackControlledWindowsDeliveryDiagnosticCommandResult>
        RunAsync(
            string[] arguments,
            TextWriter output,
            Func<PlaybackControlledWindowsPhysicalRunOptions,
                IPlaybackControlledWindowsPhysicalRunOperations> createOperations,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(createOperations);
        if (arguments.Count(argument => string.Equals(
                argument,
                Argument,
                StringComparison.OrdinalIgnoreCase)) != 1 ||
            !PlaybackControlledWindowsPhysicalRunCommand.TryParse(arguments, out var options) ||
            options.Kind != PlaybackControlledWindowsOutputValidationKind.Audio)
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(
                InvalidConfigurationExitCode,
                PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration);
        }

        var diagnosticOperations = new PlaybackControlledWindowsDeliveryDiagnosticOperations(
            createOperations(options));
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            diagnosticOperations);
        var runResult = await composition.RunAsync(cancellationToken).ConfigureAwait(false);
        var record = PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.Create(
            composition.Snapshot,
            options.FixtureSha256,
            diagnosticOperations.DeliveryResults,
            diagnosticOperations.DeliveryResultOverflowed);
        await PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.WriteAsync(
                output,
                record,
                cancellationToken)
            .ConfigureAwait(false);
        return new(
            runResult == PlaybackControlledWindowsPhysicalRunResult.Completed
                ? DiagnosticRecordProducedExitCode
                : DiagnosticRecordProducedWithRunFailureExitCode,
            runResult);
    }
}

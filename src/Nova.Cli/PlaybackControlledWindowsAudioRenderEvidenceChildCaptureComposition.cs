using System.Text;

namespace PeerCast.Nova.Cli;

internal sealed class PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition
{
    private const int ExpectedAudioBufferMilliseconds = 100;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor _executor;
    private readonly IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter _promoter;
    private readonly string _runnerAssemblyPath;
    private readonly string _repositoryRoot;

    public PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
        string runnerAssemblyPath,
        string repositoryRoot)
        : this(
            executor,
            new SystemPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter(),
            runnerAssemblyPath,
            repositoryRoot)
    {
    }

    internal PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
        IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter promoter,
        string runnerAssemblyPath,
        string repositoryRoot)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _promoter = promoter ?? throw new ArgumentNullException(nameof(promoter));
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _runnerAssemblyPath = Path.GetFullPath(runnerAssemblyPath);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    public async ValueTask<int> RunAsync(
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        CancellationToken cancellationToken)
    {
        if (!IsValid(options))
        {
            return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                .InvalidConfigurationExitCode;
        }

        PlaybackControlledWindowsAudioChildProcessResult child;
        try
        {
            child = await _executor.ExecuteAsync(
                    _runnerAssemblyPath,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await RetainFailureAsync(
                    options,
                    childExitCode: null,
                    stdoutByteCount: 0,
                    stderrByteCount: 0,
                    lastNativeAudioStage: "None",
                    controlledRunResult: "None",
                    terminalRecordRejection: "None",
                    managedFailurePhase: "None",
                    childProcessResultCaptured: false)
                .ConfigureAwait(false);
        }

        string standardOutput;
        try
        {
            standardOutput = StrictUtf8.GetString(child.StandardOutput);
        }
        catch (DecoderFallbackException)
        {
            standardOutput = string.Empty;
        }
        var lastNativeAudioStage = GetLastNativeAudioStage(child);
        var controlledRunResult =
            PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .GetControlledRunResultOrNone(standardOutput);
        var terminalRecordRejection = GetTerminalRecordRejection(child);
        var managedFailurePhase = GetManagedFailurePhase(child);

        if (IsCompletedRecord(child.ExitCode, standardOutput, options))
        {
            return await PromoteOutcomeOrFailureAsync(
                    options,
                    options.EvidenceRecordPath,
                    standardOutput,
                    PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                        .EvidenceRecordProducedExitCode,
                    child)
                .ConfigureAwait(false);
        }

        if (options.TerminalRecordPath is { Length: > 0 } terminalPath &&
            IsTerminalRecord(child.ExitCode, standardOutput, options))
        {
            return await PromoteOutcomeOrFailureAsync(
                    options,
                    terminalPath,
                    standardOutput,
                    PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                        .TerminalRecordProducedExitCode,
                    child)
                .ConfigureAwait(false);
        }

        return await RetainFailureAsync(
                options,
                child.ExitCode,
                child.StandardOutput.LongLength,
                child.StandardError.LongLength,
                lastNativeAudioStage,
                controlledRunResult,
                terminalRecordRejection,
                managedFailurePhase,
                childProcessResultCaptured: true)
            .ConfigureAwait(false);
    }

    internal static string SerializeFailureRecord(
        int childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage = "None") =>
        SerializeFailureRecordCore(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentSchemaVersion,
            childExitCode,
            stdoutByteCount,
            stderrByteCount,
            lastNativeAudioStage,
            controlledRunResult: "None",
            terminalRecordRejection: "None",
            managedFailurePhase: "None",
            childProcessResultCaptured: true);

    internal static string SerializeFailureRecord(
        int? childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage,
        string controlledRunResult,
        string terminalRecordRejection,
        bool childProcessResultCaptured) =>
        SerializeFailureRecordCore(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentSchemaVersion,
            childExitCode,
            stdoutByteCount,
            stderrByteCount,
            lastNativeAudioStage,
            controlledRunResult,
            terminalRecordRejection,
            managedFailurePhase: "None",
            childProcessResultCaptured);

    internal static string SerializeFailureRecord(
        int? childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage,
        string controlledRunResult,
        string terminalRecordRejection,
        string managedFailurePhase,
        bool childProcessResultCaptured) =>
        SerializeFailureRecordCore(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec
                .ManagedFailurePhaseSchemaVersion,
            childExitCode,
            stdoutByteCount,
            stderrByteCount,
            lastNativeAudioStage,
            controlledRunResult,
            terminalRecordRejection,
            managedFailurePhase,
            childProcessResultCaptured);

    private static string SerializeFailureRecordCore(
        int schemaVersion,
        int? childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage,
        string controlledRunResult,
        string terminalRecordRejection,
        string managedFailurePhase,
        bool childProcessResultCaptured)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stdoutByteCount);
        ArgumentOutOfRangeException.ThrowIfNegative(stderrByteCount);
        var available = childExitCode.HasValue;
        var normalizedExitCode = childExitCode.GetValueOrDefault();
        return PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(
            new(
                schemaVersion,
                PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentMilestone,
                available,
                normalizedExitCode,
                stdoutByteCount,
                stderrByteCount,
                lastNativeAudioStage,
                childProcessResultCaptured)
            {
                ControlledRunResult = controlledRunResult,
                TerminalRecordRejection = terminalRecordRejection,
                ManagedFailurePhase = managedFailurePhase,
            });
    }

    private async ValueTask<int> PromoteOutcomeOrFailureAsync(
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        string destination,
        string content,
        int producedExitCode,
        PlaybackControlledWindowsAudioChildProcessResult child)
    {
        try
        {
            await _promoter.PromoteAsync(destination, content, CancellationToken.None)
                .ConfigureAwait(false);
            return producedExitCode;
        }
        catch (Exception)
        {
            return await RetainFailureAsync(
                    options,
                    child.ExitCode,
                    child.StandardOutput.LongLength,
                    child.StandardError.LongLength,
                    GetLastNativeAudioStage(child),
                    GetControlledRunResult(child),
                    GetTerminalRecordRejection(child),
                    GetManagedFailurePhase(child),
                    childProcessResultCaptured: true)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<int> RetainFailureAsync(
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        int? childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage,
        string controlledRunResult,
        string terminalRecordRejection,
        string managedFailurePhase,
        bool childProcessResultCaptured)
    {
        var failureRecord = SerializeFailureRecord(
            childExitCode,
            stdoutByteCount,
            stderrByteCount,
            lastNativeAudioStage,
            controlledRunResult,
            terminalRecordRejection,
            managedFailurePhase,
            childProcessResultCaptured);
        try
        {
            await _promoter.PromoteAsync(
                    options.FailureRecordPath,
                    failureRecord,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                .SanitizedFailureExitCode;
        }
        catch (Exception)
        {
            return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                .InvalidConfigurationExitCode;
        }
    }

    private static string GetLastNativeAudioStage(
        PlaybackControlledWindowsAudioChildProcessResult child)
    {
        try
        {
            var standardError = StrictUtf8.GetString(child.StandardError);
            return PlaybackControlledWindowsAudioProcessFailureDiagnostics.Analyze(
                    child.ExitCode,
                    standardOutput: string.Empty,
                    standardOutputByteCount: child.StandardOutput.LongLength,
                    standardError,
                    standardErrorByteCount: child.StandardError.LongLength)
                .LastNativeAudioStage;
        }
        catch (DecoderFallbackException)
        {
            return "None";
        }
    }

    private static string GetControlledRunResult(
        PlaybackControlledWindowsAudioChildProcessResult child)
    {
        try
        {
            return PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .GetControlledRunResultOrNone(StrictUtf8.GetString(child.StandardOutput));
        }
        catch (DecoderFallbackException)
        {
            return "None";
        }
    }

    private static string GetTerminalRecordRejection(
        PlaybackControlledWindowsAudioChildProcessResult child)
    {
        try
        {
            return PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .GetTerminalRecordRejectionOrNone(
                    StrictUtf8.GetString(child.StandardError));
        }
        catch (DecoderFallbackException)
        {
            return "None";
        }
    }

    private static string GetManagedFailurePhase(
        PlaybackControlledWindowsAudioChildProcessResult child)
    {
        try
        {
            return PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
                .GetManagedFailurePhaseOrNone(StrictUtf8.GetString(child.StandardError));
        }
        catch (DecoderFallbackException)
        {
            return "None";
        }
    }

    private static bool IsCompletedRecord(
        int childExitCode,
        string standardOutput,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options) =>
        childExitCode == PlaybackControlledWindowsAudioRenderEvidenceCommand.CompletedExitCode &&
        PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.TryParse(
            standardOutput,
            out var record) &&
        record is not null &&
        string.Equals(
            record.FixtureSha256,
            options.FixtureSha256,
            StringComparison.OrdinalIgnoreCase) &&
        record.Result == PlaybackControlledWindowsPhysicalRunResult.Completed &&
        record.EvidenceResult == PlaybackControlledWindowsOutputEvidenceResult.Accepted &&
        record.ObservationHoldMilliseconds == options.ObservationHoldMilliseconds &&
        record.AudioBufferMilliseconds == ExpectedAudioBufferMilliseconds &&
        record.PhysicalRunsExecutedByThisCommand &&
        !record.AudioAcceptanceClaimed &&
        !record.ExternalBinaryOrPayloadPublished;

    private static bool IsTerminalRecord(
        int childExitCode,
        string standardOutput,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options) =>
        childExitCode == PlaybackControlledWindowsAudioRenderEvidenceCommand.RunFailureExitCode &&
        PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
            standardOutput,
            out var record) &&
        record is not null &&
        string.Equals(
            record.FixtureSha256,
            options.FixtureSha256,
            StringComparison.OrdinalIgnoreCase) &&
        record.ObservationHoldMilliseconds == options.ObservationHoldMilliseconds &&
        record.AudioBufferMilliseconds == ExpectedAudioBufferMilliseconds &&
        record.PhysicalRunStarted &&
        !record.PhysicalRunCompleted &&
        !record.CompletedRenderEvidenceEstablished &&
        !record.AudioAcceptanceClaimed &&
        !record.ExternalBinaryOrPayloadPublished;

    private bool IsValid(
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options)
    {
        if (!File.Exists(_runnerAssemblyPath) ||
            !Directory.Exists(options.RuntimeDirectory) ||
            !File.Exists(options.FixturePath) ||
            !IsSha256(options.FixtureSha256) ||
            options.MaximumDecodeOperations is < 4 or > 4096 ||
            options.MaximumDeliveryOperations is < 4 or > 4096 ||
            options.FixturePacketBytes is < 1 or > 1_048_576 ||
            options.ObservationHoldMilliseconds is < 0 or > 30_000)
        {
            return false;
        }

        try
        {
            var runtime = Path.GetFullPath(options.RuntimeDirectory);
            var fixture = Path.GetFullPath(options.FixturePath);
            var evidence = Path.GetFullPath(options.EvidenceRecordPath);
            var failure = Path.GetFullPath(options.FailureRecordPath);
            var hasTerminal = !string.IsNullOrWhiteSpace(options.TerminalRecordPath);
            var terminal = hasTerminal
                ? Path.GetFullPath(options.TerminalRecordPath!)
                : string.Empty;
            return Path.IsPathFullyQualified(options.RuntimeDirectory) &&
                Path.IsPathFullyQualified(options.FixturePath) &&
                Path.IsPathFullyQualified(options.EvidenceRecordPath) &&
                Path.IsPathFullyQualified(options.FailureRecordPath) &&
                (!hasTerminal || Path.IsPathFullyQualified(options.TerminalRecordPath!)) &&
                !string.Equals(evidence, failure, StringComparison.OrdinalIgnoreCase) &&
                (!hasTerminal ||
                    !string.Equals(evidence, terminal, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(terminal, failure, StringComparison.OrdinalIgnoreCase)) &&
                !IsUnder(runtime, _repositoryRoot) &&
                !IsUnder(fixture, _repositoryRoot) &&
                !IsUnder(evidence, _repositoryRoot) &&
                !IsUnder(failure, _repositoryRoot) &&
                (!hasTerminal || !IsUnder(terminal, _repositoryRoot)) &&
                !File.Exists(evidence) &&
                !File.Exists(failure) &&
                (!hasTerminal || !File.Exists(terminal)) &&
                Directory.Exists(Path.GetDirectoryName(evidence)) &&
                Directory.Exists(Path.GetDirectoryName(failure)) &&
                (!hasTerminal || Directory.Exists(Path.GetDirectoryName(terminal)));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private static bool IsUnder(string candidate, string parent)
    {
        var normalizedParent = parent.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return string.Equals(candidate, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}

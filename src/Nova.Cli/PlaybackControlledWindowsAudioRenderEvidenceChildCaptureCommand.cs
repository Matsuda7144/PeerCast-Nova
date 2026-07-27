using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Cli;

internal readonly record struct PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions(
    string RuntimeDirectory,
    string FixturePath,
    string FixtureSha256,
    string EvidenceRecordPath,
    string? TerminalRecordPath,
    string FailureRecordPath,
    int MaximumDecodeOperations,
    int MaximumDeliveryOperations,
    int FixturePacketBytes,
    int ObservationHoldMilliseconds);

internal interface IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor
{
    ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        CancellationToken cancellationToken);
}

internal interface IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter
{
    ValueTask PromoteAsync(
        string destination,
        string content,
        CancellationToken cancellationToken);
}

internal sealed class SystemPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter :
    IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter
{
    public async ValueTask PromoteAsync(
        string destination,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The record directory is unavailable.");
        var temporary = Path.Combine(
            directory,
            $".pcn-audio-render-capture-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                    temporary,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, destination);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
            }
        }
    }
}

internal sealed class SystemPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor :
    IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor
{
    public async ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };
        AddArguments(startInfo, runnerAssemblyPath, options);
        return await SystemPlaybackControlledWindowsAudioChildProcessExecutor.ExecuteProcessAsync(
                startInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void AddArguments(
        ProcessStartInfo startInfo,
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerAssemblyPath);
        Add(startInfo, runnerAssemblyPath);
        Add(startInfo, PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument);
        Add(startInfo, "--runtime-directory");
        Add(startInfo, options.RuntimeDirectory);
        Add(startInfo, "--controlled-fixture");
        Add(startInfo, options.FixturePath);
        Add(startInfo, "--fixture-sha256");
        Add(startInfo, options.FixtureSha256);
        Add(startInfo, "--output-kind");
        Add(startInfo, "audio");
        Add(startInfo, "--max-decode-operations");
        AddInvariant(startInfo, options.MaximumDecodeOperations);
        Add(startInfo, "--max-delivery-operations");
        AddInvariant(startInfo, options.MaximumDeliveryOperations);
        Add(startInfo, "--fixture-packet-bytes");
        AddInvariant(startInfo, options.FixturePacketBytes);
        Add(startInfo, "--observation-hold-ms");
        AddInvariant(startInfo, options.ObservationHoldMilliseconds);
    }

    private static void Add(ProcessStartInfo startInfo, string value) =>
        startInfo.ArgumentList.Add(value);

    private static void AddInvariant(ProcessStartInfo startInfo, int value) =>
        Add(startInfo, value.ToString(CultureInfo.InvariantCulture));
}

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
                childProcessResultCaptured: true)
            .ConfigureAwait(false);
    }

    internal static string SerializeFailureRecord(
        int childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage = "None") =>
        SerializeFailureRecord(
            childExitCode,
            stdoutByteCount,
            stderrByteCount,
            lastNativeAudioStage,
            controlledRunResult: "None",
            terminalRecordRejection: "None",
            childProcessResultCaptured: true);

    internal static string SerializeFailureRecord(
        int? childExitCode,
        long stdoutByteCount,
        long stderrByteCount,
        string lastNativeAudioStage,
        string controlledRunResult,
        string terminalRecordRejection,
        bool childProcessResultCaptured)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stdoutByteCount);
        ArgumentOutOfRangeException.ThrowIfNegative(stderrByteCount);
        var available = childExitCode.HasValue;
        var normalizedExitCode = childExitCode.GetValueOrDefault();
        return PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(
            new(
                PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentSchemaVersion,
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
        bool childProcessResultCaptured)
    {
        var failureRecord = SerializeFailureRecord(
            childExitCode,
            stdoutByteCount,
            stderrByteCount,
            lastNativeAudioStage,
            controlledRunResult,
            terminalRecordRejection,
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

public static class PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
{
    public const string Argument = "--capture-controlled-windows-audio-render-evidence";
    public const int EvidenceRecordProducedExitCode = 0;
    public const int TerminalRecordProducedExitCode = 1;
    public const int SanitizedFailureExitCode = 2;
    public const int InvalidConfigurationExitCode = 3;

    public static ValueTask<int> RunAsync(
        string[] arguments,
        string runnerAssemblyPath,
        TextWriter output,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            arguments,
            runnerAssemblyPath,
            Directory.GetCurrentDirectory(),
            output,
            new SystemPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor(),
            cancellationToken);

    internal static async ValueTask<int> RunAsync(
        string[] arguments,
        string runnerAssemblyPath,
        string repositoryRoot,
        TextWriter output,
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return InvalidConfigurationExitCode;
        }

        var composition =
            new PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
                executor,
                runnerAssemblyPath,
                repositoryRoot);
        var exitCode = await composition.RunAsync(options, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                exitCode switch
                {
                    EvidenceRecordProducedExitCode => "Result: RenderEvidenceRecordProduced",
                    TerminalRecordProducedExitCode => "Result: TerminalRenderRecordProduced",
                    SanitizedFailureExitCode => "Result: SanitizedFailureRecordProduced",
                    _ => "Result: CaptureIntegrationFailure",
                })
            .ConfigureAwait(false);
        return exitCode;
    }

    internal static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options)
    {
        options = default;
        if (Count(arguments, Argument) != 1 ||
            !TryGetOne(arguments, "--runtime-directory", out var runtime) ||
            !TryGetOne(arguments, "--controlled-fixture", out var fixture) ||
            !TryGetOne(arguments, "--fixture-sha256", out var fixtureSha256) ||
            !TryGetOne(
                arguments,
                "--render-evidence-record-path",
                out var evidenceRecord) ||
            !TryGetOptionalOne(
                arguments,
                "--terminal-render-record-path",
                out var terminalRecord) ||
            !TryGetOne(arguments, "--failure-record-path", out var failureRecord) ||
            !TryGetBounded(
                arguments,
                "--max-decode-operations",
                4,
                4096,
                out var maximumDecodeOperations) ||
            !TryGetBounded(
                arguments,
                "--max-delivery-operations",
                4,
                4096,
                out var maximumDeliveryOperations) ||
            !TryGetBounded(
                arguments,
                "--fixture-packet-bytes",
                1,
                1_048_576,
                out var fixturePacketBytes) ||
            !TryGetBounded(
                arguments,
                "--observation-hold-ms",
                0,
                30_000,
                out var observationHoldMilliseconds))
        {
            return false;
        }

        options = new(
            runtime,
            fixture,
            fixtureSha256,
            evidenceRecord,
            terminalRecord,
            failureRecord,
            maximumDecodeOperations,
            maximumDeliveryOperations,
            fixturePacketBytes,
            observationHoldMilliseconds);
        return true;
    }

    private static bool TryGetBounded(
        string[] arguments,
        string name,
        int minimum,
        int maximum,
        out int value)
    {
        value = 0;
        return TryGetOne(arguments, name, out var text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum;
    }

    private static bool TryGetOptionalOne(
        string[] arguments,
        string name,
        out string? value)
    {
        value = null;
        var matches = GetMatches(arguments, name);
        if (matches.Count == 0)
        {
            return true;
        }
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0]))
        {
            return false;
        }
        value = matches[0];
        return true;
    }

    private static bool TryGetOne(
        string[] arguments,
        string name,
        out string value)
    {
        value = string.Empty;
        var matches = GetMatches(arguments, name);
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0]))
        {
            return false;
        }
        value = matches[0];
        return true;
    }

    private static List<string> GetMatches(string[] arguments, string name)
    {
        var matches = new List<string>();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(arguments[index + 1]);
            }
        }
        return matches;
    }

    private static int Count(string[] arguments, string value) =>
        arguments.Count(argument => string.Equals(
            argument,
            value,
            StringComparison.OrdinalIgnoreCase));
}

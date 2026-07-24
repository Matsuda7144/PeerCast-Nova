using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Cli;

internal readonly record struct PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions(
    string RuntimeDirectory,
    string FixturePath,
    string FixtureSha256,
    string EvidenceRecordPath,
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
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor _executor;
    private readonly string _runnerAssemblyPath;
    private readonly string _repositoryRoot;

    public PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
        string runnerAssemblyPath,
        string repositoryRoot)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                .InvalidConfigurationExitCode;
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

        try
        {
            if (child.ExitCode == PlaybackControlledWindowsAudioRenderEvidenceCommand
                    .CompletedExitCode &&
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
                record.PhysicalRunsExecutedByThisCommand &&
                !record.AudioAcceptanceClaimed &&
                !record.ExternalBinaryOrPayloadPublished)
            {
                await PromoteAtomicallyAsync(
                        options.EvidenceRecordPath,
                        standardOutput,
                        cancellationToken)
                    .ConfigureAwait(false);
                return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                    .EvidenceRecordProducedExitCode;
            }

            var failureRecord = SerializeFailureRecord(
                child.ExitCode,
                child.StandardOutput.LongLength,
                child.StandardError.LongLength);
            await PromoteAtomicallyAsync(
                    options.FailureRecordPath,
                    failureRecord,
                    cancellationToken)
                .ConfigureAwait(false);
            return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                .SanitizedFailureExitCode;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
                .InvalidConfigurationExitCode;
        }
    }

    internal static string SerializeFailureRecord(
        int childExitCode,
        long stdoutByteCount,
        long stderrByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stdoutByteCount);
        ArgumentOutOfRangeException.ThrowIfNegative(stderrByteCount);
        var builder = new StringBuilder(768);
        Append(
            builder,
            "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 1");
        Append(builder, "Milestone: 0.0.10d Phase 5");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: RenderEvidenceRecordNotProduced");
        Append(
            builder,
            "Child-Exit-Code-Signed: " +
            childExitCode.ToString(CultureInfo.InvariantCulture));
        Append(builder, $"Child-Exit-Code-Hex: {unchecked((uint)childExitCode):X8}");
        Append(
            builder,
            $"Stdout-Bytes: {stdoutByteCount.ToString(CultureInfo.InvariantCulture)}");
        Append(
            builder,
            $"Stderr-Bytes: {stderrByteCount.ToString(CultureInfo.InvariantCulture)}");
        Append(builder, "Raw-Stdout-Published: False");
        Append(builder, "Raw-Stderr-Published: False");
        Append(builder, "Canonical-Render-Evidence-Record-Produced: False");
        Append(builder, "Child-Process-Executed: True");
        Append(builder, "Physical-Run-Completion-Established: False");
        Append(builder, "Audio-Acceptance-Claimed: False");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(
            builder,
            "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE");
        return builder.ToString();
    }

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
            return Path.IsPathFullyQualified(options.RuntimeDirectory) &&
                Path.IsPathFullyQualified(options.FixturePath) &&
                Path.IsPathFullyQualified(options.EvidenceRecordPath) &&
                Path.IsPathFullyQualified(options.FailureRecordPath) &&
                !string.Equals(evidence, failure, StringComparison.OrdinalIgnoreCase) &&
                !IsUnder(runtime, _repositoryRoot) &&
                !IsUnder(fixture, _repositoryRoot) &&
                !IsUnder(evidence, _repositoryRoot) &&
                !IsUnder(failure, _repositoryRoot) &&
                !File.Exists(evidence) && !File.Exists(failure) &&
                Directory.Exists(Path.GetDirectoryName(evidence)) &&
                Directory.Exists(Path.GetDirectoryName(failure));
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

    private static async ValueTask PromoteAtomicallyAsync(
        string destination,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The record directory is unavailable.");
        var temporary = Path.Combine(
            directory,
            $".pcn-audio-render-evidence-capture-{Guid.NewGuid():N}.tmp");
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
            File.Delete(temporary);
        }
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

public static class PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
{
    public const string Argument = "--capture-controlled-windows-audio-render-evidence";
    public const int EvidenceRecordProducedExitCode = 0;
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
}

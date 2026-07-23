using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Cli;

internal readonly record struct PlaybackControlledWindowsAudioChildCaptureOptions(
    string RuntimeDirectory,
    string FixturePath,
    string FixtureSha256,
    string AudioRecordPath,
    string FailureRecordPath,
    int MaximumDecodeOperations,
    int MaximumDeliveryOperations,
    int FixturePacketBytes,
    int ObservationHoldMilliseconds);

internal readonly record struct PlaybackControlledWindowsAudioChildProcessResult(
    int ExitCode,
    byte[] StandardOutput,
    byte[] StandardError);

internal interface IPlaybackControlledWindowsAudioChildProcessExecutor
{
    ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioChildCaptureOptions options,
        CancellationToken cancellationToken);
}

internal sealed class SystemPlaybackControlledWindowsAudioChildProcessExecutor :
    IPlaybackControlledWindowsAudioChildProcessExecutor
{
    public async ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioChildCaptureOptions options,
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
        return await ExecuteProcessAsync(startInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<PlaybackControlledWindowsAudioChildProcessResult>
        ExecuteProcessAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                "The controlled physical child process did not start.");
        }

        await using var standardOutput = new MemoryStream();
        await using var standardError = new MemoryStream();
        var standardOutputTask =
            process.StandardOutput.BaseStream.CopyToAsync(standardOutput, CancellationToken.None);
        var standardErrorTask =
            process.StandardError.BaseStream.CopyToAsync(standardError, CancellationToken.None);
        await Task.WhenAll(
                standardOutputTask,
                standardErrorTask,
                process.WaitForExitAsync(CancellationToken.None))
            .ConfigureAwait(false);

        return new(
            process.ExitCode,
            standardOutput.ToArray(),
            standardError.ToArray());
    }

    internal static void AddArguments(
        ProcessStartInfo startInfo,
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioChildCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerAssemblyPath);

        Add(startInfo, runnerAssemblyPath);
        Add(startInfo, PlaybackControlledWindowsPhysicalRunCommand.Argument);
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
        Add(
            startInfo,
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument);
    }

    private static void Add(ProcessStartInfo startInfo, string value) =>
        startInfo.ArgumentList.Add(value);

    private static void AddInvariant(ProcessStartInfo startInfo, int value) =>
        Add(startInfo, value.ToString(CultureInfo.InvariantCulture));
}

internal sealed class PlaybackControlledWindowsAudioChildCaptureComposition
{
    private const string ExpectedRuntimeBuild =
        "btbn.ffmpeg.20260721.win64.lgpl-shared";

    private readonly IPlaybackControlledWindowsAudioChildProcessExecutor _executor;
    private readonly string _runnerAssemblyPath;
    private readonly string _repositoryRoot;

    public PlaybackControlledWindowsAudioChildCaptureComposition(
        IPlaybackControlledWindowsAudioChildProcessExecutor executor,
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
        PlaybackControlledWindowsAudioChildCaptureOptions options,
        CancellationToken cancellationToken)
    {
        if (!IsValid(options))
        {
            return PlaybackControlledWindowsAudioChildCaptureCommand.InvalidConfigurationExitCode;
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
            return PlaybackControlledWindowsAudioChildCaptureCommand
                .InvalidConfigurationExitCode;
        }

        var standardOutput = Encoding.UTF8.GetString(child.StandardOutput);
        var standardError = Encoding.UTF8.GetString(child.StandardError);
        try
        {
            if (PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .IsCanonicalAudioRecord(
                    standardOutput,
                    ExpectedRuntimeBuild,
                    options.FixtureSha256))
            {
                await PromoteAtomicallyAsync(
                        options.AudioRecordPath,
                        standardOutput,
                        cancellationToken)
                    .ConfigureAwait(false);
                return child.ExitCode == 0
                    ? PlaybackControlledWindowsAudioChildCaptureCommand.SuccessExitCode
                    : PlaybackControlledWindowsAudioChildCaptureCommand
                        .CanonicalChildFailureExitCode;
            }

            var snapshot =
                PlaybackControlledWindowsAudioProcessFailureDiagnostics.Analyze(
                    child.ExitCode,
                    standardOutput,
                    child.StandardOutput.LongLength,
                    standardError,
                    child.StandardError.LongLength);
            var failureRecord =
                PlaybackControlledWindowsAudioProcessFailureDiagnostics
                    .SerializeFailureRecord(snapshot);
            await PromoteAtomicallyAsync(
                    options.FailureRecordPath,
                    failureRecord,
                    cancellationToken)
                .ConfigureAwait(false);
            return PlaybackControlledWindowsAudioChildCaptureCommand
                .SanitizedFailureExitCode;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return PlaybackControlledWindowsAudioChildCaptureCommand
                .InvalidConfigurationExitCode;
        }
    }

    private bool IsValid(PlaybackControlledWindowsAudioChildCaptureOptions options)
    {
        if (!File.Exists(_runnerAssemblyPath) ||
            !Directory.Exists(options.RuntimeDirectory) ||
            !File.Exists(options.FixturePath) ||
            !IsSha256(options.FixtureSha256) ||
            options.MaximumDecodeOperations is < 1 or > 4096 ||
            options.MaximumDeliveryOperations is < 1 or > 4096 ||
            options.FixturePacketBytes is < 1 or > 1_048_576 ||
            options.ObservationHoldMilliseconds is < 0 or > 30_000)
        {
            return false;
        }

        try
        {
            var runtime = Path.GetFullPath(options.RuntimeDirectory);
            var fixture = Path.GetFullPath(options.FixturePath);
            var audio = Path.GetFullPath(options.AudioRecordPath);
            var failure = Path.GetFullPath(options.FailureRecordPath);
            return Path.IsPathFullyQualified(options.RuntimeDirectory) &&
                Path.IsPathFullyQualified(options.FixturePath) &&
                Path.IsPathFullyQualified(options.AudioRecordPath) &&
                Path.IsPathFullyQualified(options.FailureRecordPath) &&
                !IsUnder(runtime, _repositoryRoot) &&
                !IsUnder(fixture, _repositoryRoot) &&
                !IsUnder(audio, _repositoryRoot) &&
                !IsUnder(failure, _repositoryRoot) &&
                !string.Equals(audio, failure, StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(audio) &&
                !File.Exists(failure) &&
                Directory.Exists(Path.GetDirectoryName(audio)) &&
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
            $".pcn-audio-capture-{Guid.NewGuid():N}.tmp");
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
}

public static class PlaybackControlledWindowsAudioChildCaptureCommand
{
    public const string Argument = "--capture-controlled-windows-audio-child";
    public const int SuccessExitCode = 0;
    public const int CanonicalChildFailureExitCode = 1;
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
            new SystemPlaybackControlledWindowsAudioChildProcessExecutor(),
            cancellationToken);

    internal static async ValueTask<int> RunAsync(
        string[] arguments,
        string runnerAssemblyPath,
        string repositoryRoot,
        TextWriter output,
        IPlaybackControlledWindowsAudioChildProcessExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return InvalidConfigurationExitCode;
        }

        var composition = new PlaybackControlledWindowsAudioChildCaptureComposition(
            executor,
            runnerAssemblyPath,
            repositoryRoot);
        var exitCode = await composition.RunAsync(options, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                exitCode switch
                {
                    SuccessExitCode => "Result: CanonicalAudioRecordProduced",
                    CanonicalChildFailureExitCode =>
                        "Result: CanonicalAudioRecordProducedWithChildFailure",
                    SanitizedFailureExitCode => "Result: SanitizedFailureRecordProduced",
                    _ => "Result: CaptureIntegrationFailure",
                })
            .ConfigureAwait(false);
        return exitCode;
    }

    internal static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsAudioChildCaptureOptions options)
    {
        options = default;
        if (Count(arguments, Argument) != 1 ||
            !TryGetOne(arguments, "--runtime-directory", out var runtime) ||
            !TryGetOne(arguments, "--controlled-fixture", out var fixture) ||
            !TryGetOne(arguments, "--fixture-sha256", out var fixtureSha256) ||
            !TryGetOne(arguments, "--audio-record-path", out var audioRecord) ||
            !TryGetOne(arguments, "--failure-record-path", out var failureRecord) ||
            !TryGetBounded(
                arguments,
                "--max-decode-operations",
                1,
                4096,
                out var maximumDecodeOperations) ||
            !TryGetBounded(
                arguments,
                "--max-delivery-operations",
                1,
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
            audioRecord,
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
        value = default;
        return TryGetOne(arguments, name, out var text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= minimum &&
            value <= maximum;
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
        arguments.Count(argument =>
            string.Equals(argument, value, StringComparison.OrdinalIgnoreCase));
}

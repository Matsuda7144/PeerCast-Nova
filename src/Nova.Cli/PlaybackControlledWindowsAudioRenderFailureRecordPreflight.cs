using System.Globalization;

namespace PeerCast.Nova.Cli;

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
                $"Terminal-Record-Rejection: {child?.TerminalRecordRejection ?? "None"}")
            .ConfigureAwait(false);
        if (child?.ManagedFailurePhase is { } managedFailurePhase &&
            !string.Equals(managedFailurePhase, "None", StringComparison.Ordinal))
        {
            await output.WriteLineAsync($"Managed-Failure-Phase: {managedFailurePhase}")
                .ConfigureAwait(false);
        }
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

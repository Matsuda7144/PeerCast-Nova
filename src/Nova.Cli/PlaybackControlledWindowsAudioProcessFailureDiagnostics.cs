using System.Globalization;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsProcessStderrClassification
{
    None,
    ManagedUnhandledException,
    DotNetFatalError,
    NativeProcessTermination,
    UnknownNonZeroExit,
}

public readonly record struct PlaybackControlledWindowsAudioProcessFailureSnapshot(
    int ChildExitCode,
    long StdoutByteCount,
    long StderrByteCount,
    PlaybackControlledWindowsProcessStderrClassification StderrClassification,
    string LastNativeAudioStage);

/// <summary>
/// Sanitizes one controlled audio child-process failure. Raw process output is
/// accepted only in memory and is never included in the returned record.
/// </summary>
public static class PlaybackControlledWindowsAudioProcessFailureDiagnostics
{
    public const string ManagedUnhandledExceptionMarker =
        "PCN-CONTROLLED-RUNNER-FAILURE: ManagedUnhandledException";
    private const string NativeStagePrefix = "PCN-AUDIO-NATIVE-STAGE: ";
    private const string TerminalRecordRejectionPrefix =
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: ";

    public static bool IsCanonicalAudioRecord(
        string? standardOutput,
        string expectedRuntimeBuildId,
        string expectedFixtureSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRuntimeBuildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFixtureSha256);
        return PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse(
                standardOutput,
                out var record) &&
            record is not null &&
            record.Kind == PlaybackControlledWindowsOutputValidationKind.Audio &&
            string.Equals(
                record.RuntimeBuildId,
                expectedRuntimeBuildId,
                StringComparison.Ordinal) &&
            string.Equals(
                record.FixtureSha256,
                expectedFixtureSha256,
                StringComparison.OrdinalIgnoreCase) &&
            !record.ExternalBinaryOrPayloadPublished;
    }

    public static PlaybackControlledWindowsAudioProcessFailureSnapshot Analyze(
        int childExitCode,
        string? standardOutput,
        string? standardError)
    {
        standardOutput ??= string.Empty;
        standardError ??= string.Empty;
        return Analyze(
            childExitCode,
            standardOutput,
            Encoding.UTF8.GetByteCount(standardOutput),
            standardError,
            Encoding.UTF8.GetByteCount(standardError));
    }

    public static PlaybackControlledWindowsAudioProcessFailureSnapshot Analyze(
        int childExitCode,
        string? standardOutput,
        long standardOutputByteCount,
        string? standardError,
        long standardErrorByteCount)
    {
        standardOutput ??= string.Empty;
        standardError ??= string.Empty;
        if (standardOutputByteCount < 0 || standardErrorByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(standardOutputByteCount),
                "Captured process byte counts must be non-negative.");
        }

        return new(
            childExitCode,
            standardOutputByteCount,
            standardErrorByteCount,
            ClassifyStandardError(childExitCode, standardError),
            GetLastNativeAudioStage(standardError));
    }

    public static string SerializeFailureRecord(
        PlaybackControlledWindowsAudioProcessFailureSnapshot snapshot)
    {
        if (snapshot.StdoutByteCount < 0 ||
            snapshot.StderrByteCount < 0 ||
            !Enum.IsDefined(snapshot.StderrClassification) ||
            !IsNativeAudioStageOrNone(snapshot.LastNativeAudioStage))
        {
            throw new ArgumentException(
                "The controlled process failure snapshot is invalid.",
                nameof(snapshot));
        }

        var builder = new StringBuilder(768);
        Append(builder, "PEERCAST-NOVA-CONTROLLED-WINDOWS-PROCESS-FAILURE 1");
        Append(builder, "Milestone: 0.0.10d Phase 5");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: ProcessTerminatedBeforeCanonicalRecord");
        Append(
            builder,
            $"Child-Exit-Code-Signed: " +
            snapshot.ChildExitCode.ToString(CultureInfo.InvariantCulture));
        Append(
            builder,
            $"Child-Exit-Code-Hex: {unchecked((uint)snapshot.ChildExitCode):X8}");
        Append(
            builder,
            $"Stdout-Bytes: {snapshot.StdoutByteCount.ToString(CultureInfo.InvariantCulture)}");
        Append(
            builder,
            $"Stderr-Bytes: {snapshot.StderrByteCount.ToString(CultureInfo.InvariantCulture)}");
        Append(builder, $"Stderr-Classification: {snapshot.StderrClassification}");
        Append(builder, $"Last-Native-Audio-Stage: {snapshot.LastNativeAudioStage}");
        Append(builder, "Canonical-Audio-Record-Produced: False");
        Append(builder, "Audio-Preflight-Executed: False");
        Append(builder, "Physical-Runs-Executed-By-This-Command: True");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(builder, "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-PROCESS-FAILURE");
        return builder.ToString();
    }

    private static PlaybackControlledWindowsProcessStderrClassification
        ClassifyStandardError(int childExitCode, string standardError)
    {
        var lines = GetLines(standardError);
        if (lines.Any(line => string.Equals(
                line,
                ManagedUnhandledExceptionMarker,
                StringComparison.Ordinal)))
        {
            return PlaybackControlledWindowsProcessStderrClassification.ManagedUnhandledException;
        }

        if (lines.Any(line =>
                line.StartsWith("A fatal error was encountered.", StringComparison.Ordinal) ||
                line.StartsWith("Fatal error.", StringComparison.Ordinal)))
        {
            return PlaybackControlledWindowsProcessStderrClassification.DotNetFatalError;
        }

        if (childExitCode < 0)
        {
            return PlaybackControlledWindowsProcessStderrClassification.NativeProcessTermination;
        }

        var hasNonStageError = lines.Any(line =>
            line.Length > 0 && !TryParseStage(line, out _));
        if (childExitCode == 0 && !hasNonStageError)
        {
            return PlaybackControlledWindowsProcessStderrClassification.None;
        }

        return PlaybackControlledWindowsProcessStderrClassification.UnknownNonZeroExit;
    }

    private static string GetLastNativeAudioStage(string standardError)
    {
        var result = "None";
        foreach (var line in GetLines(standardError))
        {
            if (TryParseStage(line, out var stage))
            {
                result = stage.ToString();
            }
        }

        return result;
    }

    private static bool TryParseStage(
        string line,
        out PlaybackWindowsAudioNativeStage stage)
    {
        stage = default;
        return line.StartsWith(NativeStagePrefix, StringComparison.Ordinal) &&
            Enum.TryParse(
                line[NativeStagePrefix.Length..],
                ignoreCase: false,
                out stage) &&
            Enum.IsDefined(stage) &&
            string.Equals(
                line,
                NativeStagePrefix + stage,
                StringComparison.Ordinal);
    }

    internal static bool IsNativeAudioStageOrNone(string value) =>
        string.Equals(value, "None", StringComparison.Ordinal) ||
        Enum.TryParse<PlaybackWindowsAudioNativeStage>(
            value,
            ignoreCase: false,
            out var stage) &&
        Enum.IsDefined(stage) &&
        string.Equals(value, stage.ToString(), StringComparison.Ordinal);

    internal static string GetControlledRunResultOrNone(string? standardOutput)
    {
        if (string.IsNullOrEmpty(standardOutput))
        {
            return "None";
        }

        var normalized = standardOutput.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        const string prefix = "Result: ";
        if (normalized.Contains('\n') ||
            !normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            !Enum.TryParse<PlaybackControlledWindowsPhysicalRunResult>(
                normalized[prefix.Length..],
                ignoreCase: false,
                out var result) ||
            !IsControlledRunFailureResult(result) ||
            !string.Equals(normalized, prefix + result, StringComparison.Ordinal))
        {
            return "None";
        }

        return result.ToString();
    }

    internal static bool IsControlledRunResultOrNone(string value) =>
        string.Equals(value, "None", StringComparison.Ordinal) ||
        Enum.TryParse<PlaybackControlledWindowsPhysicalRunResult>(
            value,
            ignoreCase: false,
            out var result) &&
        IsControlledRunFailureResult(result) &&
        string.Equals(value, result.ToString(), StringComparison.Ordinal);

    internal static string GetTerminalRecordRejectionOrNone(string? standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return "None";
        }

        var result = "None";
        var count = 0;
        foreach (var line in GetLines(standardError))
        {
            if (!line.StartsWith(TerminalRecordRejectionPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            count++;
            if (count != 1 ||
                !Enum.TryParse<PlaybackControlledWindowsAudioTerminalRecordRejection>(
                    line[TerminalRecordRejectionPrefix.Length..],
                    ignoreCase: false,
                    out var rejection) ||
                !IsTerminalRecordRejection(rejection) ||
                !string.Equals(
                    line,
                    TerminalRecordRejectionPrefix + rejection,
                    StringComparison.Ordinal))
            {
                return "None";
            }

            result = rejection.ToString();
        }

        return result;
    }

    internal static bool IsTerminalRecordRejectionOrNone(string value) =>
        string.Equals(value, "None", StringComparison.Ordinal) ||
        Enum.TryParse<PlaybackControlledWindowsAudioTerminalRecordRejection>(
            value,
            ignoreCase: false,
            out var rejection) &&
        IsTerminalRecordRejection(rejection) &&
        string.Equals(value, rejection.ToString(), StringComparison.Ordinal);

    private static bool IsControlledRunFailureResult(
        PlaybackControlledWindowsPhysicalRunResult result) =>
        result is PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration or
            PlaybackControlledWindowsPhysicalRunResult.RunnerRejected or
            PlaybackControlledWindowsPhysicalRunResult.LifecycleRejected or
            PlaybackControlledWindowsPhysicalRunResult.FixtureRejected or
            PlaybackControlledWindowsPhysicalRunResult.DecodeRejected or
            PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected or
            PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected or
            PlaybackControlledWindowsPhysicalRunResult.Cancelled or
            PlaybackControlledWindowsPhysicalRunResult.AlreadyRunning or
            PlaybackControlledWindowsPhysicalRunResult.Faulted or
            PlaybackControlledWindowsPhysicalRunResult.Disposed;

    private static bool IsTerminalRecordRejection(
        PlaybackControlledWindowsAudioTerminalRecordRejection rejection) =>
        rejection != PlaybackControlledWindowsAudioTerminalRecordRejection.None &&
        Enum.IsDefined(rejection);

    private static string[] GetLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

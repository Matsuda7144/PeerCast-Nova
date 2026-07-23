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
            !IsStageOrNone(snapshot.LastNativeAudioStage))
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

    private static bool IsStageOrNone(string value) =>
        string.Equals(value, "None", StringComparison.Ordinal) ||
        Enum.TryParse<PlaybackWindowsAudioNativeStage>(
            value,
            ignoreCase: false,
            out var stage) &&
        Enum.IsDefined(stage) &&
        string.Equals(value, stage.ToString(), StringComparison.Ordinal);

    private static string[] GetLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

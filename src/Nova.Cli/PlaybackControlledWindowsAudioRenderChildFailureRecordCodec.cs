using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Cli;

public static class PlaybackControlledWindowsAudioRenderChildFailureRecordCodec
{
    public const int CurrentSchemaVersion = 5;
    public const int ManagedFailurePhaseSchemaVersion = 6;
    public const string CurrentMilestone = "0.0.10d Phase 6";
    public const int MaximumRecordCharacterCount = 32 * 1024;

    private const int LegacySchemaVersion = 2;
    private const int NativeStageSchemaVersion = 3;
    private const int ControlledRunResultSchemaVersion = 4;
    private const string LegacyHeader =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 2";
    private const string NativeStageHeader =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 3";
    private const string ControlledRunResultHeader =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 4";
    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 5";
    private const string ManagedFailurePhaseHeader =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 6";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE";

    public static string Serialize(
        PlaybackControlledWindowsAudioRenderChildFailureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsValid(record))
        {
            throw new ArgumentException(
                "The controlled audio child failure record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(960);
        Append(builder, HeaderFor(record.SchemaVersion));
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: RenderEvidenceRecordNotProduced");
        Append(builder, $"Child-Exit-Code-Available: {record.ChildExitCodeAvailable}");
        Append(
            builder,
            "Child-Exit-Code-Signed: " +
            record.ChildExitCode.ToString(CultureInfo.InvariantCulture));
        Append(builder, $"Child-Exit-Code-Hex: {unchecked((uint)record.ChildExitCode):X8}");
        Append(
            builder,
            $"Stdout-Bytes: {record.StdoutByteCount.ToString(CultureInfo.InvariantCulture)}");
        Append(
            builder,
            $"Stderr-Bytes: {record.StderrByteCount.ToString(CultureInfo.InvariantCulture)}");
        if (record.SchemaVersion >= NativeStageSchemaVersion)
        {
            Append(builder, $"Last-Native-Audio-Stage: {record.LastNativeAudioStage}");
        }
        if (record.SchemaVersion >= ControlledRunResultSchemaVersion)
        {
            Append(builder, $"Controlled-Run-Result: {record.ControlledRunResult}");
        }
        if (record.SchemaVersion >= CurrentSchemaVersion)
        {
            Append(
                builder,
                $"Terminal-Record-Rejection: {record.TerminalRecordRejection}");
        }
        if (record.SchemaVersion == ManagedFailurePhaseSchemaVersion)
        {
            Append(builder, $"Managed-Failure-Phase: {record.ManagedFailurePhase}");
        }
        Append(builder, "Raw-Stdout-Published: False");
        Append(builder, "Raw-Stderr-Published: False");
        Append(builder, "Canonical-Render-Evidence-Record-Produced: False");
        Append(builder, "Canonical-Terminal-Render-Record-Produced: False");
        Append(builder, $"Child-Process-Result-Captured: {record.ChildProcessResultCaptured}");
        Append(builder, "Physical-Run-Completion-Established: False");
        Append(builder, "Audio-Acceptance-Claimed: False");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(builder, Footer);
        if (builder.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio child failure record exceeds its size limit.");
        }

        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioRenderChildFailureRecord? record)
    {
        record = null;
        return TryParseVersion(
                text,
                ManagedFailurePhaseSchemaVersion,
                ManagedFailurePhaseHeader,
                expectedLineCount: 22,
                out record) ||
            TryParseVersion(
                text,
                CurrentSchemaVersion,
                Header,
                expectedLineCount: 21,
                out record) ||
            TryParseVersion(
                text,
                ControlledRunResultSchemaVersion,
                ControlledRunResultHeader,
                expectedLineCount: 20,
                out record) ||
            TryParseVersion(
                text,
                NativeStageSchemaVersion,
                NativeStageHeader,
                expectedLineCount: 19,
                out record) ||
            TryParseVersion(
                text,
                LegacySchemaVersion,
                LegacyHeader,
                expectedLineCount: 18,
                out record);
    }

    private static bool TryParseVersion(
        string? text,
        int schemaVersion,
        string header,
        int expectedLineCount,
        out PlaybackControlledWindowsAudioRenderChildFailureRecord? record)
    {
        record = null;
        if (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryGetLines(
                text,
                MaximumRecordCharacterCount,
                expectedLineCount,
                out var normalized,
                out var lines) ||
            !string.Equals(lines[0], header, StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[1],
                "Milestone: ",
                out var milestone) ||
            !string.Equals(lines[2], "Kind: Audio", StringComparison.Ordinal) ||
            !string.Equals(
                lines[3],
                "Result: RenderEvidenceRecordNotProduced",
                StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadBoolean(
                lines[4],
                "Child-Exit-Code-Available: ",
                out var exitAvailable) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadInt32(
                lines[5],
                "Child-Exit-Code-Signed: ",
                out var exitCode) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadHexUInt32(
                lines[6],
                "Child-Exit-Code-Hex: ",
                out var exitCodeHex) ||
            exitCodeHex != unchecked((uint)exitCode) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadInt64(
                lines[7],
                "Stdout-Bytes: ",
                out var stdoutBytes) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadInt64(
                lines[8],
                "Stderr-Bytes: ",
                out var stderrBytes))
        {
            return false;
        }

        var index = 9;
        var lastNativeAudioStage = "None";
        if (schemaVersion >= NativeStageSchemaVersion &&
            (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[index++],
                "Last-Native-Audio-Stage: ",
                out lastNativeAudioStage) ||
             !PlaybackControlledWindowsAudioProcessFailureDiagnostics
                 .IsNativeAudioStageOrNone(lastNativeAudioStage)))
        {
            return false;
        }

        var controlledRunResult = "None";
        if (schemaVersion >= ControlledRunResultSchemaVersion &&
            (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[index++],
                "Controlled-Run-Result: ",
                out controlledRunResult) ||
             !PlaybackControlledWindowsAudioProcessFailureDiagnostics
                 .IsControlledRunResultOrNone(controlledRunResult)))
        {
            return false;
        }

        var terminalRecordRejection = "None";
        if (schemaVersion >= CurrentSchemaVersion &&
            (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[index++],
                "Terminal-Record-Rejection: ",
                out terminalRecordRejection) ||
             !PlaybackControlledWindowsAudioProcessFailureDiagnostics
                 .IsTerminalRecordRejectionOrNone(terminalRecordRejection)))
        {
            return false;
        }

        var managedFailurePhase = "None";
        if (schemaVersion == ManagedFailurePhaseSchemaVersion &&
            (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[index++],
                "Managed-Failure-Phase: ",
                out managedFailurePhase) ||
             !PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
                 .IsManagedFailurePhaseOrNone(managedFailurePhase)))
        {
            return false;
        }

        if (!string.Equals(
                lines[index++],
                "Raw-Stdout-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[index++],
                "Raw-Stderr-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[index++],
                "Canonical-Render-Evidence-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[index++],
                "Canonical-Terminal-Render-Record-Produced: False",
                StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadBoolean(
                lines[index++],
                "Child-Process-Result-Captured: ",
                out var childResultCaptured) ||
            !string.Equals(
                lines[index++],
                "Physical-Run-Completion-Established: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[index++],
                "Audio-Acceptance-Claimed: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[index++],
                "External-Binary-Or-Payload-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[index], Footer, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsAudioRenderChildFailureRecord(
            schemaVersion,
            milestone,
            exitAvailable,
            exitCode,
            stdoutBytes,
            stderrBytes,
            lastNativeAudioStage,
            childResultCaptured)
        {
            ControlledRunResult = controlledRunResult,
            TerminalRecordRejection = terminalRecordRejection,
            ManagedFailurePhase = managedFailurePhase,
        };
        if (!IsValid(candidate) ||
            !string.Equals(Serialize(candidate), normalized + "\n", StringComparison.Ordinal))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsValid(
        PlaybackControlledWindowsAudioRenderChildFailureRecord record) =>
        IsSupportedSchemaVersion(record.SchemaVersion) &&
        string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
        record.StdoutByteCount >= 0 &&
        record.StderrByteCount >= 0 &&
        PlaybackControlledWindowsAudioProcessFailureDiagnostics
            .IsNativeAudioStageOrNone(record.LastNativeAudioStage) &&
        PlaybackControlledWindowsAudioProcessFailureDiagnostics
            .IsControlledRunResultOrNone(record.ControlledRunResult) &&
        PlaybackControlledWindowsAudioProcessFailureDiagnostics
            .IsTerminalRecordRejectionOrNone(record.TerminalRecordRejection) &&
        PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
            .IsManagedFailurePhaseOrNone(record.ManagedFailurePhase) &&
        (record.SchemaVersion != LegacySchemaVersion ||
            string.Equals(record.LastNativeAudioStage, "None", StringComparison.Ordinal)) &&
        (record.SchemaVersion >= ControlledRunResultSchemaVersion ||
            string.Equals(record.ControlledRunResult, "None", StringComparison.Ordinal)) &&
        (record.SchemaVersion >= CurrentSchemaVersion ||
            string.Equals(record.TerminalRecordRejection, "None", StringComparison.Ordinal)) &&
        (record.SchemaVersion == ManagedFailurePhaseSchemaVersion ||
            string.Equals(record.ManagedFailurePhase, "None", StringComparison.Ordinal)) &&
        record.ChildExitCodeAvailable == record.ChildProcessResultCaptured &&
        (record.ChildExitCodeAvailable || record.ChildExitCode == 0);

    private static bool IsSupportedSchemaVersion(int schemaVersion) =>
        schemaVersion is LegacySchemaVersion or NativeStageSchemaVersion or
            ControlledRunResultSchemaVersion or CurrentSchemaVersion or
            ManagedFailurePhaseSchemaVersion;

    private static string HeaderFor(int schemaVersion) => schemaVersion switch
    {
        LegacySchemaVersion => LegacyHeader,
        NativeStageSchemaVersion => NativeStageHeader,
        ControlledRunResultSchemaVersion => ControlledRunResultHeader,
        CurrentSchemaVersion => Header,
        ManagedFailurePhaseSchemaVersion => ManagedFailurePhaseHeader,
        _ => throw new ArgumentOutOfRangeException(nameof(schemaVersion)),
    };

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

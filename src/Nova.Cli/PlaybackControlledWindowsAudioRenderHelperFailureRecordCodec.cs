using System.Text;

namespace PeerCast.Nova.Cli;

public static class PlaybackControlledWindowsAudioRenderHelperFailureRecordCodec
{
    public const int CurrentSchemaVersion = 2;
    public const string CurrentMilestone = "0.0.10d Phase 6";
    public const int MaximumRecordCharacterCount = 32 * 1024;

    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-HELPER-FAILURE 2";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-HELPER-FAILURE";

    public static string Serialize(
        PlaybackControlledWindowsAudioRenderHelperFailureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsValid(record))
        {
            throw new ArgumentException(
                "The controlled audio helper failure record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(768);
        Append(builder, Header);
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, "Kind: Audio");
        Append(builder, "Result: HelperFailure");
        Append(builder, $"Failure-Phase: {record.FailurePhase}");
        Append(builder, $"Failure-Category: {record.FailureCategory}");
        Append(builder, "Canonical-Render-Evidence-Record-Produced: False");
        Append(builder, "Canonical-Terminal-Render-Record-Produced: False");
        Append(builder, "Sanitized-Child-Failure-Record-Produced: False");
        Append(builder, "Physical-Run-Completion-Established: False");
        Append(builder, "Raw-Exception-Published: False");
        Append(builder, "Raw-Native-Stderr-Published: False");
        Append(builder, "Audio-Acceptance-Claimed: False");
        Append(builder, "External-Binary-Or-Payload-Published: False");
        Append(builder, Footer);
        if (builder.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio helper failure record exceeds its size limit.");
        }

        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioRenderHelperFailureRecord? record)
    {
        record = null;
        if (!PlaybackControlledWindowsAudioRenderFailureRecordText.TryGetLines(
                text,
                MaximumRecordCharacterCount,
                expectedLineCount: 15,
                out var normalized,
                out var lines) ||
            !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadExact(
                lines[1],
                "Milestone: ",
                out var milestone) ||
            !string.Equals(lines[2], "Kind: Audio", StringComparison.Ordinal) ||
            !string.Equals(lines[3], "Result: HelperFailure", StringComparison.Ordinal) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadEnum(
                lines[4],
                "Failure-Phase: ",
                out PlaybackControlledWindowsAudioRenderHelperFailurePhase failurePhase) ||
            !PlaybackControlledWindowsAudioRenderFailureRecordText.TryReadEnum(
                lines[5],
                "Failure-Category: ",
                out PlaybackControlledWindowsAudioRenderHelperFailureCategory failureCategory) ||
            !string.Equals(
                lines[6],
                "Canonical-Render-Evidence-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[7],
                "Canonical-Terminal-Render-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[8],
                "Sanitized-Child-Failure-Record-Produced: False",
                StringComparison.Ordinal) ||
            !string.Equals(
                lines[9],
                "Physical-Run-Completion-Established: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[10], "Raw-Exception-Published: False", StringComparison.Ordinal) ||
            !string.Equals(
                lines[11],
                "Raw-Native-Stderr-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[12], "Audio-Acceptance-Claimed: False", StringComparison.Ordinal) ||
            !string.Equals(
                lines[13],
                "External-Binary-Or-Payload-Published: False",
                StringComparison.Ordinal) ||
            !string.Equals(lines[14], Footer, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsAudioRenderHelperFailureRecord(
            CurrentSchemaVersion,
            milestone,
            failurePhase,
            failureCategory);
        if (!IsValid(candidate) ||
            !string.Equals(Serialize(candidate), normalized + "\n", StringComparison.Ordinal))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsValid(
        PlaybackControlledWindowsAudioRenderHelperFailureRecord record) =>
        record.SchemaVersion == CurrentSchemaVersion &&
        string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
        Enum.IsDefined(record.FailurePhase) &&
        record.FailurePhase != PlaybackControlledWindowsAudioRenderHelperFailurePhase.None &&
        Enum.IsDefined(record.FailureCategory) &&
        record.FailureCategory != PlaybackControlledWindowsAudioRenderHelperFailureCategory.None;

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');
}

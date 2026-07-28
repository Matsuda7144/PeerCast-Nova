namespace PeerCast.Nova.Cli;

internal static class PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
{
    private const string Prefix = "PCN-AUDIO-MANAGED-FAILURE-PHASE: ";

    internal static string GetManagedFailurePhaseOrNone(string? standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return "None";
        }

        var result = "None";
        var count = 0;
        foreach (var line in standardError.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n', StringSplitOptions.None))
        {
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            count++;
            if (count != 1 ||
                !Enum.TryParse<PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase>(
                    line[Prefix.Length..],
                    ignoreCase: false,
                    out var phase) ||
                phase == PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase.None ||
                !Enum.IsDefined(phase) ||
                !string.Equals(line, Prefix + phase, StringComparison.Ordinal))
            {
                return "None";
            }

            result = phase.ToString();
        }

        return result;
    }

    internal static bool IsManagedFailurePhaseOrNone(string value) =>
        string.Equals(value, "None", StringComparison.Ordinal) ||
        Enum.TryParse<PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase>(
            value,
            ignoreCase: false,
            out var phase) &&
        phase != PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase.None &&
        Enum.IsDefined(phase) &&
        string.Equals(value, phase.ToString(), StringComparison.Ordinal);
}

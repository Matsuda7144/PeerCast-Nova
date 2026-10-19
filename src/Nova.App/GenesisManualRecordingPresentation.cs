namespace PeerCast.Nova.App;

internal sealed record GenesisManualRecordingPresentation(
    bool CanRecord,
    bool CanStop,
    bool IsRecording,
    bool IsTransitioning,
    bool IsFailure,
    string StatusText,
    string? ErrorMessage)
{
    public string DisplayText => ErrorMessage is null
        ? StatusText
        : string.IsNullOrWhiteSpace(StatusText)
            ? ErrorMessage
            : $"{StatusText}  {ErrorMessage}";

    public static GenesisManualRecordingPresentation Unavailable { get; } =
        new(false, false, false, false, false, string.Empty, null);
}

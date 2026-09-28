namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Why one recording file write reached a valid final file.
/// </summary>
public enum RecordingFileWriteCompletionKind
{
    EndOfStream,
    Stopped,
}
